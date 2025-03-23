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
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using NAudio.Wave;
using NWaves.Signals;

using MaterialDesignThemes.Wpf;

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

        public const int PCM_SAMPLES_LENGTH = 320; // MBE_SAMPLES_LENGTH * 2

        public const int MAX_SYSTEM_NAME_LEN = 10;
        public const int MAX_CHANNEL_NAME_LEN = 21;

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

        private Brush btnGlobalPttDefaultBg;

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

            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// 
        /// </summary>
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

            channelsCanvas.Children.Clear();
            systemStatuses.Clear();

            fneSystemManager.ClearAll();

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

                        // clamp slot value
                        if (channel.Slot <= 0)
                            channel.Slot = 1;
                        if (channel.Slot > 2)
                            channel.Slot = 1;
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
                            offsetY += 116;
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
        /// Helper to reset channel states.
        /// </summary>
        /// <param name="e"></param>
        private void ResetChannel(ChannelBox e)
        {
            // reset values
            e.p25SeqNo = 0;
            e.p25N = 0;

            e.dmrSeqNo = 0;
            e.dmrN = 0;

            e.pktSeq = 0;

            e.TxStreamId = 0;
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
                    ChannelBox primaryChannel = selectedChannelsManager.PrimaryChannel;
                    List<ChannelBox> channelsToProcess = primaryChannel != null
                        ? new List<ChannelBox> { primaryChannel }
                        : selectedChannelsManager.GetSelectedChannels().ToList();

                    foreach (ChannelBox channel in channelsToProcess)
                    {

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

                        if (channel.PageState || (forHold && channel.HoldState) || primaryChannel != null)
                        {
                            byte[] pcmData;

                            Task.Run(async () =>
                            {
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

                                if (channel.TxStreamId != 0)
                                    Log.WriteWarning($"{channel.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                                channel.TxStreamId = fne.NewStreamId();
                                Log.WriteLine($"({system.Name}) {channel.ChannelMode.ToUpperInvariant()} Traffic *ALRT TONE      * TGID {channel.DstId} [STREAM ID {channel.TxStreamId}]");
                                channel.VolumeMeterLevel = 0;

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

                                ResetChannel(channel);

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
            // set the UI theme
            PaletteHelper paletteHelper = new PaletteHelper();
            Theme theme = paletteHelper.GetTheme();

            if (settingsManager.DarkMode)
                theme.SetBaseTheme(BaseTheme.Dark);
            else
                theme.SetBaseTheme(BaseTheme.Light);

            paletteHelper.SetTheme(theme);

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
                {
                    settingsManager.Maximized = true;
                    if (settingsManager.SnapCallHistoryToWindow)
                        menuSnapCallHistory.IsChecked = false;
                }

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
        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (settingsManager.SnapCallHistoryToWindow && callHistoryWindow.Visibility == Visibility.Visible && 
                WindowState != WindowState.Maximized)
            {
                callHistoryWindow.Left = Left + ActualWidth + 5;
                callHistoryWindow.Top = Top;
            }
        }

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

            if (settingsManager.SnapCallHistoryToWindow && callHistoryWindow.Visibility == Visibility.Visible && 
                WindowState != WindowState.Maximized)
            {
                callHistoryWindow.Height = ActualHeight;
                callHistoryWindow.Left = Left + ActualWidth + 5;
                callHistoryWindow.Top = Top;
            }

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
            menuSnapCallHistory.IsChecked = settingsManager.SnapCallHistoryToWindow;
            menuTogglePTTMode.IsChecked = settingsManager.TogglePTTMode;
            menuStayOnTop.IsChecked = settingsManager.KeepWindowOnTop;

            if (!string.IsNullOrEmpty(settingsManager.LastCodeplugPath) && File.Exists(settingsManager.LastCodeplugPath))
                LoadCodeplug(settingsManager.LastCodeplugPath);
            else
                GenerateChannelWidgets();

            // set background configuration
            menuDarkMode.IsChecked = settingsManager.DarkMode;
            UpdateBackground();

            btnGlobalPttDefaultBg = btnGlobalPtt.Background;

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
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_CALL_ALRT callAlert = new IOSP_CALL_ALRT(uint.Parse(pageWindow.DstId), uint.Parse(pageWindow.RadioSystem.Rid));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_CALL_ALRT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                callAlert.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
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
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.CHECK, uint.Parse(pageWindow.RadioSystem.Rid), uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
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
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.INHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
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
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.UNINHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
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
        private void ToggleSnapCallHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.SnapCallHistoryToWindow = !settingsManager.SnapCallHistoryToWindow;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleStayOnTop_Click(object sender, RoutedEventArgs e)
        {
            this.Topmost = !this.Topmost;

            settingsManager.KeepWindowOnTop = menuStayOnTop.IsChecked;
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
                ResetChannel(e);
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

                ResetChannel(e);
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
                settingsManager.UpdateAlertTonePosition(alertTone.AlertFilePath, x, y);

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

            ChannelBox primaryChannel = selectedChannelsManager.PrimaryChannel;

            if (primaryChannel != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (globalPttState)
                        btnGlobalPtt.Background = ChannelBox.RED_GRADIENT;
                    else
                        btnGlobalPtt.Background = btnGlobalPttDefaultBg;
                });

                primaryChannel.PttButton_Click(sender, e);
                return;
            }

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
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

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                channel.TxStreamId = fne.NewStreamId();
                if (globalPttState)
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = ChannelBox.RED_GRADIENT;
                        channel.PttState = true;
                    });

                    fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = btnGlobalPttDefaultBg;
                        channel.PttState = false;
                    });

                    fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false);
                }
            }
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
            if (callHistoryWindow.Visibility == Visibility.Visible)
                callHistoryWindow.Hide();
            else
            {
                callHistoryWindow.Show();

                if (settingsManager.SnapCallHistoryToWindow && WindowState != WindowState.Maximized)
                {
                    if (ActualHeight > callHistoryWindow.Height)
                        callHistoryWindow.Height = ActualHeight;

                    callHistoryWindow.Left = Left + ActualWidth + 5;
                    callHistoryWindow.Top = Top;
                }
            }
        }

        /** fnecore Hooks / Helpers */

        /// <summary>
        /// Handler for FNE key responses.
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

                        ushort keyId = cpgChannel.GetKeyId();
                        byte algoId = cpgChannel.GetAlgoId();
                        KeysetItem receivedKey = e.KmmKey.KeysetItem;

                        if (keyId != 0 && algoId != 0 && keyId == key.KeyId && algoId == receivedKey.AlgId)
                            channel.Crypter.SetKey(key.KeyId, receivedKey.AlgId, key.GetKey());
                    }
                });
            }
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole
