// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 J. Dean
*
*/

using Microsoft.Win32;
using System;
using System.Timers;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using DVMConsole.Controls;
using System.Windows.Media;
using NAudio.Wave;
using fnecore.P25;
using fnecore;
using Constants = fnecore.Constants;
using fnecore.P25.LC.TSBK;
using NWaves.Signals;
using static DVMConsole.P25Crypto;

namespace DVMConsole
{
    public partial class MainWindow : Window
    {
        public Codeplug Codeplug { get; set; }
        private bool isEditMode = false;

        private bool globalPttState = false;

        private const int GridSize = 5;

        private UIElement _draggedElement;
        private Point _startPoint;
        private double _offsetX;
        private double _offsetY;
        private bool _isDragging;

        private SettingsManager _settingsManager = new SettingsManager();
        private SelectedChannelsManager _selectedChannelsManager;
        private FlashingBackgroundManager _flashingManager;
        private WaveFilePlaybackManager _emergencyAlertPlayback;

        private ChannelBox playbackChannelBox;

        CallHistoryWindow callHistoryWindow = new CallHistoryWindow();

        public static string PLAYBACKTG = "LOCPLAYBACK";
        public static string PLAYBACKSYS = "LOCPLAYBACKSYS";
        public static string PLAYBACKCHNAME = "PLAYBACK";

        private readonly WaveInEvent _waveIn;
        private readonly AudioManager _audioManager;

        private static System.Timers.Timer _channelHoldTimer;

        private Dictionary<string, SlotStatus> systemStatuses = new Dictionary<string, SlotStatus>();
        private FneSystemManager _fneSystemManager = new FneSystemManager();

        public MainWindow()
        {
#if !DEBUG
            ConsoleNative.ShowConsole();
#endif
            InitializeComponent();
            _settingsManager.LoadSettings();
            _selectedChannelsManager = new SelectedChannelsManager();
            _flashingManager = new FlashingBackgroundManager(null, ChannelsCanvas, null, this);
            _emergencyAlertPlayback = new WaveFilePlaybackManager(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emergency.wav"));

            _channelHoldTimer = new System.Timers.Timer(10000);
            _channelHoldTimer.Elapsed += OnHoldTimerElapsed;
            _channelHoldTimer.AutoReset = true;
            _channelHoldTimer.Enabled = true;

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(8000, 16, 1)
            };
            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.RecordingStopped += WaveIn_RecordingStopped;

            _waveIn.StartRecording();

            _audioManager = new AudioManager(_settingsManager);

            _selectedChannelsManager.SelectedChannelsChanged += SelectedChannelsChanged;
            Loaded += MainWindow_Loaded;
        }

        private void OpenCodeplug_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Codeplug Files (*.yml)|*.yml|All Files (*.*)|*.*",
                Title = "Open Codeplug"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                LoadCodeplug(openFileDialog.FileName);

