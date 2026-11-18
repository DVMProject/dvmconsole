// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*/
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using dvmconsole.Controls;
using fnecore;

namespace dvmconsole
{
    public sealed class FneConnectionSnapshot
    {
        public string SystemName { get; init; } = string.Empty;
        public bool IsConnected { get; init; }
        public bool IsBusy { get; init; }
        public bool IsStarted { get; init; }
        public bool CanSyncAliases { get; init; }
        public string AliasStatus { get; init; } = string.Empty;
        public string StatusText => IsConnected ? "Connected" : "Disconnected";
    }

    public partial class MainWindow
    {
        private sealed class FneConnectionEntry
        {
            public string SystemName { get; init; } = string.Empty;
            public Codeplug.System SystemConfig { get; init; }
            public SystemStatusBox StatusBox { get; set; }
            public PeerSystem Peer { get; set; }
            public bool IsConnected { get; set; }
            public bool IsBusy { get; set; }
            public bool DesiredStarted { get; set; }
            public bool IsInitialAutoStartPending { get; set; }
            public bool SuppressNextDisconnectChime { get; set; }
            public CancellationTokenSource AliasSyncCancellation { get; set; }
            public string AliasStatus { get; set; } = string.Empty;
            public int LastHealthPingsSent { get; set; }
            public int LastHealthPingsAcked { get; set; }
            public DateTime LastHealthProgressUtc { get; set; } = DateTime.UtcNow;
            public SemaphoreSlim Sync { get; } = new SemaphoreSlim(1, 1);
            public CancellationTokenSource Lifetime { get; } = new CancellationTokenSource();
            public long OperationVersion { get; set; }
            public EventHandler<PeerConnectedEvent> PeerConnectedHandler { get; set; }
            public Action<uint> PeerDisconnectedHandler { get; set; }
        }

        private static readonly TimeSpan FNE_HEALTH_CHECK_INTERVAL = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FNE_HEALTH_NO_PROGRESS_TIMEOUT = TimeSpan.FromMinutes(2);
        private const int FNE_HEALTH_MISSED_PING_GRACE = 3;

        private readonly Dictionary<string, FneConnectionEntry> fneConnectionEntries = new(StringComparer.OrdinalIgnoreCase);
        private FneConnectionManagerWindow fneConnectionManagerWindow;
        private DateTime nextFneHealthCheckUtc = DateTime.MinValue;
        private Task fneConnectionResetTask;

        private enum FneConnectionOperation
        {
            Start,
            Stop,
            Restart
        }

        public event Action<FneConnectionSnapshot> FneConnectionStateChanged;

        public IReadOnlyList<FneConnectionSnapshot> GetFneConnectionSnapshots()
        {
            lock (fneConnectionEntries)
            {
                return fneConnectionEntries.Values
                    .Select(CreateSnapshot)
                    .OrderBy(snapshot => snapshot.SystemName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public Task StartFneSystemAsync(string systemName)
        {
            return RunFneConnectionOperationAsync(GetFneConnectionEntry(systemName), FneConnectionOperation.Start);
        }

        public Task StopFneSystemAsync(string systemName)
        {
            return RunFneConnectionOperationAsync(GetFneConnectionEntry(systemName), FneConnectionOperation.Stop);
        }

        public Task RestartFneSystemAsync(string systemName)
        {
            return RunFneConnectionOperationAsync(GetFneConnectionEntry(systemName), FneConnectionOperation.Restart);
        }

        private async Task RunFneConnectionOperationAsync(FneConnectionEntry entry, FneConnectionOperation operation, bool showError = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => RunFneConnectionOperationAsync(entry, operation, showError)).Task.Unwrap();
                return;
            }

            if (!IsCurrentFneConnectionEntry(entry))
                return;

            long version = ++entry.OperationVersion;
            entry.DesiredStarted = operation != FneConnectionOperation.Stop;
            bool acquired = false;
            try
            {
                await entry.Sync.WaitAsync(entry.Lifetime.Token);
                acquired = true;
                if (!IsCurrentFneConnectionOperation(entry, version))
                    return;

                entry.IsBusy = true;
                PublishConnectionState(entry);
                if (!IsCurrentFneConnectionOperation(entry, version))
                    return;

                if (operation == FneConnectionOperation.Start && entry.Peer?.IsStarted == true && entry.IsConnected)
                {
                    ResetHealthMonitorBaseline(entry);
                    return;
                }

                bool shouldPlayManualDisconnectChime = operation == FneConnectionOperation.Stop &&
                    (entry.IsConnected || entry.Peer?.IsStarted == true);
                await RemovePeerForEntryAsync(entry);
                if (!IsCurrentFneConnectionOperation(entry, version))
                    return;
                ApplyDisconnectedState(entry, recordEvent: true);

                if (operation == FneConnectionOperation.Stop)
                {
                    if (shouldPlayManualDisconnectChime)
                        PlayFneConnectionChime(connected: false);
                    return;
                }

                if (operation == FneConnectionOperation.Restart)
                    await Task.Delay(250, entry.Lifetime.Token);

                if (!IsCurrentFneConnectionOperation(entry, version))
                    return;

                CreatePeerForEntry(entry);
                ResetHealthMonitorBaseline(entry);
                PeerSystem peer = entry.Peer;
                // Once Start is running it must finish before reset/stop can remove this peer.
                await Task.Run(() => peer.Start(), entry.Lifetime.Token);
            }
            catch (OperationCanceledException) when (entry.Lifetime.IsCancellationRequested)
            {
                /* reset retired this entry */
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
                if (showError && IsCurrentFneConnectionOperation(entry, version))
                {
                    MessageBox.Show($"Unable to {operation.ToString().ToLowerInvariant()} FNE system '{entry.SystemName}'. {ex.Message}", "FNE Connection Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else if (!showError && IsCurrentFneConnectionOperation(entry, version))
                {
                    Log.WriteWarning($"FNE health restart failed for '{entry.SystemName}': {ex.Message}");
                }
            }
            finally
            {
                if (acquired)
                {
                    entry.IsBusy = false;
                    // Always release even if a state subscriber throws.
                    entry.Sync.Release();
                    PublishConnectionState(entry);
                }
            }
        }

        private void RegisterFneConnection(Codeplug.System system, SystemStatusBox statusBox, bool autoStart)
        {
            if (system == null || string.IsNullOrWhiteSpace(system.Name))
                return;

            if (fneConnectionResetTask != null || GetFneConnectionEntry(system.Name) != null)
                throw new InvalidOperationException("Await ResetFneConnectionsAsync before replacing FNE connections.");

            FneConnectionEntry entry = new FneConnectionEntry
            {
                SystemName = system.Name,
                SystemConfig = system,
                AliasStatus = GetSavedAliasStatus(system),
                StatusBox = statusBox,
                IsConnected = false,
                IsBusy = false,
                DesiredStarted = autoStart,
                IsInitialAutoStartPending = autoStart
            };

            lock (fneConnectionEntries)
            {
                fneConnectionEntries[system.Name] = entry;
            }

            ApplyDisconnectedState(entry);
            PublishConnectionState(entry);

            if (autoStart)
                _ = StartFneSystemAsync(system.Name);
        }

        private async Task ResetFneConnectionsAsync()
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(ResetFneConnectionsAsync).Task.Unwrap();
                return;
            }

            if (fneConnectionResetTask != null)
            {
                await fneConnectionResetTask;
                return;
            }

            fneConnectionResetTask = ResetFneConnectionsCoreAsync();
            try
            {
                await fneConnectionResetTask;
            }
            finally
            {
                fneConnectionResetTask = null;
            }
        }

        private async Task ResetFneConnectionsCoreAsync()
        {
            // Publish the reset task before cancellation or callbacks can reenter reset.
            await Task.Yield();
            List<FneConnectionEntry> entries;
            lock (fneConnectionEntries)
            {
                entries = fneConnectionEntries.Values.ToList();
            }

            foreach (FneConnectionEntry entry in entries)
            {
                entry.DesiredStarted = false;
                entry.Lifetime.Cancel();
                CancelDeferredStartupKeyRequests(entry.SystemName);
                CancelFneAliasSync(entry);
            }

            List<Exception> errors = new List<Exception>();
            foreach (FneConnectionEntry entry in entries)
            {
                // Cancellation fences queued waiters; acquiring the gate joins an active Start/Stop.
                await entry.Sync.WaitAsync();
                try
                {
                    await RemovePeerForEntryAsync(entry);
                    lock (fneConnectionEntries)
                    {
                        if (fneConnectionEntries.TryGetValue(entry.SystemName, out FneConnectionEntry current) && ReferenceEquals(current, entry))
                            fneConnectionEntries.Remove(entry.SystemName);
                    }
                }
                catch (Exception ex)
                {
                    Log.StackTrace(ex, false);
                    errors.Add(ex);
                }
                finally
                {
                    // Retired entries can still have canceled continuations. Do not dispose their gates.
                    entry.Sync.Release();
                }
            }

            nextFneHealthCheckUtc = DateTime.MinValue;
            RefreshCommandControlsForConnectionState();
            NotifyConnectionWindowReset();
            if (errors.Count != 0)
                throw new AggregateException("Unable to stop all old FNE connections; replacement was not completed.", errors);
        }

        private FneConnectionEntry GetFneConnectionEntry(string systemName)
        {
            lock (fneConnectionEntries)
            {
                fneConnectionEntries.TryGetValue(systemName, out FneConnectionEntry entry);
                return entry;
            }
        }

        private bool IsCurrentFneConnectionEntry(FneConnectionEntry entry)
        {
            return entry != null && !isShuttingDown && fneConnectionResetTask == null && !entry.Lifetime.IsCancellationRequested &&
                ReferenceEquals(GetFneConnectionEntry(entry.SystemName), entry);
        }

        private bool IsCurrentFneConnectionOperation(FneConnectionEntry entry, long version)
        {
            return IsCurrentFneConnectionEntry(entry) && entry.OperationVersion == version;
        }

        private void CreatePeerForEntry(FneConnectionEntry entry)
        {
            if (!IsCurrentFneConnectionEntry(entry) || entry.Peer != null || fneSystemManager.GetFneSystem(entry.SystemName) != null)
                throw new InvalidOperationException("The previous FNE peer must be stopped and removed before creating a replacement.");

            PeerSystem peer = fneSystemManager.AddOrReplaceFneSystem(entry.SystemName, entry.SystemConfig, this);
            entry.Peer = peer;

            entry.PeerConnectedHandler = (sender, response) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsCurrentFneConnectionEntry(entry) || !ReferenceEquals(entry.Peer, peer) || entry.PeerConnectedHandler == null || !entry.DesiredStarted)
                        return;
                    Log.WriteLine($"FNE Peer connected: {entry.SystemName}");
                    bool wasConnected = entry.IsConnected;
                    entry.IsConnected = true;
                    if (!wasConnected)
                        AddFneEventHistoryEntry(entry, connected: true);
                    ResetHealthMonitorBaseline(entry);
                    UpdateSystemStatusBox(entry);
                    UpdateChannelConnectionVisuals(entry.SystemName);
                    RefreshCommandControlsForConnectionState();
                    PublishConnectionState(entry);
                    if (!IsCurrentFneConnectionEntry(entry) || !ReferenceEquals(entry.Peer, peer) || !entry.DesiredStarted)
                        return;
                    QueueMissingChannelKeys(entry.SystemName);
                    ScheduleDeferredStartupKeyRequests(entry.SystemName);
                    PlayFneConnectedChime(entry);
                    _ = SyncFneAliasesAsync(entry.SystemName);
                }));
            };

            entry.PeerDisconnectedHandler = _ =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsCurrentFneConnectionEntry(entry) || !ReferenceEquals(entry.Peer, peer) || entry.PeerDisconnectedHandler == null || !entry.DesiredStarted)
                        return;
                    Log.WriteLine($"FNE Peer disconnected: {entry.SystemName}");
                    ApplyDisconnectedState(entry, recordEvent: true);
                    PublishConnectionState(entry);
                    if (!IsCurrentFneConnectionEntry(entry) || !ReferenceEquals(entry.Peer, peer) || !entry.DesiredStarted)
                        return;
                    CancelDeferredStartupKeyRequests(entry.SystemName);
                    if (entry.SuppressNextDisconnectChime)
                        entry.SuppressNextDisconnectChime = false;
                    else
                        PlayFneConnectionChime(connected: false);
                }));
            };

            peer.peer.PeerConnected += entry.PeerConnectedHandler;
            peer.peer.PeerDisconnected += entry.PeerDisconnectedHandler;
        }

        private async Task RemovePeerForEntryAsync(FneConnectionEntry entry)
        {
            if (ReferenceEquals(GetFneConnectionEntry(entry.SystemName), entry))
                CancelDeferredStartupKeyRequests(entry.SystemName);
            CancelFneAliasSync(entry);
            PeerSystem peer = entry.Peer;
            if (peer?.peer != null)
            {
                if (entry.PeerConnectedHandler != null)
                    peer.peer.PeerConnected -= entry.PeerConnectedHandler;

                if (entry.PeerDisconnectedHandler != null)
                    peer.peer.PeerDisconnected -= entry.PeerDisconnectedHandler;
            }

            // Detach callbacks before stopping, but retain the peer for a retry if Stop fails.
            entry.PeerConnectedHandler = null;
            entry.PeerDisconnectedHandler = null;
            if (peer != null)
            {
                await Task.Run(() => peer.Stop());
                if (peer.IsStarted)
                    throw new InvalidOperationException($"FNE peer '{entry.SystemName}' did not stop.");

                // Manager mutation stays on the Dispatcher. Its Stop is now a verified no-op.
                if (ReferenceEquals(fneSystemManager.GetFneSystem(entry.SystemName), peer))
                    fneSystemManager.RemoveFneSystem(entry.SystemName);
            }

            entry.Peer = null;
            entry.SuppressNextDisconnectChime = false;
        }

        private void ApplyDisconnectedState(FneConnectionEntry entry, bool recordEvent = false)
        {
            if (!IsCurrentFneConnectionEntry(entry))
                return;
            CancelFneAliasSync(entry);
            bool wasConnected = entry.IsConnected;
            entry.IsConnected = false;
            if (recordEvent && wasConnected)
                AddFneEventHistoryEntry(entry, connected: false);

            UpdateSystemStatusBox(entry);
            ClearPatchPttTargetsForDisconnectedSystem(entry.SystemName);
            ResetChannelsForDisconnectedSystem(entry.SystemName);
            UpdateChannelConnectionVisuals(entry.SystemName);
            RefreshCommandControlsForConnectionState();
        }

        private void AddFneEventHistoryEntry(FneConnectionEntry entry, bool connected)
        {
            if (entry == null)
                return;

            string status = connected ? "connected" : "disconnected";
            callHistoryWindow?.AddEvent(
                "FNE",
                $"{entry.SystemName} {status}",
                GetFnePeerIdText(entry),
                GetFneEndpointText(entry),
                connected ? Brushes.LightGreen : Brushes.LightCoral);
        }

        private static string GetFnePeerIdText(FneConnectionEntry entry)
        {
            return entry?.SystemConfig?.PeerId > 0
                ? entry.SystemConfig.PeerId.ToString()
                : string.Empty;
        }

        private static string GetFneEndpointText(FneConnectionEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry?.SystemConfig?.Address))
                return string.Empty;

            return $"{entry.SystemConfig.Address.Trim()}:{entry.SystemConfig.Port}";
        }

        private void UpdateSystemStatusBox(FneConnectionEntry entry)
        {
            if (entry?.StatusBox == null)
                return;

            entry.StatusBox.Background = entry.IsConnected ? ChannelBox.GREEN_GRADIENT : ChannelBox.RED_GRADIENT;
            entry.StatusBox.ConnectionState = entry.IsConnected ? "Connected" : "Disconnected";
        }

        private void ResetChannelsForDisconnectedSystem(string systemName)
        {
            string normalizedSystemName = NormalizeChannelSystemName(systemName);
            foreach (Canvas canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                {
                    if (!string.Equals(channel.ResourceSystemName, normalizedSystemName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Codeplug.Channel disconnectedChannel = channel.ConfiguredChannel;
                    Codeplug.System disconnectedSystem = Codeplug?.GetSystemForChannel(disconnectedChannel);
                    if (disconnectedChannel != null)
                        patchManager.StopMember(systemName, disconnectedChannel.Tgid);
                    bool hadTransmitState = channel.PttState || channel.PatchForwardingTxState || channel.TxStreamId != 0;
                    SlotStatus slotStatus = FindActiveReceiveStatus(channel, disconnectedChannel);

                    EndReceiveCall(channel, disconnectedSystem, disconnectedChannel, slotStatus, DateTime.Now, discardAudio: true);

                    if (disconnectedSystem != null && disconnectedChannel != null && hadTransmitState)
                        EndTarTxRecording(channel, disconnectedSystem, disconnectedChannel);

                    EndAnalogTransmission(channel, discardPending: true);
                    EndDmrTransmission(channel, discardPending: true);
                    EndNxdnTransmission(channel, discardPending: true);
                    channel.PttState = false;
                    channel.PatchForwardingTxState = false;
                    channel.VolumeMeterLevel = 0;
                    ResetChannel(channel);
                    UpdateTabAudioIndicatorForChannel(channel);
                }
            }
        }

        private void ClearPatchPttTargetsForDisconnectedSystem(string systemName)
        {
            List<PatchPttTargetSession> sessionsToClear;
            lock (patchPttSync)
            {
                sessionsToClear = activePatchPttTargets.Values
                    .Where(session => string.Equals(session.SystemName, systemName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (PatchPttTargetSession session in sessionsToClear)
                    activePatchPttTargets.Remove(session.Key);
            }

            foreach (PatchPttTargetSession session in sessionsToClear)
            {
                if (session.Channel == null)
                    continue;

                if (session.CodeplugSystem != null && session.CodeplugChannel != null)
                    EndTarTxRecording(session.Channel, session.CodeplugSystem, session.CodeplugChannel);

                session.Channel.PatchForwardingTxState = false;
                session.Channel.PttState = false;
                session.Channel.VolumeMeterLevel = 0;
                ResetChannel(session.Channel);
            }
        }

        private bool IsFneSystemConnected(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName))
                return false;

            FneConnectionEntry entry = GetFneConnectionEntry(NormalizeChannelSystemName(systemName));
            return entry?.IsConnected == true && entry.Peer?.IsStarted == true;
        }

        private bool TryHandleDisconnectedFneEndpoint(ChannelBox channel, Codeplug.System system, string context = null)
        {
            if (channel == null || system == null)
                return false;

            string normalizedSystemName = NormalizeChannelSystemName(system.Name);
            FneConnectionEntry entry = GetFneConnectionEntry(normalizedSystemName);
            if (entry == null || IsFneSystemConnected(normalizedSystemName))
                return false;

            channel.SetFneConnectionWarning(true, $"{normalizedSystemName} FNE disconnected. Transmit disabled.");
            Log.WriteWarning($"{context ?? channel.ChannelName}: FNE system '{normalizedSystemName}' is disconnected; keeping resource selected.");
            return true;
        }

        private void RefreshAllChannelConnectionVisuals()
        {
            foreach (Canvas canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                    UpdateChannelConnectionVisual(channel);
            }
        }

        private void UpdateChannelConnectionVisuals(string systemName)
        {
            foreach (Canvas canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                {
                    if (string.Equals(channel.ResourceSystemName, NormalizeChannelSystemName(systemName), StringComparison.OrdinalIgnoreCase))
                        UpdateChannelConnectionVisual(channel);
                }
            }
        }

        private void UpdateChannelConnectionVisual(ChannelBox channel)
        {
            if (channel == null || channel.IsLocalPlayback)
                return;

            string systemName = channel.ResourceSystemName;
            bool disconnected = !IsFneSystemConnected(systemName);
            channel.SetFneConnectionWarning(
                disconnected,
                disconnected ? $"{systemName} FNE disconnected. Transmit disabled." : null);
        }

        private void ResetHealthMonitorBaseline(FneConnectionEntry entry)
        {
            if (entry?.Peer?.peer == null)
            {
                if (entry != null)
                {
                    entry.LastHealthPingsSent = 0;
                    entry.LastHealthPingsAcked = 0;
                    entry.LastHealthProgressUtc = DateTime.UtcNow;
                }
                return;
            }

            entry.LastHealthPingsSent = entry.Peer.peer.PingsSent;
            entry.LastHealthPingsAcked = entry.Peer.peer.PingsAcked;
            entry.LastHealthProgressUtc = DateTime.UtcNow;
        }

        private void CheckFneConnectionHealth(bool force = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => CheckFneConnectionHealth(force)));
                return;
            }

            if (isShuttingDown)
                return;

            DateTime now = DateTime.UtcNow;
            if (!force && now < nextFneHealthCheckUtc)
                return;

            nextFneHealthCheckUtc = now.Add(FNE_HEALTH_CHECK_INTERVAL);

            List<(FneConnectionEntry Entry, long Version)> systemsToRestart = new List<(FneConnectionEntry, long)>();
            lock (fneConnectionEntries)
            {
                foreach (FneConnectionEntry entry in fneConnectionEntries.Values)
                {
                    if (!ShouldRestartUnhealthyFneConnection(entry, now, out _))
                        continue;

                    systemsToRestart.Add((entry, entry.OperationVersion));
                }
            }

            foreach ((FneConnectionEntry entry, long version) in systemsToRestart)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsCurrentFneConnectionOperation(entry, version) ||
                        !ShouldRestartUnhealthyFneConnection(entry, DateTime.UtcNow, out string currentReason))
                        return;
                    Log.WriteWarning($"FNE health check restarting '{entry.SystemName}': {currentReason}");
                    _ = RunFneConnectionOperationAsync(entry, FneConnectionOperation.Restart, showError: false);
                }));
            }
        }

        private bool ShouldRestartUnhealthyFneConnection(FneConnectionEntry entry, DateTime now, out string reason)
        {
            reason = string.Empty;
            if (!IsCurrentFneConnectionEntry(entry) || entry.IsBusy || !entry.DesiredStarted)
                return false;

            if (entry.Peer == null || entry.Peer.peer == null || !entry.Peer.IsStarted)
            {
                reason = "peer is not started";
                return true;
            }

            if (!entry.IsConnected)
            {
                reason = "peer is not connected";
                return true;
            }

            int pingsSent = entry.Peer.peer.PingsSent;
            int pingsAcked = entry.Peer.peer.PingsAcked;

            if (pingsSent != entry.LastHealthPingsSent || pingsAcked != entry.LastHealthPingsAcked)
            {
                entry.LastHealthPingsSent = pingsSent;
                entry.LastHealthPingsAcked = pingsAcked;
                entry.LastHealthProgressUtc = now;
            }

            if (pingsSent > pingsAcked + FNE_HEALTH_MISSED_PING_GRACE)
            {
                reason = $"missed ping threshold exceeded (sent={pingsSent}, acked={pingsAcked})";
                return true;
            }

            if (now - entry.LastHealthProgressUtc > FNE_HEALTH_NO_PROGRESS_TIMEOUT)
            {
                reason = $"no ping progress for {(now - entry.LastHealthProgressUtc).TotalSeconds:F0} seconds";
                return true;
            }

            return false;
        }

        private void RefreshCommandControlsForConnectionState()
        {
            bool anyConnected;
            lock (fneConnectionEntries)
            {
                anyConnected = fneConnectionEntries.Values.Any(entry => entry.IsConnected);
            }

            if (anyConnected)
                EnableCommandControls();
            else
                DisableCommandControls();
        }

        private void PublishConnectionState(FneConnectionEntry entry)
        {
            if (!IsCurrentFneConnectionEntry(entry))
                return;
            FneConnectionSnapshot snapshot = CreateSnapshot(entry);
            FneConnectionStateChanged?.Invoke(snapshot);
        }

        private void NotifyConnectionWindowReset()
        {
            FneConnectionStateChanged?.Invoke(new FneConnectionSnapshot());
        }

        private void FneConnectionManager_Click(object sender, RoutedEventArgs e)
        {
            if (fneConnectionManagerWindow == null || !fneConnectionManagerWindow.IsLoaded)
            {
                fneConnectionManagerWindow = new FneConnectionManagerWindow
                {
                    Owner = this
                };
                fneConnectionManagerWindow.Closed += (_, _) => fneConnectionManagerWindow = null;
            }

            if (fneConnectionManagerWindow.Visibility == Visibility.Visible)
            {
                fneConnectionManagerWindow.Activate();
                return;
            }

            fneConnectionManagerWindow.Show();
            fneConnectionManagerWindow.Activate();
        }

        private static FneConnectionSnapshot CreateSnapshot(FneConnectionEntry entry)
        {
            return new FneConnectionSnapshot
            {
                SystemName = entry.SystemName,
                IsConnected = entry.IsConnected,
                IsBusy = entry.IsBusy,
                IsStarted = entry.Peer?.IsStarted == true,
                CanSyncAliases = entry.SystemConfig.SyncRadioAliases && entry.IsConnected && !entry.IsBusy && entry.AliasSyncCancellation == null,
                AliasStatus = entry.AliasStatus
            };
        }
    }
}
