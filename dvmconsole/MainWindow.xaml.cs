// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 J. Dean
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*
*/

using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using NAudio.Wave;
using NWaves.Signals;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using dvmconsole.Controls;

using Constants = fnecore.Constants;
using fnecore;
using fnecore.DMR;
using fnecore.P25;
using fnecore.P25.KMM;
using fnecore.P25.LC.TSBK;

namespace dvmconsole
{
    /// <summary>
    /// Data structure representing the position of a <see cref="ChannelBox"/> widget.
    /// </summary>
    public class ChannelPosition
    {
        /*
         ** Properties
         */

        /// <summary>
        /// X
        /// </summary>
        public double X { get; set; }
        /// <summary>
        /// Y
        /// </summary>
        public double Y { get; set; }
    } // public class ChannelPosition

    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        public const double MIN_WIDTH = 875;
        public const double MIN_HEIGHT = 700;
        public const int MBE_SAMPLES_LENGTH = 160;
        public const int PCM_SAMPLES_LENGTH = 320; // MBE_SAMPLES_LENGTH * 2

        public const int MAX_SYSTEM_NAME_LEN = 10;
        public const int MAX_CHANNEL_NAME_LEN = 15;

        private const string INVALID_SYSTEM = "INVALID SYSTEM";
        private const string INVALID_CODEPLUG_CHANNEL = "INVALID CODEPLUG CHANNEL";
        private const string ERR_INVALID_FNE_REF = "invalid FNE peer reference, this should not happen";
        private const string ERR_INVALID_CODEPLUG = "Codeplug has/may contain errors";
        private const string ERR_SKIPPING_AUDIO = "Skipping channel for audio";

        private const string PLEASE_CHECK_CODEPLUG = "Please check your codeplug for errors.";
        private const string PLEASE_RESTART_CONSOLE = "Please restart the console.";

        private const string URI_RESOURCE_PATH = "pack://application:,,,/dvmconsole;component";

        private bool isShuttingDown = false;
        private bool globalPttState = false;

        private const int GridSize = 5;

        private UIElement draggedElement;
        private Point startPoint;
        private double offsetX;
        private double offsetY;
        private bool isDragging;

        private bool windowLoaded = false;
        private bool noSaveSettingsOnClose = false;
        private SettingsManager settingsManager = new SettingsManager();
        private SelectedChannelsManager selectedChannelsManager;
        private FlashingBackgroundManager flashingManager;
        private WaveFilePlaybackManager emergencyAlertPlayback;

        private ChannelBox playbackChannelBox;

        CallHistoryWindow callHistoryWindow = new CallHistoryWindow();

        public static string PLAYBACKTG = "LOCPLAYBACK";
        public static string PLAYBACKSYS = "Local Playback";
        public static string PLAYBACKCHNAME = "PLAYBACK";

        private readonly WaveInEvent waveIn;
        private readonly AudioManager audioManager;

        private static System.Timers.Timer channelHoldTimer;

        private Dictionary<string, SlotStatus> systemStatuses = new Dictionary<string, SlotStatus>();
        private FneSystemManager fneSystemManager = new FneSystemManager();

        private bool selectAll = false;

        /*
         ** Properties
         */

        /// <summary>
        /// Codeplug
        /// </summary>
        public Codeplug Codeplug { get; set; }

        /*
         ** Methods
         */

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            MinWidth = Width = MIN_WIDTH;
            MinHeight = Height = MIN_HEIGHT;

            DisableControls();

            settingsManager.LoadSettings();

            selectedChannelsManager = new SelectedChannelsManager();
            flashingManager = new FlashingBackgroundManager(null, channelsCanvas, null, this);
            emergencyAlertPlayback = new WaveFilePlaybackManager(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/emergency.wav"));

            channelHoldTimer = new System.Timers.Timer(10000);
            channelHoldTimer.Elapsed += OnHoldTimerElapsed;
            channelHoldTimer.AutoReset = true;
            channelHoldTimer.Enabled = true;

            waveIn = new WaveInEvent { WaveFormat = new WaveFormat(8000, 16, 1) };
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;

            waveIn.StartRecording();

            audioManager = new AudioManager(settingsManager);

            btnGlobalPtt.PreviewMouseLeftButtonDown += btnGlobalPtt_MouseLeftButtonDown;
            btnGlobalPtt.PreviewMouseLeftButtonUp += btnGlobalPtt_MouseLeftButtonUp;
            btnGlobalPtt.MouseRightButtonDown += btnGlobalPtt_MouseRightButtonDown;

            selectedChannelsManager.SelectedChannelsChanged += SelectedChannelsChanged;
            selectedChannelsManager.PrimaryChannelChanged += PrimaryChannelChanged;
            SizeChanged += MainWindow_SizeChanged;
            Loaded += MainWindow_Loaded;
        }

        private void PrimaryChannelChanged()
        {
            var primaryChannel = selectedChannelsManager.PrimaryChannel;
            foreach (UIElement element in channelsCanvas.Children)
            {
                if (element is ChannelBox box)
                {
                    box.IsPrimary = box == primaryChannel;
                }
            }
        }

        /// <summary>
        /// Helper to enable menu controls for Commands submenu.
        /// </summary>
        private void EnableCommandControls()
        {
            menuPageSubscriber.IsEnabled = true;
            menuRadioCheckSubscriber.IsEnabled = true;
            menuInhibitSubscriber.IsEnabled = true;
            menuUninhibitSubscriber.IsEnabled = true;
            menuQuickCall2.IsEnabled = true;
        }

        /// <summary>
        /// Helper to enable form controls when settings and codeplug are loaded.
        /// </summary>
        private void EnableControls()
        {
            btnGlobalPtt.IsEnabled = true;
            btnAlert1.IsEnabled = true;
            btnAlert2.IsEnabled = true;
            btnAlert3.IsEnabled = true;
            btnPageSub.IsEnabled = true;
            btnSelectAll.IsEnabled = true;
            btnKeyStatus.IsEnabled = true;
            btnCallHistory.IsEnabled = true;
        }

        /// <summary>
        /// Helper to disable menu controls for Commands submenu.
        /// </summary>
        private void DisableCommandControls()
        {
            menuPageSubscriber.IsEnabled = false;
            menuRadioCheckSubscriber.IsEnabled = false;
            menuInhibitSubscriber.IsEnabled = false;
            menuUninhibitSubscriber.IsEnabled = false;
            menuQuickCall2.IsEnabled = false;
        }

        /// <summary>
        /// Helper to disable form controls when settings load fails.
        /// </summary>
        private void DisableControls()
        {
            DisableCommandControls();

            btnGlobalPtt.IsEnabled = false;
            btnAlert1.IsEnabled = false;
            btnAlert2.IsEnabled = false;
            btnAlert3.IsEnabled = false;
            btnPageSub.IsEnabled = false;
            btnSelectAll.IsEnabled = false;
            btnKeyStatus.IsEnabled = false;
            btnCallHistory.IsEnabled = false;
        }