                _settingsManager.LastCodeplugPath = openFileDialog.FileName;
                _settingsManager.SaveSettings();
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists("UserSettings.json"))
                File.Delete("UserSettings.json");
        }

        private void LoadCodeplug(string filePath)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var yaml = File.ReadAllText(filePath);
                Codeplug = deserializer.Deserialize<Codeplug>(yaml);

                GenerateChannelWidgets();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading codeplug: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateChannelWidgets()
        {
            ChannelsCanvas.Children.Clear();
            double offsetX = 20;
            double offsetY = 20;

            if (Codeplug != null)
            {
                foreach (var system in Codeplug.Systems)
                {
                    var systemStatusBox = new SystemStatusBox(system.Name, system.Address, system.Port);

                    if (_settingsManager.SystemStatusPositions.TryGetValue(system.Name, out var position))
                    {
                        Canvas.SetLeft(systemStatusBox, position.X);
                        Canvas.SetTop(systemStatusBox, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(systemStatusBox, offsetX);
                        Canvas.SetTop(systemStatusBox, offsetY);
                    }

                    systemStatusBox.MouseLeftButtonDown += SystemStatusBox_MouseLeftButtonDown;
                    systemStatusBox.MouseMove += SystemStatusBox_MouseMove;
                    systemStatusBox.MouseRightButtonDown += SystemStatusBox_MouseRightButtonDown;

                    ChannelsCanvas.Children.Add(systemStatusBox);

                    offsetX += 225;
                    if (offsetX + 220 > ChannelsCanvas.ActualWidth)
                    {
                        offsetX = 20;
                        offsetY += 106;
                    }

                    if (File.Exists(system.AliasPath))
                        system.RidAlias = AliasTools.LoadAliases(system.AliasPath);

                    _fneSystemManager.AddFneSystem(system.Name, system, this);

                    PeerSystem peer = _fneSystemManager.GetFneSystem(system.Name);

                    peer.peer.PeerConnected += (sender, response) =>
                    {
                        Console.WriteLine("FNE Peer connected");

                        Dispatcher.Invoke(() =>
                        {
                            systemStatusBox.Background = (Brush)new BrushConverter().ConvertFrom("#FF00BC48");
                            systemStatusBox.ConnectionState = "Connected";
                        });
                    };


                    peer.peer.PeerDisconnected += (response) =>
                    {
                        Console.WriteLine("FNE Peer disconnected");

                        Dispatcher.Invoke(() =>
                        {
                            systemStatusBox.Background = new SolidColorBrush(Colors.Red);
                            systemStatusBox.ConnectionState = "Disconnected";
                        });
                    };

                    Task.Run(() =>
                    {
                        peer.Start();
                    });

                    if (!_settingsManager.ShowSystemStatus)
                        systemStatusBox.Visibility = Visibility.Collapsed;

                }
            }

            if (_settingsManager.ShowChannels && Codeplug != null)
            {
                foreach (var zone in Codeplug.Zones)
                {
                    foreach (var channel in zone.Channels)
                    {
                        var channelBox = new ChannelBox(_selectedChannelsManager, _audioManager, channel.Name, channel.System, channel.Tgid);

                        //channelBox.crypter.AddKey(channel.GetKeyId(), channel.GetAlgoId(), channel.GetEncryptionKey());

                        systemStatuses.Add(channel.Name, new SlotStatus());

                        if (_settingsManager.ChannelPositions.TryGetValue(channel.Name, out var position))
                        {
                            Canvas.SetLeft(channelBox, position.X);
                            Canvas.SetTop(channelBox, position.Y);
                        }
                        else
                        {
                            Canvas.SetLeft(channelBox, offsetX);
                            Canvas.SetTop(channelBox, offsetY);
                        }

                        channelBox.PTTButtonClicked += ChannelBox_PTTButtonClicked;
                        channelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
                        channelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

                        channelBox.MouseLeftButtonDown += ChannelBox_MouseLeftButtonDown;
                        channelBox.MouseMove += ChannelBox_MouseMove;
                        channelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
                        ChannelsCanvas.Children.Add(channelBox);

                        offsetX += 225;

                        if (offsetX + 220 > ChannelsCanvas.ActualWidth)
                        {
                            offsetX = 20;
                            offsetY += 106;
                        }
                    }
                }
            }

            if (_settingsManager.ShowAlertTones && Codeplug != null)
            {
                foreach (var alertPath in _settingsManager.AlertToneFilePaths)
                {
                    var alertTone = new AlertTone(alertPath)
                    {
                        IsEditMode = isEditMode
                    };

                    alertTone.OnAlertTone += SendAlertTone;

                    if (_settingsManager.AlertTonePositions.TryGetValue(alertPath, out var position))
                    {
                        Canvas.SetLeft(alertTone, position.X);
                        Canvas.SetTop(alertTone, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(alertTone, 20);
                        Canvas.SetTop(alertTone, 20);
                    }

                    alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;

                    ChannelsCanvas.Children.Add(alertTone);
                }
            }

            playbackChannelBox = new ChannelBox(_selectedChannelsManager, _audioManager, PLAYBACKCHNAME, PLAYBACKSYS, PLAYBACKTG);

            if (_settingsManager.ChannelPositions.TryGetValue(PLAYBACKCHNAME, out var pos))
            {
                Canvas.SetLeft(playbackChannelBox, pos.X);
                Canvas.SetTop(playbackChannelBox, pos.Y);
            }
            else
            {
                Canvas.SetLeft(playbackChannelBox, offsetX);
                Canvas.SetTop(playbackChannelBox, offsetY);
            }

            playbackChannelBox.PTTButtonClicked += ChannelBox_PTTButtonClicked;
            playbackChannelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
            playbackChannelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

            playbackChannelBox.MouseLeftButtonDown += ChannelBox_MouseLeftButtonDown;
            playbackChannelBox.MouseMove += ChannelBox_MouseMove;
            playbackChannelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
            ChannelsCanvas.Children.Add(playbackChannelBox);

            //offsetX += 225;

            //if (offsetX + 220 > ChannelsCanvas.ActualWidth)
            //{
            //    offsetX = 20;
            //    offsetY += 106;
            //}

            AdjustCanvasHeight();
        }

        private void WaveIn_RecordingStopped(object sender, EventArgs e)
        {
            /* stub */
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            bool isAnyTgOn = false;

            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                {
                    playbackChannelBox.IsReceiving = true;
                    continue;
                }

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                Task.Run(() =>
                {
                    PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                    if (channel.IsSelected && channel.PttState)
                    {
                        isAnyTgOn = true;

                        int samples = 320;

                        channel.chunkedPcm = AudioConverter.SplitToChunks(e.Buffer);

                        foreach (byte[] chunk in channel.chunkedPcm)
                        {
                            if (chunk.Length == samples)
                            {
                                P25EncodeAudioFrame(chunk, handler, channel, cpgChannel, system);
                            }
                            else
                            {
                                Console.WriteLine("bad sample length: " + chunk.Length);
                            }
                        }
                    }
                });
            }

            if (isAnyTgOn && playbackChannelBox.IsSelected)
                _audioManager.AddTalkgroupStream(PLAYBACKTG, e.Buffer);
        }

        private void SelectedChannelsChanged()
        {
            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                PeerSystem fne = _fneSystemManager.GetFneSystem(system.Name);

                if (channel.IsSelected)
                {
                    uint newTgid = UInt32.Parse(cpgChannel.Tgid);

                    if (cpgChannel.GetAlgoId() != 0 && cpgChannel.GetKeyId() != 0)
                        fne.peer.SendMasterKeyRequest(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId());
                }
            }
        }

        private void AudioSettings_Click(object sender, RoutedEventArgs e)
        {
            List<Codeplug.Channel> channels = Codeplug?.Zones.SelectMany(z => z.Channels).ToList() ?? new List<Codeplug.Channel>();

            AudioSettingsWindow audioSettingsWindow = new AudioSettingsWindow(_settingsManager, _audioManager, channels);
            audioSettingsWindow.ShowDialog();
        }

        private void P25Page_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            if (pageWindow.ShowDialog() == true)
            {
                PeerSystem handler = _fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_CALL_ALRT callAlert = new IOSP_CALL_ALRT(UInt32.Parse(pageWindow.DstId), UInt32.Parse(pageWindow.RadioSystem.Rid));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = UInt32.Parse(pageWindow.RadioSystem.Rid),
                    DstId = UInt32.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_CALL_ALRT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];
                byte[] payload = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                callAlert.Encode(ref tsbk, ref payload, true, true);

                handler.SendP25TSBK(callData, tsbk);

                Console.WriteLine("sent page");
            }
        }

        private async void ManualPage_Click(object sender, RoutedEventArgs e)
        {
            QuickCallPage pageWindow = new QuickCallPage();
            pageWindow.Owner = this;
            if (pageWindow.ShowDialog() == true)
            {
                foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
                {
                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                    if (channel.PageState)
                    {
                        ToneGenerator generator = new ToneGenerator();

                        double toneADuration = 1.0;
                        double toneBDuration = 3.0;

                        byte[] toneA = generator.GenerateTone(Double.Parse(pageWindow.ToneA), toneADuration);
                        byte[] toneB = generator.GenerateTone(Double.Parse(pageWindow.ToneB), toneBDuration);

                        byte[] combinedAudio = new byte[toneA.Length + toneB.Length];
                        Buffer.BlockCopy(toneA, 0, combinedAudio, 0, toneA.Length);
                        Buffer.BlockCopy(toneB, 0, combinedAudio, toneA.Length, toneB.Length);

                        int chunkSize = 320;

                        int totalChunks = (combinedAudio.Length + chunkSize - 1) / chunkSize;

                        Task.Run(() =>
                        {
                            //_waveProvider.ClearBuffer();
                            _audioManager.AddTalkgroupStream(cpgChannel.Tgid, combinedAudio);
                        });

                        await Task.Run(() =>
                        {
                            for (int i = 0; i < totalChunks; i++)
                            {
                                int offset = i * chunkSize;
                                int size = Math.Min(chunkSize, combinedAudio.Length - offset);

                                byte[] chunk = new byte[chunkSize];
                                Buffer.BlockCopy(combinedAudio, offset, chunk, 0, size);

                                if (chunk.Length == 320)
                                {
                                    P25EncodeAudioFrame(chunk, handler, channel, cpgChannel, system);
                                }
                            }
                        });

                        double totalDurationMs = (toneADuration + toneBDuration) * 1000 + 750;
                        await Task.Delay((int)totalDurationMs  + 4000);

                        handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), false);

                        Dispatcher.Invoke(() =>
                        {
                            //channel.PageState = false; // TODO: Investigate
                            channel.PageSelectButton.Background = channel.grayGradient;
                        });
                    }
                }
            }
        }

        private void SendAlertTone(AlertTone e)
        {
            Task.Run(() => SendAlertTone(e.AlertFilePath));
        }

        private void SendAlertTone(string filePath, bool forHold = false)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
                    {
                        if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                            continue;

                        Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                        PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                        if (channel.PageState || (forHold && channel.HoldState))
                        {
                            byte[] pcmData;

                            Task.Run(async () => {
                                using (var waveReader = new WaveFileReader(filePath))
                                {
                                    if (waveReader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
                                        waveReader.WaveFormat.SampleRate != 8000 ||
                                        waveReader.WaveFormat.BitsPerSample != 16 ||
                                        waveReader.WaveFormat.Channels != 1)
                                    {
                                        MessageBox.Show("The alert tone must be PCM 16-bit, Mono, 8000Hz format.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                        return;
                                    }

                                    using (MemoryStream ms = new MemoryStream())
                                    {
                                        waveReader.CopyTo(ms);
                                        pcmData = ms.ToArray();
                                    }
                                }

                                int chunkSize = 1600;
                                int totalChunks = (pcmData.Length + chunkSize - 1) / chunkSize;

                                if (pcmData.Length % chunkSize != 0)
                                {
                                    byte[] paddedData = new byte[totalChunks * chunkSize];
                                    Buffer.BlockCopy(pcmData, 0, paddedData, 0, pcmData.Length);
                                    pcmData = paddedData;
                                }

                                Task.Run(() =>
                                {
                                    _audioManager.AddTalkgroupStream(cpgChannel.Tgid, pcmData);
                                });

                                DateTime startTime = DateTime.UtcNow;

                                for (int i = 0; i < totalChunks; i++)
                                {
                                    int offset = i * chunkSize;
                                    byte[] chunk = new byte[chunkSize];
                                    Buffer.BlockCopy(pcmData, offset, chunk, 0, chunkSize);

                                    PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                                    channel.chunkedPcm = AudioConverter.SplitToChunks(chunk);

                                    foreach (byte[] smallchunk in channel.chunkedPcm)
                                    {
                                        if (smallchunk.Length == 320)
                                        {
                                            P25EncodeAudioFrame(smallchunk, handler, channel, cpgChannel, system);
                                        }
                                    }

                                    DateTime nextPacketTime = startTime.AddMilliseconds((i + 1) * 100);
                                    TimeSpan waitTime = nextPacketTime - DateTime.UtcNow;

                                    if (waitTime.TotalMilliseconds > 0)
                                    {
                                        await Task.Delay(waitTime);
                                    }
                                }

                                double totalDurationMs = ((double)pcmData.Length / 16000) + 250;
                                await Task.Delay((int)totalDurationMs + 3000);

                                handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), false);

                                Dispatcher.Invoke(() =>
                                {
                                    if (forHold)
                                        channel.PttButton.Background = channel.grayGradient;
                                    else
                                        channel.PageState = false;
                                });
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to process alert tone: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Alert file not set or file not found.", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SelectWidgets_Click(object sender, RoutedEventArgs e)
        {
            WidgetSelectionWindow widgetSelectionWindow = new WidgetSelectionWindow();
            widgetSelectionWindow.Owner = this;
            if (widgetSelectionWindow.ShowDialog() == true)
            {
                _settingsManager.ShowSystemStatus = widgetSelectionWindow.ShowSystemStatus;
                _settingsManager.ShowChannels = widgetSelectionWindow.ShowChannels;
                _settingsManager.ShowAlertTones = widgetSelectionWindow.ShowAlertTones;

                GenerateChannelWidgets();
                _settingsManager.SaveSettings();
            }
        }

        private void HandleEmergency(string dstId, string srcId)
        {
            bool forUs = false;

            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                if (dstId == cpgChannel.Tgid)
                {
                    forUs = true;
                    channel.Emergency = true;
                    channel.LastSrcId = srcId;
                }
            }

            if (forUs)
            {
                Dispatcher.Invoke(() =>
                {
                    _flashingManager.Start();
                    _emergencyAlertPlayback.Start();
                });
            }
        }

        private void ChannelBox_HoldChannelButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;
        }

        private void ChannelBox_PageButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

            if (e.PageState)
            {
                handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), true);
            }
            else
            {
                handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), false);
            }
        }

        private void ChannelBox_PTTButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

            if (!e.IsSelected)
                return;

            FneUtils.Memset(e.mi, 0x00, P25Defines.P25_MI_LENGTH);

            uint srcId = UInt32.Parse(system.Rid);
            uint dstId = UInt32.Parse(cpgChannel.Tgid);

            if (e.PttState)
            {
                e.txStreamId = handler.NewStreamId();

                handler.SendP25TDU(srcId, dstId, true);
            }
            else
            {
                handler.SendP25TDU(srcId, dstId, false);
            }
        }

        private void ChannelBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode || !(sender is UIElement element)) return;

            _draggedElement = element;
            _startPoint = e.GetPosition(ChannelsCanvas);
            _offsetX = _startPoint.X - Canvas.GetLeft(_draggedElement);
            _offsetY = _startPoint.Y - Canvas.GetTop(_draggedElement);
            _isDragging = true;

            element.CaptureMouse();
        }

        private void ChannelBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isEditMode || !_isDragging || _draggedElement == null) return;

            Point currentPosition = e.GetPosition(ChannelsCanvas);

            // Calculate the new position with snapping to the grid
            double newLeft = Math.Round((currentPosition.X - _offsetX) / GridSize) * GridSize;
            double newTop = Math.Round((currentPosition.Y - _offsetY) / GridSize) * GridSize;

            // Ensure the box stays within canvas bounds
            newLeft = Math.Max(0, Math.Min(newLeft, ChannelsCanvas.ActualWidth - _draggedElement.RenderSize.Width));
            newTop = Math.Max(0, Math.Min(newTop, ChannelsCanvas.ActualHeight - _draggedElement.RenderSize.Height));

            // Apply snapped position
            Canvas.SetLeft(_draggedElement, newLeft);
            Canvas.SetTop(_draggedElement, newTop);

            // Save the new position if it's a ChannelBox
            if (_draggedElement is ChannelBox channelBox)
            {
                _settingsManager.UpdateChannelPosition(channelBox.ChannelName, newLeft, newTop);
            }

            AdjustCanvasHeight();
        }

        private void ChannelBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode || !_isDragging || _draggedElement == null) return;

            _isDragging = false;
            _draggedElement.ReleaseMouseCapture();
            _draggedElement = null;
        }

        private void SystemStatusBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseLeftButtonDown(sender, e);
        private void SystemStatusBox_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);

        private void SystemStatusBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            if (sender is SystemStatusBox systemStatusBox)
            {
                double x = Canvas.GetLeft(systemStatusBox);
                double y = Canvas.GetTop(systemStatusBox);
                _settingsManager.SystemStatusPositions[systemStatusBox.SystemName] = new ChannelPosition { X = x, Y = y };

                ChannelBox_MouseRightButtonDown(sender, e);

                AdjustCanvasHeight();
            }
        }

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            var menuItem = (MenuItem)sender;
            menuItem.Header = isEditMode ? "Disable Edit Mode" : "Enable Edit Mode";
            UpdateEditModeForWidgets();
        }

        private void UpdateEditModeForWidgets()
        {
            foreach (var child in ChannelsCanvas.Children)
            {
                if (child is AlertTone alertTone)
                {
                    alertTone.IsEditMode = isEditMode;
                }

                if (child is ChannelBox channelBox)
                {
                    channelBox.IsEditMode = isEditMode;
                }
            }
        }

        private void AddAlertTone_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select Alert Tone"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string alertFilePath = openFileDialog.FileName;
                var alertTone = new AlertTone(alertFilePath)
                {
                    IsEditMode = isEditMode
                };

                alertTone.OnAlertTone += SendAlertTone;

                if (_settingsManager.AlertTonePositions.TryGetValue(alertFilePath, out var position))
                {
                    Canvas.SetLeft(alertTone, position.X);
                    Canvas.SetTop(alertTone, position.Y);
                }
                else
                {
                    Canvas.SetLeft(alertTone, 20);
                    Canvas.SetTop(alertTone, 20);
                }

                alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;

                ChannelsCanvas.Children.Add(alertTone);
                _settingsManager.UpdateAlertTonePaths(alertFilePath);

                AdjustCanvasHeight();
            }
        }

        private void AlertTone_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            if (sender is AlertTone alertTone)
            {
                double x = Canvas.GetLeft(alertTone);
                double y = Canvas.GetTop(alertTone);
                _settingsManager.UpdateAlertTonePosition(alertTone.AlertFilePath, x, y);

                AdjustCanvasHeight();
            }
        }

        private void AdjustCanvasHeight()
        {
            double maxBottom = 0;

            foreach (UIElement child in ChannelsCanvas.Children)
            {
                double childBottom = Canvas.GetTop(child) + child.RenderSize.Height;
                if (childBottom > maxBottom)
                {
                    maxBottom = childBottom;
                }
            }

            ChannelsCanvas.Height = maxBottom + 150;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settingsManager.LastCodeplugPath) && File.Exists(_settingsManager.LastCodeplugPath))
            {
                LoadCodeplug(_settingsManager.LastCodeplugPath);
            }
            else
            {
                GenerateChannelWidgets();
            }
        }

        private async void OnHoldTimerElapsed(object sender, ElapsedEventArgs e)
        {
            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                if (channel.HoldState && !channel.IsReceiving && !channel.PttState && !channel.PageState)
                {
                    handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), true);
                    await Task.Delay(1000);

                    SendAlertTone("hold.wav", true);
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _settingsManager.SaveSettings();
            base.OnClosing(e);
            Application.Current.Shutdown();
        }

        private void ClearEmergency_Click(object sender, RoutedEventArgs e)
        {
            _emergencyAlertPlayback.Stop();
            _flashingManager.Stop();

            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                channel.Emergency = false;
            }
        }

        private void btnAlert1_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                SendAlertTone("alert1.wav");
            });
        }

        private void btnAlert2_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone("alert2.wav");
            });
        }

        private void btnAlert3_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone("alert3.wav");
            });
        }

        private async void btnGlobalPtt_Click(object sender, RoutedEventArgs e)
        {
            if (globalPttState)
                await Task.Delay(500);

            globalPttState = !globalPttState;

            foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                channel.txStreamId = handler.NewStreamId();

                if (globalPttState)
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = channel.redGradient;
                        channel.PttState = true;
                    });

                    handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), true);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = channel.grayGradient;
                        channel.PttState = false;
                    });

                    handler.SendP25TDU(UInt32.Parse(system.Rid), UInt32.Parse(cpgChannel.Tgid), false);
                }
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ChannelBox channel in ChannelsCanvas.Children.OfType<ChannelBox>())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                if (!channel.IsSelected)
                {
                    channel.IsSelected = true;

                    channel.Background = channel.IsSelected ? (Brush)new BrushConverter().ConvertFrom("#FF0B004B") : Brushes.Gray;

                    if (channel.IsSelected)
                    {
                        _selectedChannelsManager.AddSelectedChannel(channel);
                    }
                    else
                    {
                        _selectedChannelsManager.RemoveSelectedChannel(channel);
                    }
                }
            }
        }

        /// <summary>
        /// Helper to encode and transmit PCM audio as P25 IMBE frames.
        /// </summary>
        private void P25EncodeAudioFrame(byte[] pcm, PeerSystem handler, ChannelBox channel, Codeplug.Channel cpgChannel, Codeplug.System system)
        {
            bool encryptCall = true; // TODO: make this dynamic somewhere?

            if (channel.p25N > 17)
                channel.p25N = 0;
            if (channel.p25N == 0)
                FneUtils.Memset(channel.netLDU1, 0, 9 * 25);
            if (channel.p25N == 9)
                FneUtils.Memset(channel.netLDU2, 0, 9 * 25);

            // Log.Logger.Debug($"BYTE BUFFER {FneUtils.HexDump(pcm)}");

            //// pre-process: apply gain to PCM audio frames
            //if (Program.Configuration.TxAudioGain != 1.0f)
            //{
            //    BufferedWaveProvider buffer = new BufferedWaveProvider(waveFormat);
            //    buffer.AddSamples(pcm, 0, pcm.Length);

            //    VolumeWaveProvider16 gainControl = new VolumeWaveProvider16(buffer);
            //    gainControl.Volume = Program.Configuration.TxAudioGain;
            //    gainControl.Read(pcm, 0, pcm.Length);
            //}

            int smpIdx = 0;
            short[] samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];
            for (int pcmIdx = 0; pcmIdx < pcm.Length; pcmIdx += 2)
            {
                samples[smpIdx] = (short)((pcm[pcmIdx + 1] << 8) + pcm[pcmIdx + 0]);
                smpIdx++;
            }

            // Convert to floats
            float[] fSamples = AudioConverter.PcmToFloat(samples);

            // Convert to signal
            DiscreteSignal signal = new DiscreteSignal(8000, fSamples, true);

            // Log.Logger.Debug($"SAMPLE BUFFER {FneUtils.HexDump(samples)}");

            // encode PCM samples into IMBE codewords
            byte[] imbe = new byte[FneSystemBase.IMBE_BUF_LEN];


            int tone = 0;

            if (true) // TODO: Disable/enable detection
            {
                tone = channel.toneDetector.Detect(signal);
            }
            if (tone > 0)
            {
                MBEToneGenerator.IMBEEncodeSingleTone((ushort)tone, imbe);
                Console.WriteLine($"({system.Name}) P25D: {tone} HZ TONE DETECT");
            }
            else
            {
#if WIN32
                if (channel.extFullRateVocoder == null)
                    channel.extFullRateVocoder = new AmbeVocoder(true);

                channel.extFullRateVocoder.encode(samples, out imbe);
#else
                if (channel.encoder == null)
                    channel.encoder = new MBEEncoder(MBE_MODE.IMBE_88BIT);

                channel.encoder.encode(samples, imbe);
#endif
            }
            // Log.Logger.Debug($"IMBE {FneUtils.HexDump(imbe)}");

            if (encryptCall && cpgChannel.GetAlgoId() != 0 && cpgChannel.GetKeyId() != 0)
            {
                // initial HDU MI
                if (channel.p25N == 0)
                {
                    if (channel.mi.All(b => b == 0))
                    {
                        Random random = new Random();

                        for (int i = 0; i < P25Defines.P25_MI_LENGTH; i++)
                        {
                            channel.mi[i] = (byte)random.Next(0x00, 0x100);
                        }
                    }

                    channel.crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), ProtocolType.P25Phase1, channel.mi);
                }

                // crypto time
                channel.crypter.Process(imbe, channel.p25N < 9U ? P25Crypto.FrameType.LDU1 : P25Crypto.FrameType.LDU2, 0);

                // last block of LDU2, prepare a new MI
                if (channel.p25N == 17U)
                {
                    P25Crypto.CycleP25Lfsr(channel.mi);
                    channel.crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), ProtocolType.P25Phase1, channel.mi);
                }
            }

            // fill the LDU buffers appropriately
            switch (channel.p25N)
            {
                // LDU1
                case 0:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 10, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 1:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 26, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 2:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 55, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 3:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 80, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 4:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 105, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 5:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 130, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 6:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 155, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 7:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 180, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 8:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 204, FneSystemBase.IMBE_BUF_LEN);
                    break;

                // LDU2
                case 9:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 10, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 10:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 26, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 11:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 55, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 12:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 80, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 13:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 105, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 14:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 130, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 15:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 155, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 16:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 180, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 17:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 204, FneSystemBase.IMBE_BUF_LEN);
                    break;
            }

            uint srcId = UInt32.Parse(system.Rid);
            uint dstId = UInt32.Parse(cpgChannel.Tgid);

            FnePeer peer = handler.peer;
            RemoteCallData callData = new RemoteCallData()
            {
                SrcId = srcId,
                DstId = dstId,
                LCO = P25Defines.LC_GROUP
            };

            // send P25 LDU1
            if (channel.p25N == 8U)
            {
                ushort pktSeq = 0;
                if (channel.p25SeqNo == 0U)
                    pktSeq = peer.pktSeq(true);
                else
                    pktSeq = peer.pktSeq();

                //Console.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME    * PEER {handler.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.txStreamId}]");

                byte[] payload = new byte[200];
                handler.CreateNewP25MessageHdr((byte)P25DUID.LDU1, callData, ref payload, cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                handler.CreateP25LDU1Message(channel.netLDU1, ref payload, srcId, dstId);

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, pktSeq, channel.txStreamId);
            }

            // send P25 LDU2
            if (channel.p25N == 17U)
            {
                ushort pktSeq = 0;
                if (channel.p25SeqNo == 0U)
                    pktSeq = peer.pktSeq(true);
                else
                    pktSeq = peer.pktSeq();

                //Console.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME    * PEER {handler.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.txStreamId}]");

                byte[] payload = new byte[200];
                handler.CreateNewP25MessageHdr((byte)P25DUID.LDU2, callData, ref payload, cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                handler.CreateP25LDU2Message(channel.netLDU2, ref payload, new CryptoParams { AlgId = cpgChannel.GetAlgoId(), KeyId = cpgChannel.GetKeyId(), Mi = channel.mi });

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, pktSeq, channel.txStreamId);
            }

            channel.p25SeqNo++;
            channel.p25N++;
        }

        /// <summary>
        /// Helper to decode and playback P25 IMBE frames as PCM audio.
        /// </summary>
        /// <param name="ldu"></param>
        /// <param name="e"></param>
        private void P25DecodeAudioFrame(byte[] ldu, P25DataReceivedEvent e, PeerSystem system, ChannelBox channel, bool emergency = false, P25Crypto.FrameType frameType = P25Crypto.FrameType.LDU1)
        {
            try
            {
                // decode 9 IMBE codewords into PCM samples
                for (int n = 0; n < 9; n++)
                {
                    byte[] imbe = new byte[FneSystemBase.IMBE_BUF_LEN];
                    switch (n)
                    {
                        case 0:
                            Buffer.BlockCopy(ldu, 10, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 1:
                            Buffer.BlockCopy(ldu, 26, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 2:
                            Buffer.BlockCopy(ldu, 55, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 3:
                            Buffer.BlockCopy(ldu, 80, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 4:
                            Buffer.BlockCopy(ldu, 105, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 5:
                            Buffer.BlockCopy(ldu, 130, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 6:
                            Buffer.BlockCopy(ldu, 155, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 7:
                            Buffer.BlockCopy(ldu, 180, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 8:
                            Buffer.BlockCopy(ldu, 204, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                    }

                    //Log.Logger.Debug($"Decoding IMBE buffer: {FneUtils.HexDump(imbe)}");

                    short[] samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];

                    channel.crypter.Process(imbe, frameType, n);

#if WIN32
                    if (channel.extFullRateVocoder == null)
                        channel.extFullRateVocoder = new AmbeVocoder(true);

                    channel.p25Errs = channel.extFullRateVocoder.decode(imbe, out samples);
#else

                    channel.p25Errs = channel.decoder.decode(imbe, samples);
#endif

                    if (emergency && !channel.Emergency)
                    {
                        Task.Run(() =>
                        {
                            HandleEmergency(e.SrcId.ToString(), e.DstId.ToString());
                        });
                    }

                    if (samples != null)
                    {
                        //Log.Logger.Debug($"({Config.Name}) P25D: Traffic *VOICE FRAME    * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} VC{n} ERRS {errs} [STREAM ID {e.StreamId}]");
                        //Log.Logger.Debug($"IMBE {FneUtils.HexDump(imbe)}");
                        //Console.WriteLine($"SAMPLE BUFFER {FneUtils.HexDump(samples)}");

                        int pcmIdx = 0;
                        byte[] pcmData = new byte[samples.Length * 2];
                        for (int i = 0; i < samples.Length; i++)
                        {
                            pcmData[pcmIdx] = (byte)(samples[i] & 0xFF);
                            pcmData[pcmIdx + 1] = (byte)((samples[i] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        _audioManager.AddTalkgroupStream(e.DstId.ToString(), pcmData);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Audio Decode Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        public void KeyResponseReceived(KeyResponseEvent e)
        {
            //Console.WriteLine($"Message ID: {e.KmmKey.MessageId}");
            //Console.WriteLine($"Decrypt Info Format: {e.KmmKey.DecryptInfoFmt}");
            //Console.WriteLine($"Algorithm ID: {e.KmmKey.AlgId}");
            //Console.WriteLine($"Key ID: {e.KmmKey.KeyId}");
            //Console.WriteLine($"Keyset ID: {e.KmmKey.KeysetItem.KeysetId}");
            //Console.WriteLine($"Keyset Alg ID: {e.KmmKey.KeysetItem.AlgId}");
            //Console.WriteLine($"Keyset Key Length: {e.KmmKey.KeysetItem.KeyLength}");
            //Console.WriteLine($"Number of Keys: {e.KmmKey.KeysetItem.Keys.Count}");

            foreach (var key in e.KmmKey.KeysetItem.Keys)
            {
                //Console.WriteLine($"  Key Format: {key.KeyFormat}");
                //Console.WriteLine($"  SLN: {key.Sln}");
                //Console.WriteLine($"  Key ID: {key.KeyId}");
                //Console.WriteLine($"  Key Data: {BitConverter.ToString(key.GetKey())}");

                Dispatcher.Invoke(() =>
                {
                    foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
                    {
                        Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                        PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                        if (cpgChannel.GetKeyId() != 0 && cpgChannel.GetAlgoId() != 0)
                            channel.crypter.AddKey(key.KeyId, e.KmmKey.KeysetItem.AlgId, key.GetKey());
                    }
                });
            }
        }

        private void KeyStatus_Click(object sender, RoutedEventArgs e)
        {
            KeyStatusWindow keyStatus = new KeyStatusWindow(Codeplug, this);
            keyStatus.Show();
        }

        /// <summary>
        /// Event handler used to process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void P25DataReceived(P25DataReceivedEvent e, DateTime pktTime)
        {
            uint sysId = (uint)((e.Data[11U] << 8) | (e.Data[12U] << 0));
            uint netId = FneUtils.Bytes3ToUInt32(e.Data, 16);
            byte control = e.Data[14U];

            byte len = e.Data[23];
            byte[] data = new byte[len];
            for (int i = 24; i < len; i++)
                data[i - 24] = e.Data[i];

            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in _selectedChannelsManager.GetSelectedChannels())
                {
                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    bool isEmergency = false;
                    bool encrypted = false;

                    PeerSystem handler = _fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    if (!systemStatuses.ContainsKey(cpgChannel.Name))
                    {
                        systemStatuses[cpgChannel.Name] = new SlotStatus();
                    }

                    if (channel.decoder == null)
                    {
                        channel.decoder = new MBEDecoder(MBE_MODE.IMBE_88BIT);
                    }

                    SlotStatus slot = systemStatuses[cpgChannel.Name];

                    // if this is an LDU1 see if this is the first LDU with HDU encryption data
                    if (e.DUID == P25DUID.LDU1)
                    {
                        byte frameType = e.Data[180];

                        // get the initial MI and other enc info (bug found by the screeeeeeeeech on initial tx...)
                        if (frameType == P25Defines.P25_FT_HDU_VALID)
                        {
                            channel.algId = e.Data[181];
                            channel.kId = (ushort)((e.Data[182] << 8) | e.Data[183]);
                            Array.Copy(e.Data, 184, channel.mi, 0, P25Defines.P25_MI_LENGTH);

                            channel.crypter.Prepare(channel.algId, channel.kId, P25Crypto.ProtocolType.P25Phase1, channel.mi);

                            encrypted = true;
                        }
                    }

                    // is this a new call stream?
                    if (e.StreamId != slot.RxStreamId && ((e.DUID != P25DUID.TDU) && (e.DUID != P25DUID.TDULC)))
                    {
                        channel.IsReceiving = true;
                        slot.RxStart = pktTime;
                        Console.WriteLine($"({system.Name}) P25D: Traffic *CALL START     * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");

                        FneUtils.Memset(channel.mi, 0x00, P25Defines.P25_MI_LENGTH);

                        callHistoryWindow.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId);
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, encrypted);

                        string alias = string.Empty;

                        try
                        {
                            alias = AliasTools.GetAliasByRid(system.RidAlias, (int)e.SrcId);
                        }
                        catch (Exception) { }

                        if (string.IsNullOrEmpty(alias))
                            channel.LastSrcId = "Last SRC: " + e.SrcId;
                        else
                            channel.LastSrcId = "Last: " + alias;

                        if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                            channel.Background = (Brush)new BrushConverter().ConvertFrom("#ffdeaf0a");
                        else
                            channel.Background = (Brush)new BrushConverter().ConvertFrom("#FF00BC48");
                    }

                    // Is the call over?
                    if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (slot.RxType != fnecore.FrameType.TERMINATOR))
                    {
                        channel.IsReceiving = false;
                        TimeSpan callDuration = pktTime - slot.RxStart;
                        Console.WriteLine($"({system.Name}) P25D: Traffic *CALL END       * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        channel.Background = (Brush)new BrushConverter().ConvertFrom("#FF0B004B");
                        callHistoryWindow.ChannelUnkeyed(cpgChannel.Name, (int)e.SrcId);
                        return;
                    }

                    if ((channel.algId != cpgChannel.GetAlgoId() || channel.kId != cpgChannel.GetKeyId()) && channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                        continue;

                    byte[] newMI = new byte[P25Defines.P25_MI_LENGTH];

                    int count = 0;

                    switch (e.DUID)
                    {
                        case P25DUID.LDU1:
                            {
                                // The '62', '63', '64', '65', '66', '67', '68', '69', '6A' records are LDU1
                                if ((data[0U] == 0x62U) && (data[22U] == 0x63U) &&
                                    (data[36U] == 0x64U) && (data[53U] == 0x65U) &&
                                    (data[70U] == 0x66U) && (data[87U] == 0x67U) &&
                                    (data[104U] == 0x68U) && (data[121U] == 0x69U) &&
                                    (data[138U] == 0x6AU))
                                {
                                    // The '62' record - IMBE Voice 1
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 0, 22);
                                    count += 22;

                                    // The '63' record - IMBE Voice 2
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 25, 14);
                                    count += 14;

                                    // The '64' record - IMBE Voice 3 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 50, 17);
                                    byte serviceOptions = data[count + 3];
                                    isEmergency = (serviceOptions & 0x80) == 0x80;
                                    count += 17;

                                    // The '65' record - IMBE Voice 4 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 75, 17);
                                    count += 17;

                                    // The '66' record - IMBE Voice 5 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 100, 17);
                                    count += 17;

                                    // The '67' record - IMBE Voice 6 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 125, 17);
                                    count += 17;

                                    // The '68' record - IMBE Voice 7 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 150, 17);
                                    count += 17;

                                    // The '69' record - IMBE Voice 8 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 175, 17);
                                    count += 17;

                                    // The '6A' record - IMBE Voice 9 + Low Speed Data
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 200, 16);
                                    count += 16;

                                    // decode 9 IMBE codewords into PCM samples
                                    P25DecodeAudioFrame(channel.netLDU1, e, handler, channel, isEmergency);
                                }
                            }
                            break;
                        case P25DUID.LDU2:
                            {
                                // The '6B', '6C', '6D', '6E', '6F', '70', '71', '72', '73' records are LDU2
                                if ((data[0U] == 0x6BU) && (data[22U] == 0x6CU) &&
                                    (data[36U] == 0x6DU) && (data[53U] == 0x6EU) &&
                                    (data[70U] == 0x6FU) && (data[87U] == 0x70U) &&
                                    (data[104U] == 0x71U) && (data[121U] == 0x72U) &&
                                    (data[138U] == 0x73U))
                                {
                                    // The '6B' record - IMBE Voice 10
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 0, 22);
                                    count += 22;

                                    // The '6C' record - IMBE Voice 11
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 25, 14);
                                    count += 14;

                                    // The '6D' record - IMBE Voice 12 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 50, 17);
                                    newMI[0] = data[count + 1];
                                    newMI[1] = data[count + 2];
                                    newMI[2] = data[count + 3];
                                    count += 17;

                                    // The '6E' record - IMBE Voice 13 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 75, 17);
                                    newMI[3] = data[count + 1];
                                    newMI[4] = data[count + 2];
                                    newMI[5] = data[count + 3];
                                    count += 17;

                                    // The '6F' record - IMBE Voice 14 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 100, 17);
                                    newMI[6] = data[count + 1];
                                    newMI[7] = data[count + 2];
                                    newMI[8] = data[count + 3];
                                    count += 17;

                                    // The '70' record - IMBE Voice 15 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 125, 17);
                                    channel.algId = data[count + 1];                                    // Algorithm ID
                                    channel.kId = (ushort)((data[count + 2] << 8) | data[count + 3]);   // Key ID
                                    count += 17;

                                    // The '71' record - IMBE Voice 16 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 150, 17);
                                    count += 17;

                                    // The '72' record - IMBE Voice 17 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 175, 17);
                                    count += 17;

                                    // The '73' record - IMBE Voice 18 + Low Speed Data
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 200, 16);
                                    count += 16;

                                    if (channel.p25Errs > 0) // temp, need to actually get errors I guess
                                        P25Crypto.CycleP25Lfsr(channel.mi);
                                    else
                                        Array.Copy(newMI, channel.mi, P25Defines.P25_MI_LENGTH);

                                    // decode 9 IMBE codewords into PCM samples
                                    P25DecodeAudioFrame(channel.netLDU2, e, handler, channel, isEmergency, P25Crypto.FrameType.LDU2);
                                }
                            }
                            break;
                    }

                    if (channel.mi != null)
                        channel.crypter.Prepare(channel.algId, channel.kId, P25Crypto.ProtocolType.P25Phase1, channel.mi);

                    slot.RxRFS = e.SrcId;
                    slot.RxType = e.FrameType;
                    slot.RxTGId = e.DstId;
                    slot.RxTime = pktTime;
                    slot.RxStreamId = e.StreamId;

                }
            });
        }

        private void CallHist_Click(object sender, RoutedEventArgs e)
        {
            callHistoryWindow.Show();
        }
    }
}
