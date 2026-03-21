// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, K7CBL
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
            public SemaphoreSlim Sync { get; } = new SemaphoreSlim(1, 1);
            public EventHandler<PeerConnectedEvent> PeerConnectedHandler { get; set; }
            public Action<uint> PeerDisconnectedHandler { get; set; }
        }

        private readonly Dictionary<string, FneConnectionEntry> fneConnectionEntries = new(StringComparer.OrdinalIgnoreCase);
        private FneConnectionManagerWindow fneConnectionManagerWindow;

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

            await entry.Sync.WaitAsync();
            try
            {
                if (entry.IsBusy)
                    return;

                entry.IsBusy = true;
                PublishConnectionState(entry);

                if (entry.Peer?.IsStarted == true)
                    return;

                CreatePeerForEntry(entry);
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
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry == null)
                return;

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
                await Task.Run(() => entry.Peer.Start());
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Unable to restart FNE system '{systemName}'. {ex.Message}", "FNE Connection Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                });
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
                IsBusy = false
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
                    UpdateSystemStatusBox(entry);
                    RefreshCommandControlsForConnectionState();
                    PublishConnectionState(entry);
                });
            };

            entry.PeerDisconnectedHandler = _ =>
            {
                Log.WriteLine($"FNE Peer disconnected: {entry.SystemName}");
                Dispatcher.Invoke(() =>
                {
                    ApplyDisconnectedState(entry);
                    PublishConnectionState(entry);
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
            ResetChannelsForDisconnectedSystem(entry.SystemName);
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
            foreach (Canvas canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                {
                    if (!string.Equals(channel.SystemName, systemName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!(channel.IsReceiving || channel.IsReceivingEncrypted))
                        continue;

                    Codeplug.System disconnectedSystem = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel disconnectedChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (disconnectedSystem != null && disconnectedChannel != null && channel.RxStreamId > 0)
                        patchManager.HandleCallEnd(disconnectedSystem.Name, disconnectedChannel.Tgid, channel.RxStreamId);

                    channel.IsReceiving = false;
                    channel.IsReceivingEncrypted = false;
                    channel.PeerId = 0;
                    channel.RxStreamId = 0;
                    channel.VolumeMeterLevel = 0;
                    UpdateTabAudioIndicatorForChannel(channel);
                }
            }
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