        /// <summary>
        /// Helper to load the codeplug.
        /// </summary>
        /// <param name="filePath"></param>
        private void LoadCodeplug(string filePath)
        {
            DisableControls();

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                string yaml = File.ReadAllText(filePath);
                Codeplug = deserializer.Deserialize<Codeplug>(yaml);

                // perform codeplug validation
                List<string> errors = new List<string>();

                // ensure string lengths are acceptable
                // systems
                Dictionary<string, string> replacedSystemNames = new Dictionary<string, string>();
                foreach (Codeplug.System system in Codeplug.Systems)
                {
                    // ensure system name is less then or equals to the max
                    if (system.Name.Length > MAX_SYSTEM_NAME_LEN)
                    {
                        string original = system.Name;
                        system.Name = system.Name.Substring(0, MAX_SYSTEM_NAME_LEN);
                        replacedSystemNames.Add(original, system.Name);
                        Log.WriteLine($"{original} SYSTEM NAME was greater then {MAX_SYSTEM_NAME_LEN} characters, truncated {system.Name}");
                    }
                }

                // zones
                foreach (Codeplug.Zone zone in Codeplug.Zones)
                {
                    // channels
                    foreach (Codeplug.Channel channel in zone.Channels)
                    {
                        if (Codeplug.Systems.Find((x) => x.Name == channel.System) == null)
                            errors.Add($"{channel.Name} refers to an {INVALID_SYSTEM} {channel.System}.");

                        // because we possibly truncated system names above lets see if we
                        // have to replaced the related system name
                        if (replacedSystemNames.ContainsKey(channel.System))
                            channel.System = replacedSystemNames[channel.System];

                        // ensure channel name is less then or equals to the max
                        if (channel.Name.Length > MAX_CHANNEL_NAME_LEN)
                        {
                            string original = channel.Name;
                            channel.Name = channel.Name.Substring(0, MAX_CHANNEL_NAME_LEN);
                            Log.WriteLine($"{original} CHANNEL NAME was greater then {MAX_CHANNEL_NAME_LEN} characters, truncated {channel.Name}");
                        }
                    }
                }

                // compile list of errors and throw up a messagebox of doom
                if (errors.Count > 0)
                {
                    string newLine = Environment.NewLine + Environment.NewLine;
                    string messageBoxString = $"Loaded codeplug {filePath} contains errors. {PLEASE_CHECK_CODEPLUG}" + newLine;
                    foreach (string error in errors)
                        messageBoxString += error + newLine;
                    messageBoxString = messageBoxString.TrimEnd(new char[] { '\r', '\n' });

                    MessageBox.Show(messageBoxString, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // generate widgets and enable controls
                GenerateChannelWidgets();
                EnableControls();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading codeplug: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.StackTrace(ex, false);
                DisableControls();
            }
        }

        /// <summary>
        /// Helper to initialize and generate channel widgets on the canvas.
        /// </summary>
        private void GenerateChannelWidgets()
        {
            channelsCanvas.Children.Clear();
            systemStatuses.Clear();

            fneSystemManager.ClearAll();

            double offsetX = 20;
            double offsetY = 20;

            Cursor = Cursors.Wait;

            if (Codeplug != null)
            {
                // load and initialize systems
                foreach (var system in Codeplug.Systems)
                {
                    SystemStatusBox systemStatusBox = new SystemStatusBox(system.Name, system.Address, system.Port);
                    if (settingsManager.SystemStatusPositions.TryGetValue(system.Name, out var position))
                    {
                        Canvas.SetLeft(systemStatusBox, position.X);
                        Canvas.SetTop(systemStatusBox, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(systemStatusBox, offsetX);
                        Canvas.SetTop(systemStatusBox, offsetY);
                    }

                    // widget placement
                    systemStatusBox.MouseRightButtonDown += SystemStatusBox_MouseRightButtonDown;
                    systemStatusBox.MouseRightButtonUp += SystemStatusBox_MouseRightButtonUp;
                    systemStatusBox.MouseMove += SystemStatusBox_MouseMove;

                    channelsCanvas.Children.Add(systemStatusBox);

                    offsetX += 225;
                    if (offsetX + 220 > channelsCanvas.ActualWidth)
                    {
                        offsetX = 20;
                        offsetY += 106;
                    }

                    // do we have aliases for this system?
                    if (File.Exists(system.AliasPath))
                        system.RidAlias = AliasTools.LoadAliases(system.AliasPath);

                    fneSystemManager.AddFneSystem(system.Name, system, this);
                    PeerSystem peer = fneSystemManager.GetFneSystem(system.Name);

                    // hook FNE events
                    peer.peer.PeerConnected += (sender, response) =>
                    {
                        Log.WriteLine("FNE Peer connected");
                        Dispatcher.Invoke(() =>
                        {
                            EnableCommandControls();
                            systemStatusBox.Background = ChannelBox.GREEN_GRADIENT;
                            systemStatusBox.ConnectionState = "Connected";
                        });
                    };

                    peer.peer.PeerDisconnected += (response) =>
                    {
                        Log.WriteLine("FNE Peer disconnected");
                        Dispatcher.Invoke(() =>
                        {
                            DisableCommandControls();
                            systemStatusBox.Background = ChannelBox.RED_GRADIENT;
                            systemStatusBox.ConnectionState = "Disconnected";
                        });
                    };

                    // start peer
                    Task.Run(() =>
                    {
                        try
                        {
                            peer.Start();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Fatal error while connecting to server. {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            Log.StackTrace(ex, false);
                        }
                    });

                    if (!settingsManager.ShowSystemStatus)
                        systemStatusBox.Visibility = Visibility.Collapsed;
                }
            }

            // are we showing channels?
            if (settingsManager.ShowChannels && Codeplug != null)
            {
                // iterate through the coeplug zones and begin building channel widgets
                foreach (var zone in Codeplug.Zones)
                {
                    // iterate through zone channels
                    foreach (var channel in zone.Channels)
                    {
                        ChannelBox channelBox = new ChannelBox(selectedChannelsManager, audioManager, channel.Name, channel.System, channel.Tgid, settingsManager.TogglePTTMode);
                        channelBox.ChannelMode = channel.Mode.ToUpperInvariant();
                        if (channel.GetAlgoId() != P25Defines.P25_ALGO_UNENCRYPT && channel.GetKeyId() > 0)
                            channelBox.IsTxEncrypted = true;

                        systemStatuses.Add(channel.Name, new SlotStatus());

                        if (settingsManager.ChannelPositions.TryGetValue(channel.Name, out var position))
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
                        channelBox.PTTButtonPressed += ChannelBox_PTTButtonPressed;
                        channelBox.PTTButtonReleased += ChannelBox_PTTButtonReleased;
                        channelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
                        channelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

                        // widget placement
                        channelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
                        channelBox.MouseRightButtonUp += ChannelBox_MouseRightButtonUp;
                        channelBox.MouseMove += ChannelBox_MouseMove;

                        channelsCanvas.Children.Add(channelBox);

                        offsetX += 225;

                        if (offsetX + 220 > channelsCanvas.ActualWidth)
                        {
                            offsetX = 20;
                            offsetY += 106;
                        }
                    }
                }
            }

            // are we showing user configured alert tones?
            if (settingsManager.ShowAlertTones && Codeplug != null)
            {
                // iterate through the alert tones and begin building alert tone widges
                foreach (var alertPath in settingsManager.AlertToneFilePaths)
                {
                    AlertTone alertTone = new AlertTone(alertPath);

                    alertTone.OnAlertTone += SendAlertTone;

                    // widget placement
                    alertTone.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
                    alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;
                    alertTone.MouseMove += AlertTone_MouseMove;

                    if (settingsManager.AlertTonePositions.TryGetValue(alertPath, out var position))
                    {
                        Canvas.SetLeft(alertTone, position.X);
                        Canvas.SetTop(alertTone, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(alertTone, 20);
                        Canvas.SetTop(alertTone, 20);
                    }

                    channelsCanvas.Children.Add(alertTone);
                }
            }

            // initialize the playback channel
            playbackChannelBox = new ChannelBox(selectedChannelsManager, audioManager, PLAYBACKCHNAME, PLAYBACKSYS, PLAYBACKTG);
            playbackChannelBox.ChannelMode = "Local";
            playbackChannelBox.HidePTTButton(); // playback box shouldn't have PTT

            if (settingsManager.ChannelPositions.TryGetValue(PLAYBACKCHNAME, out var pos))
            {
                Canvas.SetLeft(playbackChannelBox, pos.X);
                Canvas.SetTop(playbackChannelBox, pos.Y);
            }
            else
            {
                Canvas.SetLeft(playbackChannelBox, offsetX);
                Canvas.SetTop(playbackChannelBox, offsetY);
            }

            playbackChannelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
            playbackChannelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

            // widget placement
            playbackChannelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
            playbackChannelBox.MouseRightButtonUp += ChannelBox_MouseRightButtonUp;
            playbackChannelBox.MouseMove += ChannelBox_MouseMove;

            channelsCanvas.Children.Add(playbackChannelBox);

            Cursor = Cursors.Arrow;
        }

        /// <summary>
        /// 
        /// </summary>
        private void SelectedChannelsChanged()
        {
            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                // is the channel selected?
                if (channel.IsSelected)
                {
                    // if the channel is configured for encryption request the key from the FNE
                    uint newTgid = uint.Parse(cpgChannel.Tgid);
                    if (cpgChannel.GetAlgoId() != 0 && cpgChannel.GetKeyId() != 0)
                    {
                        fne.peer.SendMasterKeyRequest(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId());
                        if (Codeplug.KeyFile != null)
                        {
                            if (!File.Exists(Codeplug.KeyFile))
                            {
                                MessageBox.Show($"Key file {Codeplug.KeyFile} not found. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            else
                            {
                                var deserializer = new DeserializerBuilder()
                                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                    .IgnoreUnmatchedProperties()
                                    .Build();
                                var keys = deserializer.Deserialize<KeyContainer>(File.ReadAllText(Codeplug.KeyFile));
                                var KeysetItems = new Dictionary<int, KeysetItem>();

                                foreach (var keyEntry in keys.Keys)
                                {
                                    var keyItem = new KeyItem();
                                    keyItem.KeyId = keyEntry.KeyId;
                                    var keyBytes = keyEntry.KeyBytes;
                                    keyItem.SetKey(keyBytes,(uint)keyBytes.Length);
                                    if (!KeysetItems.ContainsKey(keyEntry.AlgId))
                                    {
                                        var asByte = (byte)keyEntry.AlgId;
                                        KeysetItems.Add(keyEntry.AlgId, new KeysetItem() { AlgId = asByte });
                                    }


                                    KeysetItems[keyEntry.AlgId].AddKey(keyItem);
                                }
                                foreach (var eventData in KeysetItems.Select(keyValuePair => keyValuePair.Value).Select(keysetItem => new KeyResponseEvent(0, new KmmModifyKey
                                         {
                                             AlgId = 0,
                                             KeyId = 0,
                                             MessageId = 0,
                                             MessageLength = 0,
                                             KeysetItem = keysetItem
                                         }, [])))
                                {
                                    KeyResponseReceived(eventData);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        private void SendAlertTone(AlertTone e)
        {
            Task.Run(() => SendAlertTone(e.AlertFilePath));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="forHold"></param>
        private void SendAlertTone(string filePath, bool forHold = false)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var channel = selectedChannelsManager.PrimaryChannel;
                    if (channel == null)
                    {
                        return;
                    }

                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        return;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    if (system == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        return;
                    }

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (cpgChannel == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        return;
                    }

                    PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                    if (fne == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        return;
                    }

                    //
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
                                audioManager.AddTalkgroupStream(cpgChannel.Tgid, pcmData);
                            });

                            DateTime startTime = DateTime.UtcNow;

                            for (int i = 0; i < totalChunks; i++)
                            {
                                int offset = i * chunkSize;
                                byte[] chunk = new byte[chunkSize];
                                Buffer.BlockCopy(pcmData, offset, chunk, 0, chunkSize);

                                channel.chunkedPCM = AudioConverter.SplitToChunks(chunk);

                                foreach (byte[] audioChunk in channel.chunkedPCM)
                                {
                                    if (audioChunk.Length == PCM_SAMPLES_LENGTH)
                                    {
                                        if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                            P25EncodeAudioFrame(audioChunk, fne, channel, cpgChannel, system);
                                        else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                            DMREncodeAudioFrame(audioChunk, fne, channel, cpgChannel, system);
                                    }
                                }

                                DateTime nextPacketTime = startTime.AddMilliseconds((i + 1) * 100);
                                TimeSpan waitTime = nextPacketTime - DateTime.UtcNow;

                                if (waitTime.TotalMilliseconds > 0)
                                    await Task.Delay(waitTime);
                            }

                            double totalDurationMs = ((double)pcmData.Length / 16000) + 250;
                            await Task.Delay((int)totalDurationMs + 3000);

                            fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false);

                            Dispatcher.Invoke(() =>
                            {
                                if (forHold)
                                    channel.PttButton.Background = ChannelBox.GRAY_GRADIENT;
                                else
                                    channel.PageState = false;
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to process alert tone: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log.StackTrace(ex, false);
                }
            }
            else
                MessageBox.Show("Alert file not set or file not found.", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dstId"></param>
        /// <param name="srcId"></param>
        private void HandleEmergency(string dstId, string srcId)
        {
            bool forUs = false;

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
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
                    flashingManager.Start();
                    emergencyAlertPlayback.Start();
                });
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdateBackground()
        {
            BitmapImage bg = new BitmapImage();

            // do we have a user defined background?
            if (settingsManager.UserBackgroundImage != null)
            {
                // does the file exist?
                if (File.Exists(settingsManager.UserBackgroundImage))
                {
                    bg.BeginInit();
                    bg.UriSource = new Uri(settingsManager.UserBackgroundImage);
                    bg.EndInit();

                    channelsCanvasBg.ImageSource = bg;
                    return;
                }
            }

            bg.BeginInit();
            if (settingsManager.DarkMode)
                bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_dark.png");
            else
                bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_light.png");
            bg.EndInit();

            channelsCanvasBg.ImageSource = bg;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnHoldTimerElapsed(object sender, ElapsedEventArgs e)
        {
            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                if (channel.HoldState && !channel.IsReceiving && !channel.PttState && !channel.PageState)
                {
                    handler.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
                    await Task.Delay(1000);

                    SendAlertTone(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/hold.wav"), true);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            isShuttingDown = true;

            waveIn.StopRecording();

            fneSystemManager.ClearAll();

            if (!noSaveSettingsOnClose)
            {
                if (WindowState == WindowState.Maximized)
                    settingsManager.Maximized = true;

                settingsManager.SaveSettings();
            }

            base.OnClosing(e);
            Application.Current.Shutdown();
        }

        /** NAudio Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WaveIn_RecordingStopped(object sender, EventArgs e)
        {
            /* stub */
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            bool isAnyTgOn = false;
            if (isShuttingDown)
                return;

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                // is the channel selected and in a PTT state?
                if (channel.IsSelected && channel.PttState)
                {
                    isAnyTgOn = true;
                    Task.Run(() =>
                    {
                        channel.chunkedPCM = AudioConverter.SplitToChunks(e.Buffer);
                        foreach (byte[] chunk in channel.chunkedPCM)
                        {
                            if (chunk.Length == PCM_SAMPLES_LENGTH)
                            {
                                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                    P25EncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                    DMREncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                            }
                            else
                                Log.WriteLine("bad sample length: " + chunk.Length);
                        }
                    });
                }
            }

            if (playbackChannelBox != null && isAnyTgOn && playbackChannelBox.IsSelected)
                audioManager.AddTalkgroupStream(PLAYBACKTG, e.Buffer);
        }

        /** WPF Window Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;

            if (!windowLoaded)
                return;

            if (ActualWidth > channelsCanvas.ActualWidth)
            {
                channelsCanvas.Width = ActualWidth;
                canvasScrollViewer.Width = ActualWidth;
            }
            else
                canvasScrollViewer.Width = Width - widthOffset;

            if (ActualHeight > channelsCanvas.ActualHeight)
            {
                channelsCanvas.Height = ActualHeight;
                canvasScrollViewer.Height = ActualHeight;
            }
            else
                canvasScrollViewer.Height = Height - heightOffset;

            if (WindowState == WindowState.Maximized)
                ResizeCanvasToWindow_Click(sender, e);
            else
                settingsManager.Maximized = false;

            settingsManager.CanvasWidth = channelsCanvas.ActualWidth;
            settingsManager.CanvasHeight = channelsCanvas.ActualHeight;

            settingsManager.WindowWidth = ActualWidth;
            settingsManager.WindowHeight = ActualHeight;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;

            // set PTT toggle mode (this must be done before channel widgets are defined)
            menuToggleLockWidgets.IsChecked = settingsManager.LockWidgets;
            menuTogglePTTMode.IsChecked = settingsManager.TogglePTTMode;

            if (!string.IsNullOrEmpty(settingsManager.LastCodeplugPath) && File.Exists(settingsManager.LastCodeplugPath))
                LoadCodeplug(settingsManager.LastCodeplugPath);
            else
                GenerateChannelWidgets();

            // set background configuration
            menuDarkMode.IsChecked = settingsManager.DarkMode;
            UpdateBackground();

            // set window configuration
            if (settingsManager.Maximized)
            {
                windowLoaded = true;
                WindowState = WindowState.Maximized;
                ResizeCanvasToWindow_Click(sender, e);
            }
            else
            {
                Width = settingsManager.WindowWidth;
                channelsCanvas.Width = settingsManager.CanvasWidth;
                if (settingsManager.CanvasWidth > settingsManager.WindowWidth)
                    canvasScrollViewer.Width = Width - widthOffset;
                else
                    canvasScrollViewer.Width = Width;

                Height = settingsManager.WindowHeight;
                channelsCanvas.Height = settingsManager.CanvasHeight;
                if (settingsManager.CanvasHeight > settingsManager.WindowHeight)
                    canvasScrollViewer.Height = Height - heightOffset;
                else
                    canvasScrollViewer.Height = Height;

                windowLoaded = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

                settingsManager.LastCodeplugPath = openFileDialog.FileName;
                noSaveSettingsOnClose = false;
                settingsManager.SaveSettings();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PageRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Page Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                PeerSystem handler = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_CALL_ALRT callAlert = new IOSP_CALL_ALRT(uint.Parse(pageWindow.DstId), uint.Parse(pageWindow.RadioSystem.Rid));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_CALL_ALRT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                callAlert.Encode(ref tsbk);

                handler.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RadioCheckRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Radio Check Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                PeerSystem handler = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.CHECK, uint.Parse(pageWindow.RadioSystem.Rid), uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                handler.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InhibitRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Inhibit Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                PeerSystem handler = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.INHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                handler.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninhibitRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Uninhibit Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                PeerSystem handler = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.UNINHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                handler.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ManualPage_Click(object sender, RoutedEventArgs e)
        {
            QuickCallPage pageWindow = new QuickCallPage();
            pageWindow.Owner = this;

            if (pageWindow.ShowDialog() == true)
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    if (system == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (cpgChannel == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                    if (fne == null)
                    {
                        MessageBox.Show($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    // 
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

                        int chunkSize = PCM_SAMPLES_LENGTH;
                        int totalChunks = (combinedAudio.Length + chunkSize - 1) / chunkSize;

                        Task.Run(() =>
                        {
                            //_waveProvider.ClearBuffer();
                            audioManager.AddTalkgroupStream(cpgChannel.Tgid, combinedAudio);
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
                                    if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                        P25EncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                    else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                        DMREncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                }
                            }
                        });

                        double totalDurationMs = (toneADuration + toneBDuration) * 1000 + 750;
                        await Task.Delay((int)totalDurationMs  + 4000);

                        fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false);

                        Dispatcher.Invoke(() =>
                        {
                            //channel.PageState = false; // TODO: Investigate
                            channel.PageSelectButton.Background = ChannelBox.GRAY_GRADIENT;
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TogglePTTMode_Click(object sender, RoutedEventArgs e)
        {
            settingsManager.TogglePTTMode = menuTogglePTTMode.IsChecked;

            // update elements
            foreach (UIElement child in channelsCanvas.Children)
            {
                if (child is ChannelBox)
                    ((ChannelBox)child).PTTToggleMode = settingsManager.TogglePTTMode;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AudioSettings_Click(object sender, RoutedEventArgs e)
        {
            List<Codeplug.Channel> channels = Codeplug?.Zones.SelectMany(z => z.Channels).ToList() ?? new List<Codeplug.Channel>();

            AudioSettingsWindow audioSettingsWindow = new AudioSettingsWindow(settingsManager, audioManager, channels);
            audioSettingsWindow.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var confirmResult = MessageBox.Show("Are you sure to wish to reset console settings?", "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmResult == MessageBoxResult.Yes)
            {
                MessageBox.Show("Settings will be reset after console restart.", "Reset Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                noSaveSettingsOnClose = true;
                settingsManager.Reset();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectWidgets_Click(object sender, RoutedEventArgs e)
        {
            WidgetSelectionWindow widgetSelectionWindow = new WidgetSelectionWindow();
            widgetSelectionWindow.Owner = this;
            if (widgetSelectionWindow.ShowDialog() == true)
            {
                settingsManager.ShowSystemStatus = widgetSelectionWindow.ShowSystemStatus;
                settingsManager.ShowChannels = widgetSelectionWindow.ShowChannels;
                settingsManager.ShowAlertTones = widgetSelectionWindow.ShowAlertTones;

                GenerateChannelWidgets();
                if (!noSaveSettingsOnClose)
                    settingsManager.SaveSettings();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
                AlertTone alertTone = new AlertTone(alertFilePath);

                alertTone.OnAlertTone += SendAlertTone;

                // widget placement
                alertTone.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
                alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;
                alertTone.MouseMove += AlertTone_MouseMove;

                if (settingsManager.AlertTonePositions.TryGetValue(alertFilePath, out var position))
                {
                    Canvas.SetLeft(alertTone, position.X);
                    Canvas.SetTop(alertTone, position.Y);
                }
                else
                {
                    Canvas.SetLeft(alertTone, 20);
                    Canvas.SetTop(alertTone, 20);
                }

                channelsCanvas.Children.Add(alertTone);
                settingsManager.UpdateAlertTonePaths(alertFilePath);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenUserBackground_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JPEG Files (*.jpg)|*.jpg|PNG Files (*.png)|*.png|All Files (*.*)|*.*",
                Title = "Open User Background"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                settingsManager.UserBackgroundImage = openFileDialog.FileName;
                settingsManager.SaveSettings();
                UpdateBackground();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.DarkMode = menuDarkMode.IsChecked;
            UpdateBackground();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleLockWidgets_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.LockWidgets = !settingsManager.LockWidgets;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResizeCanvasToWindow_Click(object sender, RoutedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;

            foreach (UIElement child in channelsCanvas.Children)
            {
                double childLeft = Canvas.GetLeft(child) + child.RenderSize.Width;
                if (childLeft > ActualWidth)
                    Canvas.SetLeft(child, ActualWidth - (child.RenderSize.Width + widthOffset));
                double childBottom = Canvas.GetTop(child) + child.RenderSize.Height;
                if (childBottom > ActualHeight)
                    Canvas.SetTop(child, ActualHeight - (child.RenderSize.Height + heightOffset));
            }

            channelsCanvas.Width = ActualWidth;
            canvasScrollViewer.Width = ActualWidth;
            channelsCanvas.Height = ActualHeight;
            canvasScrollViewer.Height = ActualHeight;

            settingsManager.CanvasWidth = ActualWidth;
            settingsManager.CanvasHeight = ActualHeight;

            settingsManager.WindowWidth = ActualWidth;
            settingsManager.WindowHeight = ActualHeight;
        }

        /** Widget Controls */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_HoldChannelButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_PageButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            if (system == null)
            {
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            if (cpgChannel == null)
            {
                // bryanb: this should actually never happen...
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
            if (fne == null)
            {
                MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            if (e.PageState)
                fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
            else
                fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_PTTButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            if (system == null)
            {
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            if (cpgChannel == null)
            {
                // bryanb: this should actually never happen...
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
            if (fne == null)
            {
                MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            if (!e.IsSelected)
                return;

            FneUtils.Memset(e.mi, 0x00, P25Defines.P25_MI_LENGTH);

            uint srcId = uint.Parse(system.Rid);
            uint dstId = uint.Parse(cpgChannel.Tgid);

            if (e.PttState)
            {
                if (e.TxStreamId != 0)
                    Log.WriteWarning($"{e.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                e.TxStreamId = fne.NewStreamId();
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL START     * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, true);
            }
            else
            {
                e.VolumeMeterLevel = 0;
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL END       * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, false);
                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                    fne.SendDMRTerminator(srcId, dstId, 1, e.dmrSeqNo, e.dmrN, e.embeddedData);

                // reset values
                e.p25SeqNo = 0;
                e.p25N = 0;

                e.dmrSeqNo = 0;
                e.dmrN = 0;

                e.pktSeq = 0;

                e.TxStreamId = 0;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void ChannelBox_PTTButtonPressed(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            if (!e.PttState)
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                if (!e.IsSelected)
                    return;

                FneUtils.Memset(e.mi, 0x00, P25Defines.P25_MI_LENGTH);

                uint srcId = uint.Parse(system.Rid);
                uint dstId = uint.Parse(cpgChannel.Tgid);

                if (e.TxStreamId != 0)
                    Log.WriteWarning($"{e.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                e.TxStreamId = fne.NewStreamId();
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL START     * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, true);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void ChannelBox_PTTButtonReleased(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            if (e.PttState)
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                if (!e.IsSelected)
                    return;

                uint srcId = uint.Parse(system.Rid);
                uint dstId = uint.Parse(cpgChannel.Tgid);

                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL END       * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, false);
                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                    fne.SendDMRTerminator(srcId, dstId, 1, e.dmrSeqNo, e.dmrN, e.embeddedData);

                // reset values
                e.p25SeqNo = 0;
                e.p25N = 0;

                e.dmrSeqNo = 0;
                e.dmrN = 0;

                e.pktSeq = 0;

                e.TxStreamId = 0;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets || !(sender is UIElement element))
                return;

            draggedElement = element;
            startPoint = e.GetPosition(channelsCanvas);
            offsetX = startPoint.X - Canvas.GetLeft(draggedElement);
            offsetY = startPoint.Y - Canvas.GetTop(draggedElement);
            isDragging = true;

            Cursor = Cursors.ScrollAll;

            element.CaptureMouse();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets || !isDragging || draggedElement == null)
                return;

            Cursor = Cursors.Arrow;

            isDragging = false;
            draggedElement.ReleaseMouseCapture();
            draggedElement = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (settingsManager.LockWidgets || !isDragging || draggedElement == null) 
                return;

            Point currentPosition = e.GetPosition(channelsCanvas);

            // Calculate the new position with snapping to the grid
            double newLeft = Math.Round((currentPosition.X - offsetX) / GridSize) * GridSize;
            double newTop = Math.Round((currentPosition.Y - offsetY) / GridSize) * GridSize;

            // Ensure the box stays within canvas bounds
            newLeft = Math.Max(0, Math.Min(newLeft, channelsCanvas.ActualWidth - draggedElement.RenderSize.Width));
            newTop = Math.Max(0, Math.Min(newTop, channelsCanvas.ActualHeight - draggedElement.RenderSize.Height));

            // Apply snapped position
            Canvas.SetLeft(draggedElement, newLeft);
            Canvas.SetTop(draggedElement, newTop);

            // Save the new position if it's a ChannelBox
            if (draggedElement is ChannelBox channelBox)
                settingsManager.UpdateChannelPosition(channelBox.ChannelName, newLeft, newTop);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseRightButtonDown(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets)
                return;

            if (sender is SystemStatusBox systemStatusBox)
            {
                double x = Canvas.GetLeft(systemStatusBox);
                double y = Canvas.GetTop(systemStatusBox);
                settingsManager.SystemStatusPositions[systemStatusBox.SystemName] = new ChannelPosition { X = x, Y = y };

                ChannelBox_MouseRightButtonUp(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseRightButtonDown(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets)
                return;

            if (sender is AlertTone alertTone)
            {
                double x = Canvas.GetLeft(alertTone);
                double y = Canvas.GetTop(alertTone);
                settingsManager.UpdateAlertTonePosition(alertTone.AlertFileName, x, y);

                ChannelBox_MouseRightButtonUp(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);

        /** WPF Ribbon Controls */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ClearEmergency_Click(object sender, RoutedEventArgs e)
        {
            emergencyAlertPlayback.Stop();
            flashingManager.Stop();

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                channel.Emergency = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btnGlobalPtt_Click(object sender, RoutedEventArgs e)
        {
            if (globalPttState)
                await Task.Delay(500);
            ChannelBox channel = selectedChannelsManager.PrimaryChannel;
            if (channel == null)
            {
                return;
            }
            channel.PttButton_Click(sender,e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
                return;

            globalPttState = !globalPttState;

            btnGlobalPtt_Click(sender, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
            {
                globalPttState = !globalPttState;
                btnGlobalPtt_Click(sender, e);
            }
            else
            {
                globalPttState = true;
                btnGlobalPtt_Click(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
                return;

            globalPttState = false;
            btnGlobalPtt_Click(sender, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert1_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert1.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert2_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert2.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert3_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert3.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            selectAll = !selectAll;
            foreach (ChannelBox channel in channelsCanvas.Children.OfType<ChannelBox>())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                channel.IsSelected = selectAll;
                channel.Background = channel.IsSelected ? ChannelBox.BLUE_GRADIENT : ChannelBox.DARK_GRAY_GRADIENT;

                if (channel.IsSelected)
                    selectedChannelsManager.AddSelectedChannel(channel);
                else
                    selectedChannelsManager.RemoveSelectedChannel(channel);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void KeyStatus_Click(object sender, RoutedEventArgs e)
        {
            KeyStatusWindow keyStatus = new KeyStatusWindow(Codeplug, this);
            keyStatus.Owner = this;
            keyStatus.Show();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CallHist_Click(object sender, RoutedEventArgs e)
        {
            callHistoryWindow.Owner = this;
            callHistoryWindow.Show();
        }

        /** fnecore Hooks / Helpers */

        /// <summary>
        /// Helper to encode and transmit PCM audio as P25 IMBE frames.
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="fne"></param>
        /// <param name="channel"></param>
        /// <param name="cpgChannel"></param>
        /// <param name="system"></param>
        private void P25EncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel, Codeplug.Channel cpgChannel, Codeplug.System system)
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

            channel.VolumeMeterLevel = 0;

            float max = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                short sample = samples[index];

                // to floating point
                float sample32 = sample / 32768f;

                if (sample32 < 0)
                    sample32 = -sample32;

                // is this the max value?
                if (sample32 > max)
                    max = sample32;
            }

            channel.VolumeMeterLevel = max;

            // Convert to floats
            float[] fSamples = AudioConverter.PcmToFloat(samples);

            // Convert to signal
            DiscreteSignal signal = new DiscreteSignal(8000, fSamples, true);

            // encode PCM samples into IMBE codewords
            byte[] imbe = new byte[FneSystemBase.IMBE_BUF_LEN];

            int tone = 0;

            if (true) // TODO: Disable/enable detection
            {
                tone = channel.ToneDetector.Detect(signal);
            }

            if (tone > 0)
            {
                MBEToneGenerator.IMBEEncodeSingleTone((ushort)tone, imbe);
                Log.WriteLine($"({system.Name}) P25D: {tone} HZ TONE DETECT");
            }
            else
            {
                // do we have the external vocoder library?
                if (channel.ExternalVocoderEnabled)
                {
                    if (channel.ExtFullRateVocoder == null)
                        channel.ExtFullRateVocoder = new AmbeVocoder(true);

                    channel.ExtFullRateVocoder.encode(samples, out imbe);
                }
                else
                {
                    if (channel.Encoder == null)
                        channel.Encoder = new MBEEncoder(MBE_MODE.IMBE_88BIT);

                    channel.Encoder.encode(samples, imbe);
                }
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
                            channel.mi[i] = (byte)random.Next(0x00, 0x100);
                    }

                    channel.Crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                }

                // crypto time
                channel.Crypter.Process(imbe, channel.p25N < 9U ? P25DUID.LDU1 : P25DUID.LDU2);

                // last block of LDU2, prepare a new MI
                if (channel.p25N == 17U)
                {
                    P25Crypto.CycleP25Lfsr(channel.mi);
                    channel.Crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
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

            uint srcId = uint.Parse(system.Rid);
            uint dstId = uint.Parse(cpgChannel.Tgid);

            FnePeer peer = fne.peer;
            RemoteCallData callData = new RemoteCallData()
            {
                SrcId = srcId,
                DstId = dstId,
                LCO = P25Defines.LC_GROUP
            };

            // make sure we have a valid stream ID
            if (channel.TxStreamId == 0)
                Log.WriteWarning($"({channel.SystemName}) P25D: Traffic *VOICE FRAME    * Stream ID not set for traffic? Shouldn't happen.");

            // send P25 LDU1
            if (channel.p25N == 8U)
            {
                // bryanb: in multi-TG architecture we cannot use the pktSeq helper singleton in the FNE peer class otherwise we won't
                //  maintain outgoing RTP packet sequences properly
                if (channel.p25SeqNo == 0U)
                    channel.pktSeq = 0;
                else
                {
                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;
                }

                Log.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME LDU1* PEER {fne.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.TxStreamId} SEQ {channel.p25SeqNo}]");

                byte[] payload = new byte[200];
                fne.CreateNewP25MessageHdr((byte)P25DUID.LDU1, callData, ref payload, cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                fne.CreateP25LDU1Message(channel.netLDU1, ref payload, srcId, dstId);

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, channel.pktSeq, channel.TxStreamId);
            }

            // send P25 LDU2
            if (channel.p25N == 17U)
            {
                // bryanb: in multi-TG architecture we cannot use the pktSeq helper singleton in the FNE peer class otherwise we won't
                //  maintain outgoing RTP packet sequences properly
                if (channel.p25SeqNo == 0U)
                    channel.pktSeq = 0;
                else
                {
                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;
                }

                Log.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME LDU2* PEER {fne.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.TxStreamId} SEQ {channel.p25SeqNo}]");

                byte[] payload = new byte[200];
                fne.CreateNewP25MessageHdr((byte)P25DUID.LDU2, callData, ref payload, cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                fne.CreateP25LDU2Message(channel.netLDU2, ref payload, new CryptoParams { AlgId = cpgChannel.GetAlgoId(), KeyId = cpgChannel.GetKeyId(), MI = channel.mi });

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, channel.pktSeq, channel.TxStreamId);
            }

            channel.p25SeqNo++;
            channel.p25N++;
        }

        /// <summary>
        /// Helper to decode and playback P25 IMBE frames as PCM audio.
        /// </summary>
        /// <param name="ldu"></param>
        /// <param name="e"></param>
        /// <param name="system"></param>
        /// <param name="channel"></param>
        /// <param name="emergency"></param>
        /// <param name="duid"></param>
        private void P25DecodeAudioFrame(byte[] ldu, P25DataReceivedEvent e, PeerSystem system, ChannelBox channel, bool emergency = false, P25DUID duid = P25DUID.LDU1)
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

                    channel.Crypter.Process(imbe, duid);

                    // do we have the external vocoder library?
                    if (channel.ExternalVocoderEnabled)
                    {
                        if (channel.ExtFullRateVocoder == null)
                            channel.ExtFullRateVocoder = new AmbeVocoder(true);

                        channel.p25Errs = channel.ExtFullRateVocoder.decode(imbe, out samples);
                    }
                    else
                        channel.p25Errs = channel.Decoder.decode(imbe, samples);

                    if (emergency && !channel.Emergency)
                    {
                        Task.Run(() =>
                        {
                            HandleEmergency(e.DstId.ToString(), e.SrcId.ToString());
                        });
                    }

                    if (samples != null)
                    {
                        Log.WriteLine($"P25D: Traffic *VOICE FRAME    * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} VC{n} [STREAM ID {e.StreamId}]");

                        channel.VolumeMeterLevel = 0;

                        float max = 0;
                        for (int index = 0; index < samples.Length; index++)
                        {
                            short sample = samples[index];
                            
                            // to floating point
                            float sample32 = sample / 32768f;

                            if (sample32 < 0) 
                                sample32 = -sample32;

                            // is this the max value?
                            if (sample32 > max)
                                max = sample32;
                        }

                        channel.VolumeMeterLevel = max;

                        int pcmIdx = 0;
                        byte[] pcmData = new byte[samples.Length * 2];
                        for (int i = 0; i < samples.Length; i++)
                        {
                            pcmData[pcmIdx] = (byte)(samples[i] & 0xFF);
                            pcmData[pcmIdx + 1] = (byte)((samples[i] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        audioManager.AddTalkgroupStream(e.DstId.ToString(), pcmData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Audio Decode Exception: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Helper to encode and transmit PCM audio as DMR AMBE frames.
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="fne"></param>
        /// <param name="channel"></param>
        /// <param name="cpgChannel"></param>
        /// <param name="system"></param>
        private void DMREncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel, Codeplug.Channel cpgChannel, Codeplug.System system)
        {
            // make sure we have a valid stream ID
            if (channel.TxStreamId == 0)
                Log.WriteWarning($"({channel.SystemName}) DMRD: Traffic *VOICE FRAME    * Stream ID not set for traffic? Shouldn't happen.");

            try
            {
                byte slot = 1; // TODO: Support both time slots

                byte[] data = null, dmrpkt = null;
                channel.dmrN = (byte)(channel.dmrSeqNo % 6);
                if (channel.ambeCount == FneSystemBase.AMBE_PER_SLOT)
                {
                    // is this the intitial sequence?
                    if (channel.dmrSeqNo == 0)
                    {
                        channel.pktSeq = 0;

                        // send DMR voice header
                        data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];

                        // generate DMR LC
                        LC dmrLC = new LC();
                        dmrLC.FLCO = (byte)DMRFLCO.FLCO_GROUP;
                        dmrLC.SrcId = uint.Parse(system.Rid);
                        dmrLC.DstId = uint.Parse(cpgChannel.Tgid);
                        channel.embeddedData.SetLC(dmrLC);

                        // generate the Slot Type
                        SlotType slotType = new SlotType();
                        slotType.DataType = (byte)DMRDataType.VOICE_LC_HEADER;
                        slotType.GetData(ref data);

                        FullLC.Encode(dmrLC, ref data, DMRDataType.VOICE_LC_HEADER);

                        // generate DMR network frame
                        dmrpkt = new byte[FneSystemBase.DMR_PACKET_SIZE];
                        fne.CreateDMRMessage(ref dmrpkt, uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), slot, FrameType.VOICE_SYNC, (byte)channel.dmrSeqNo, 0);
                        Buffer.BlockCopy(data, 0, dmrpkt, 20, FneSystemBase.DMR_FRAME_LENGTH_BYTES);

                        fne.peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR), dmrpkt, channel.pktSeq, channel.TxStreamId);

                        channel.dmrSeqNo++;
                    }

                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;

                    // send DMR voice
                    data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];

                    Buffer.BlockCopy(channel.ambeBuffer, 0, data, 0, 13);
                    data[13U] = (byte)(channel.ambeBuffer[13U] & 0xF0);
                    data[19U] = (byte)(channel.ambeBuffer[13U] & 0x0F);
                    Buffer.BlockCopy(channel.ambeBuffer, 14, data, 20, 13);

                    FrameType frameType = FrameType.VOICE_SYNC;
                    if (channel.dmrN == 0)
                        frameType = FrameType.VOICE_SYNC;
                    else
                    {
                        frameType = FrameType.VOICE;

                        byte lcss = channel.embeddedData.GetData(ref data, channel.dmrN);

                        // generated embedded signalling
                        EMB emb = new EMB();
                        emb.ColorCode = 0;
                        emb.LCSS = lcss;
                        emb.Encode(ref data);
                    }

                    // generate DMR network frame
                    dmrpkt = new byte[FneSystemBase.DMR_PACKET_SIZE];
                    fne.CreateDMRMessage(ref dmrpkt, uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), 1, frameType, (byte)channel.dmrSeqNo, channel.dmrN);
                    Buffer.BlockCopy(data, 0, dmrpkt, 20, FneSystemBase.DMR_FRAME_LENGTH_BYTES);

                    fne.peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR), dmrpkt, channel.pktSeq, channel.TxStreamId);

                    channel.dmrSeqNo++;

                    FneUtils.Memset(channel.ambeBuffer, 0, 27);
                    channel.ambeCount = 0;
                }

                int smpIdx = 0;
                short[] samples = new short[MBE_SAMPLES_LENGTH];
                for (int pcmIdx = 0; pcmIdx < pcm.Length; pcmIdx += 2)
                {
                    samples[smpIdx] = (short)((pcm[pcmIdx + 1] << 8) + pcm[pcmIdx + 0]);
                    smpIdx++;
                }

                // encode PCM samples into AMBE codewords
                byte[] ambe = null;

                if (channel.ExternalVocoderEnabled)
                {
                    if (channel.ExtHalfRateVocoder == null)
                        channel.ExtHalfRateVocoder = new AmbeVocoder(false);

                    channel.ExtHalfRateVocoder.encode(samples, out ambe, true);
                }
                else
                {
                    if (channel.Encoder == null)
                        channel.Encoder = new MBEEncoder(MBE_MODE.DMR_AMBE);

                    channel.Encoder.encode(samples, ambe);
                }

                Buffer.BlockCopy(ambe, 0, channel.ambeBuffer, channel.ambeCount * 9, FneSystemBase.AMBE_BUF_LEN);

                channel.ambeCount++;
            } 
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Helper to decode and playback DMR AMBE frames as PCM audio.
        /// </summary>
        /// <param name="ambe"></param>
        /// <param name="e"></param>
        /// <param name="system"></param>
        /// <param name="channel"></param>
        private void DMRDecodeAudioFrame(byte[] ambe, DMRDataReceivedEvent e, PeerSystem system, ChannelBox channel)
        {
            try
            {
                // Log.Logger.Debug($"FULL AMBE {FneUtils.HexDump(ambe)}");
                for (int n = 0; n < FneSystemBase.AMBE_PER_SLOT; n++)
                {
                    byte[] ambePartial = new byte[FneSystemBase.AMBE_BUF_LEN];
                    for (int i = 0; i < FneSystemBase.AMBE_BUF_LEN; i++)
                        ambePartial[i] = ambe[i + (n * 9)];

                    short[] samples = null;
                    int errs = 0;

                    // do we have the external vocoder library?
                    if (channel.ExternalVocoderEnabled)
                    {
                        if (channel.ExtHalfRateVocoder == null)
                            channel.ExtHalfRateVocoder = new AmbeVocoder(false);

                        errs = channel.ExtHalfRateVocoder.decode(ambePartial, out samples);
                    }
                    else
                    {
                        samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];
                        errs = channel.Decoder.decode(ambePartial, samples);
                    }

                    if (samples != null)
                    {
                        //Log.WriteLine($"({system.SystemName}) DMRD: Traffic *VOICE FRAME    * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot + 1} VC{e.n}.{n} ERRS {errs} [STREAM ID {e.StreamId}]");
                        // Log.Logger.Debug($"PARTIAL AMBE {FneUtils.HexDump(ambePartial)}");
                        // Log.Logger.Debug($"SAMPLE BUFFER {FneUtils.HexDump(samples)}");

                        int pcmIdx = 0;
                        byte[] pcm = new byte[samples.Length * 2];
                        for (int smpIdx = 0; smpIdx < samples.Length; smpIdx++)
                        {
                            pcm[pcmIdx + 0] = (byte)(samples[smpIdx] & 0xFF);
                            pcm[pcmIdx + 1] = (byte)((samples[smpIdx] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        //Log.WriteLine($"PCM BYTE BUFFER {FneUtils.HexDump(pcm)}");
                        audioManager.AddTalkgroupStream(e.DstId.ToString(), pcm);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteError($"Audio Decode Exception: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        public void KeyResponseReceived(KeyResponseEvent e)
        {
            //Log.WriteLine($"Message ID: {e.KmmKey.MessageId}");
            //Log.WriteLine($"Decrypt Info Format: {e.KmmKey.DecryptInfoFmt}");
            //Log.WriteLine($"Algorithm ID: {e.KmmKey.AlgId}");
            //Log.WriteLine($"Key ID: {e.KmmKey.KeyId}");
            //Log.WriteLine($"Keyset ID: {e.KmmKey.KeysetItem.KeysetId}");
            //Log.WriteLine($"Keyset Alg ID: {e.KmmKey.KeysetItem.AlgId}");
            //Log.WriteLine($"Keyset Key Length: {e.KmmKey.KeysetItem.KeyLength}");
            //Log.WriteLine($"Number of Keys: {e.KmmKey.KeysetItem.Keys.Count}");

            foreach (var key in e.KmmKey.KeysetItem.Keys)
            {
                //Log.WriteLine($"  Key Format: {key.KeyFormat}");
                //Log.WriteLine($"  SLN: {key.Sln}");
                //Log.WriteLine($"  Key ID: {key.KeyId}");
                //Log.WriteLine($"  Key Data: {BitConverter.ToString(key.GetKey())}");

                Dispatcher.Invoke(() =>
                {
                    foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                    {
                        if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                            continue;

                        Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                        PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                        ushort keyId = cpgChannel.GetKeyId();
                        byte algoId = cpgChannel.GetAlgoId();
                        KeysetItem receivedKey = e.KmmKey.KeysetItem;

                        if (keyId != 0 && algoId != 0 && keyId == key.KeyId && algoId == receivedKey.AlgId)
                            channel.Crypter.SetKey(key.KeyId, receivedKey.AlgId, key.GetKey());
                    }
                });
            }
        }

        /// <summary>
        /// Event handler used to process incoming DMR data.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="pktTime"></param>
        public void DMRDataReceived(DMRDataReceivedEvent e, DateTime pktTime)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    if (cpgChannel.GetChannelMode() != Codeplug.ChannelMode.DMR)
                        continue;

                    PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled || channel.Name == PLAYBACKCHNAME)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    if (!systemStatuses.ContainsKey(cpgChannel.Name + e.Slot))
                        systemStatuses[cpgChannel.Name + e.Slot] = new SlotStatus();

                    if (channel.Decoder == null)
                        channel.Decoder = new MBEDecoder(MBE_MODE.DMR_AMBE);

                    byte[] data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];
                    Buffer.BlockCopy(e.Data, 20, data, 0, FneSystemBase.DMR_FRAME_LENGTH_BYTES);
                    byte bits = e.Data[15];

                    // is this a new call stream?
                    if (e.StreamId != systemStatuses[cpgChannel.Name + e.Slot].RxStreamId)
                    {
                        channel.IsReceiving = true;
                        systemStatuses[cpgChannel.Name + e.Slot].RxStart = pktTime;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL START     * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} Slot {e.Slot} [STREAM ID {e.StreamId}]");

                        // if we can, use the LC from the voice header as to keep all options intact
                        if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_LC_HEADER))
                        {
                            LC lc = FullLC.Decode(data, DMRDataType.VOICE_LC_HEADER);
                            systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC = lc;
                        }
                        else // if we don't have a voice header; don't wait to decode it, just make a dummy header
                            systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC = new LC()
                            {
                                SrcId = e.SrcId,
                                DstId = e.DstId
                            };

                        systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC = new PrivacyLC();
                        Log.WriteLine($"({system.Name}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_LC {FneUtils.HexDump(systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC.GetBytes())}");

                        callHistoryWindow.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId);
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, false); // TODO: Encrypted state

                        string alias = string.Empty;

                        try
                        {
                            alias = AliasTools.GetAliasByRid(system.RidAlias, (int)e.SrcId);
                        }
                        catch (Exception) { }

                        if (string.IsNullOrEmpty(alias))
                            channel.LastSrcId = "Last ID: " + e.SrcId;
                        else
                            channel.LastSrcId = "Last: " + alias;

                        channel.Background = ChannelBox.GREEN_GRADIENT;
                    }

                    // if we can, use the PI LC from the PI voice header as to keep all options intact
                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_PI_HEADER))
                    {
                        PrivacyLC lc = FullLC.DecodePI(data);
                        systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC = lc;
                        //Log.WriteLine($"({SystemName}) DMRD: Traffic *CALL PI PARAMS  * PEER {e.PeerId} DST_ID {e.DstId} TS {e.Slot + 1} ALGID {lc.AlgId} KID {lc.KId} [STREAM ID {e.StreamId}]");
                        //Log.WriteLine($"({SystemName}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_PI_LC {FneUtils.HexDump(systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC.GetBytes())}");
                    }

                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.TERMINATOR_WITH_LC) && (systemStatuses[cpgChannel.Name + e.Slot].RxType != FrameType.TERMINATOR))
                    {
                        channel.IsReceiving = false;
                        TimeSpan callDuration = pktTime - systemStatuses[cpgChannel.Name + e.Slot].RxStart;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL END       * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} Slot {e.Slot} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        channel.Background = ChannelBox.BLUE_GRADIENT;
                        channel.VolumeMeterLevel = 0;
                        callHistoryWindow.ChannelUnkeyed(cpgChannel.Name, (int)e.SrcId);
                    }

                    if (e.FrameType == FrameType.VOICE_SYNC || e.FrameType == FrameType.VOICE)
                    {
                        byte[] ambe = new byte[FneSystemBase.DMR_AMBE_LENGTH_BYTES];
                        Buffer.BlockCopy(data, 0, ambe, 0, 14);
                        ambe[13] &= 0xF0;
                        ambe[13] |= (byte)(data[19] & 0x0F);
                        Buffer.BlockCopy(data, 20, ambe, 14, 13);
                        DMRDecodeAudioFrame(ambe, e, handler, channel);
                    }

                    systemStatuses[cpgChannel.Name + e.Slot].RxRFS = e.SrcId;
                    systemStatuses[cpgChannel.Name + e.Slot].RxType = e.FrameType;
                    systemStatuses[cpgChannel.Name + e.Slot].RxTGId = e.DstId;
                    systemStatuses[cpgChannel.Name + e.Slot].RxTime = pktTime;
                    systemStatuses[cpgChannel.Name + e.Slot].RxStreamId = e.StreamId;
                }
            });
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
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    if (cpgChannel.GetChannelMode() != Codeplug.ChannelMode.P25)
                        continue;

                    bool isEmergency = false;
                    bool encrypted = false;

                    PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled || channel.Name == PLAYBACKCHNAME)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    if (!systemStatuses.ContainsKey(cpgChannel.Name))
                        systemStatuses[cpgChannel.Name] = new SlotStatus();

                    if (channel.Decoder == null)
                        channel.Decoder = new MBEDecoder(MBE_MODE.IMBE_88BIT);

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

                            channel.Crypter.Prepare(channel.algId, channel.kId, channel.mi);

                            if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                                encrypted = true;
                        }
                    }

                    // is this a new call stream?
                    if (e.StreamId != slot.RxStreamId && ((e.DUID != P25DUID.TDU) && (e.DUID != P25DUID.TDULC)))
                    {
                        channel.IsReceiving = true;
                        slot.RxStart = pktTime;
                        Log.WriteLine($"({system.Name}) P25D: Traffic *CALL START     * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");

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
                            channel.LastSrcId = "Last ID: " + e.SrcId;
                        else
                            channel.LastSrcId = "Last: " + alias;

                        if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                        {
                            channel.Background = ChannelBox.ORANGE_GRADIENT;
                            Log.WriteLine($"({system.Name}) P25D: Traffic *CALL ENC PARMS * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                        }
                        else
                            channel.Background = ChannelBox.GREEN_GRADIENT;
                    }

                    // is the call over?
                    if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (slot.RxType != fnecore.FrameType.TERMINATOR))
                    {
                        channel.IsReceiving = false;
                        TimeSpan callDuration = pktTime - slot.RxStart;
                        Log.WriteLine($"({system.Name}) P25D: Traffic *CALL END       * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        channel.Background = ChannelBox.BLUE_GRADIENT;
                        channel.VolumeMeterLevel = 0;
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
                                P25DecodeAudioFrame(channel.netLDU2, e, handler, channel, isEmergency, P25DUID.LDU2);
                            }
                        }
                            break;
                    }

                    if (channel.mi != null)
                        channel.Crypter.Prepare(channel.algId, channel.kId, channel.mi);

                    slot.RxRFS = e.SrcId;
                    slot.RxType = e.FrameType;
                    slot.RxTGId = e.DstId;
                    slot.RxTime = pktTime;
                    slot.RxStreamId = e.StreamId;

                }
            });
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole