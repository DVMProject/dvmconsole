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
            public int LastHealthPingsSent { get; set; }
            public int LastHealthPingsAcked { get; set; }
            public DateTime LastHealthProgressUtc { get; set; } = DateTime.UtcNow;
            public SemaphoreSlim Sync { get; } = new SemaphoreSlim(1, 1);
            public EventHandler<PeerConnectedEvent> PeerConnectedHandler { get; set; }
            public Action<uint> PeerDisconnectedHandler { get; set; }
        }

        private static readonly TimeSpan FNE_HEALTH_CHECK_INTERVAL = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FNE_HEALTH_NO_PROGRESS_TIMEOUT = TimeSpan.FromMinutes(2);
        private const int FNE_HEALTH_MISSED_PING_GRACE = 3;

        private readonly Dictionary<string, FneConnectionEntry> fneConnectionEntries = new(StringComparer.OrdinalIgnoreCase);
        private FneConnectionManagerWindow fneConnectionManagerWindow;
        private DateTime nextFneHealthCheckUtc = DateTime.MinValue;

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

        public async Task StartFneSystemAsync(string systemName)
        {
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry == null)
                return;

            entry.DesiredStarted = true;

            await entry.Sync.WaitAsync();
            try
            {
                if (entry.IsBusy)
                    return;

                entry.IsBusy = true;
                PublishConnectionState(entry);

                if (entry.Peer?.IsStarted == true && entry.IsConnected)
                {
                    ResetHealthMonitorBaseline(entry);
                    return;
                }

                if (entry.Peer?.IsStarted == true)
                {
                    await Task.Run(() => entry.Peer.Stop());
                    RemovePeerForEntry(entry);
                    ApplyDisconnectedState(entry);
                }

                CreatePeerForEntry(entry);
                ResetHealthMonitorBaseline(entry);
                await Task.Run(() => entry.Peer.Start());
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Unable to start FNE system '{systemName}'. {ex.Message}", "FNE Connection Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                entry.IsBusy = false;
                PublishConnectionState(entry);
                entry.Sync.Release();
            }
        }

        public async Task StopFneSystemAsync(string systemName)
        {
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry == null)
                return;

            entry.DesiredStarted = false;

            await entry.Sync.WaitAsync();
            try
            {
                if (entry.IsBusy)
                    return;

                entry.IsBusy = true;
                PublishConnectionState(entry);

                if (entry.Peer?.IsStarted == true)
                    await Task.Run(() => entry.Peer.Stop());

                RemovePeerForEntry(entry);
                ApplyDisconnectedState(entry);
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Unable to stop FNE system '{systemName}'. {ex.Message}", "FNE Connection Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                entry.IsBusy = false;
                PublishConnectionState(entry);
                entry.Sync.Release();
            }
        }

        public async Task RestartFneSystemAsync(string systemName)
        {
            await RestartFneSystemAsync(systemName, showError: true);
        }

        private async Task RestartFneSystemAsync(string systemName, bool showError)
        {
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry == null)
                return;

            entry.DesiredStarted = true;

            await entry.Sync.WaitAsync();
            try
            {
                if (entry.IsBusy)
                    return;

                entry.IsBusy = true;
                PublishConnectionState(entry);

                if (entry.Peer?.IsStarted == true)
                    await Task.Run(() => entry.Peer.Stop());

                RemovePeerForEntry(entry);
                ApplyDisconnectedState(entry);

                await Task.Delay(250);

                CreatePeerForEntry(entry);
                ResetHealthMonitorBaseline(entry);
                await Task.Run(() => entry.Peer.Start());
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
                if (showError)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Unable to restart FNE system '{systemName}'. {ex.Message}", "FNE Connection Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                else
                {
                    Log.WriteWarning($"FNE health restart failed for '{systemName}': {ex.Message}");
                }
            }
            finally
            {
                entry.IsBusy = false;
                PublishConnectionState(entry);
                entry.Sync.Release();
            }
        }

        private void RegisterFneConnection(Codeplug.System system, SystemStatusBox statusBox, bool autoStart)
        {
            if (system == null || string.IsNullOrWhiteSpace(system.Name))
                return;

            FneConnectionEntry entry = new FneConnectionEntry
            {
                SystemName = system.Name,
                SystemConfig = system,
                StatusBox = statusBox,
                IsConnected = false,
                IsBusy = false,
                DesiredStarted = autoStart
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

        private void ResetFneConnections()
        {
            List<FneConnectionEntry> entries;
            lock (fneConnectionEntries)
            {
                entries = fneConnectionEntries.Values.ToList();
                fneConnectionEntries.Clear();
            }

            foreach (FneConnectionEntry entry in entries)
            {
                try
                {
                    CancelDeferredStartupKeyRequests(entry.SystemName);
                    RemovePeerForEntry(entry);
                    entry.Sync.Dispose();
                }
                catch
                {
                    /* best effort cleanup */
                }
            }

            fneSystemManager.ClearAll();
            RefreshCommandControlsForConnectionState();
            NotifyConnectionWindowReset();
        }

        private FneConnectionEntry GetFneConnectionEntry(string systemName)
        {
            lock (fneConnectionEntries)
            {
                fneConnectionEntries.TryGetValue(systemName, out FneConnectionEntry entry);
                return entry;
            }
        }

        private void CreatePeerForEntry(FneConnectionEntry entry)
        {
            RemovePeerForEntry(entry);

            PeerSystem peer = fneSystemManager.AddOrReplaceFneSystem(entry.SystemName, entry.SystemConfig, this);
            entry.Peer = peer;

            entry.PeerConnectedHandler = (sender, response) =>
            {
                Log.WriteLine($"FNE Peer connected: {entry.SystemName}");
                Dispatcher.Invoke(() =>
                {
                    entry.IsConnected = true;
                    ResetHealthMonitorBaseline(entry);
                    UpdateSystemStatusBox(entry);
                    UpdateChannelConnectionVisuals(entry.SystemName);
                    RefreshCommandControlsForConnectionState();
                    PublishConnectionState(entry);
                    ScheduleDeferredStartupKeyRequests(entry.SystemName);
                });
            };

            entry.PeerDisconnectedHandler = _ =>
            {
                Log.WriteLine($"FNE Peer disconnected: {entry.SystemName}");
                Dispatcher.Invoke(() =>
                {
                    ApplyDisconnectedState(entry);
                    PublishConnectionState(entry);
                    CancelDeferredStartupKeyRequests(entry.SystemName);
                });
            };

            peer.peer.PeerConnected += entry.PeerConnectedHandler;
            peer.peer.PeerDisconnected += entry.PeerDisconnectedHandler;
        }

        private void RemovePeerForEntry(FneConnectionEntry entry)
        {
            if (entry?.Peer?.peer != null)
            {
                if (entry.PeerConnectedHandler != null)
                    entry.Peer.peer.PeerConnected -= entry.PeerConnectedHandler;

                if (entry.PeerDisconnectedHandler != null)
                    entry.Peer.peer.PeerDisconnected -= entry.PeerDisconnectedHandler;
            }

            if (!string.IsNullOrWhiteSpace(entry?.SystemName))
                fneSystemManager.RemoveFneSystem(entry.SystemName);

            entry.Peer = null;
            entry.PeerConnectedHandler = null;
            entry.PeerDisconnectedHandler = null;
        }

        private void ApplyDisconnectedState(FneConnectionEntry entry)
        {
            entry.IsConnected = false;
            UpdateSystemStatusBox(entry);
            ClearPatchPttTargetsForDisconnectedSystem(entry.SystemName);
            ResetChannelsForDisconnectedSystem(entry.SystemName);
            UpdateChannelConnectionVisuals(entry.SystemName);
            RefreshCommandControlsForConnectionState();
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
                    if (!string.Equals(NormalizeChannelSystemName(channel.SystemName), normalizedSystemName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Codeplug.System disconnectedSystem = Codeplug?.Systems?
                        .FirstOrDefault(system => ResourceIdentity.SystemMatches(system.Name, normalizedSystemName));
                    Codeplug.Channel disconnectedChannel = GetConfiguredChannels()
                        .FirstOrDefault(cpgChannel =>
                            ResourceIdentity.SystemMatches(cpgChannel.System, normalizedSystemName) &&
                            string.Equals(cpgChannel.Tgid?.Trim() ?? string.Empty, channel.DstId?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    bool hadReceiveState = channel.IsReceiving || channel.IsReceivingEncrypted;
                    bool hadTransmitState = channel.PttState || channel.PatchForwardingTxState || channel.TxStreamId != 0;

                    if (disconnectedSystem != null && disconnectedChannel != null && hadReceiveState && channel.RxStreamId > 0)
                        patchManager.HandleCallEnd(disconnectedSystem.Name, disconnectedChannel.Tgid, channel.RxStreamId);

                    if (disconnectedSystem != null && disconnectedChannel != null && hadTransmitState)
                        EndTarTxRecording(channel, disconnectedSystem, disconnectedChannel);

                    channel.IsReceiving = false;
                    channel.IsReceivingEncrypted = false;
                    channel.PttState = false;
                    channel.PatchForwardingTxState = false;
                    channel.PeerId = 0;
                    channel.RxStreamId = 0;
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
                    if (string.Equals(NormalizeChannelSystemName(channel.SystemName), NormalizeChannelSystemName(systemName), StringComparison.OrdinalIgnoreCase))
                        UpdateChannelConnectionVisual(channel);
                }
            }
        }

        private void UpdateChannelConnectionVisual(ChannelBox channel)
        {
            if (channel == null || channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                return;

            string systemName = NormalizeChannelSystemName(channel.SystemName);
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
            if (isShuttingDown)
                return;

            DateTime now = DateTime.UtcNow;
            if (!force && now < nextFneHealthCheckUtc)
                return;

            nextFneHealthCheckUtc = now.Add(FNE_HEALTH_CHECK_INTERVAL);

            List<(string SystemName, string Reason)> systemsToRestart = new List<(string, string)>();
            lock (fneConnectionEntries)
            {
                foreach (FneConnectionEntry entry in fneConnectionEntries.Values)
                {
                    if (!ShouldRestartUnhealthyFneConnection(entry, now, out string reason))
                        continue;

                    systemsToRestart.Add((entry.SystemName, reason));
                }
            }

            foreach ((string systemName, string reason) in systemsToRestart)
            {
                Log.WriteWarning($"FNE health check restarting '{systemName}': {reason}");
                Dispatcher.BeginInvoke(new Action(() => _ = RestartFneSystemAsync(systemName, showError: false)));
            }
        }

        private bool ShouldRestartUnhealthyFneConnection(FneConnectionEntry entry, DateTime now, out string reason)
        {
            reason = string.Empty;
            if (entry == null || entry.IsBusy || !entry.DesiredStarted)
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
                IsStarted = entry.Peer?.IsStarted == true
            };
        }
    }
}
