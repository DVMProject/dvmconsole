// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using fnecore;
using fnecore.DMR;
using fnecore.P25;

namespace dvmconsole.Controls
{
    /// <summary>
    /// Interaction logic for ChannelBox.xaml.
    /// </summary>
    public partial class ChannelBox : UserControl, INotifyPropertyChanged
    {
        public enum ResourceCardSize
        {
            Small,
            Normal,
            Large
        }

        public const double SMALL_CARD_WIDTH = 154;
        public const double SMALL_CARD_HEIGHT = 68;
        public const double NORMAL_CARD_WIDTH = 264;
        public const double NORMAL_CARD_HEIGHT = 110;
        public const double LARGE_CARD_WIDTH = 380;
        public const double LARGE_CARD_HEIGHT = 158;

        public const int DEFAULT_PTT_RELEASE_TAIL_MS = 500;
        private const double DEFAULT_VOLUME = 1.0;
        private const double VOLUME_STEP = 0.1;
        private const double VOLUME_MARKER_TRACK_PADDING = 4.0;
        private const double VOLUME_METER_VISIBLE_THRESHOLD = 0.01;
        private static readonly Brush SELECTABLE_ENCRYPTION_ON_BRUSH = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107"));
        private static readonly Brush SELECTABLE_ENCRYPTION_OFF_BRUSH = Brushes.LightGray;

        public readonly static Border BORDER_DEFAULT;
        public readonly static Border BORDER_GREEN;

        public readonly static LinearGradientBrush GRAY_GRADIENT;

        public readonly static LinearGradientBrush DARK_GRAY_GRADIENT;      // Delected/Disconnected Color
        public readonly static LinearGradientBrush BLUE_GRADIENT;           // Selected Channel Color
        public readonly static LinearGradientBrush RED_GRADIENT;            // Playback Selected Color
        public readonly static LinearGradientBrush GREEN_GRADIENT;          // Clear Rx Color
        public readonly static LinearGradientBrush ORANGE_GRADIENT;         // Encrypted Rx Color

        private const string ERR_NO_LOADED_ENC_KEY = "does not have a loaded encryption key";

        private readonly SelectedChannelsManager selectedChannelsManager;
        private readonly AudioManager audioManager;

        private bool pttState;
        private bool patchForwardingTxState;
        private bool pageState;
        private bool holdState;
        private bool isTxEncrypted;
        private string lastSrcId = "0";
        private double volume = DEFAULT_VOLUME;
        private bool updatingVolumeSlider;
        private bool isRxOnly;
        private bool isEncryptionSelectable;
        private bool isSelected;
        private bool forceHidePttButton;
        private ResourceCardSize cardSize = ResourceCardSize.Normal;

        private bool isMultiSelectMember = false;
        private bool isPatchGroupActive = false;
        private string indicatorIconSource = "/dvmconsole;component/Assets/patch_edit_off.png";
        private Visibility indicatorIconVisibility = Visibility.Collapsed;
        private string indicatorIconToolTip = "Member of one or more patch groups";
        private Visibility tarIndicatorVisibility = Visibility.Collapsed;
        private string tarIndicatorToolTip = "TAR recording enabled for this channel";
        private Visibility selectableEncryptionVisibility = Visibility.Collapsed;
        private string selectableEncryptionToolTip = "Selectable encryption";
        private Brush selectableEncryptionForeground = Brushes.LightGray;

        public FlashingBackgroundManager flashingBackgroundManager;

        public byte[] netLDU1 = new byte[9 * 25];
        public byte[] netLDU2 = new byte[9 * 25];

        public ushort pktSeq = 0;                               // RTP packet sequence

        public int p25N = 0;
        public int p25SeqNo = 0;
        public int p25Errs = 0;

        public byte dmrN = 0;
        public int dmrSeqNo = 0;

        public int ambeCount = 0;
        public byte[] ambeBuffer = new byte[FneSystemBase.DMR_AMBE_LENGTH_BYTES];
        public EmbeddedData embeddedData = new EmbeddedData();

        public byte[] mi = new byte[P25Defines.P25_MI_LENGTH];     // Message Indicator
        public byte algId = 0;                                     // Algorithm ID
        public ushort kId = 0;                                     // Key ID

        public List<byte[]> chunkedPCM = new List<byte[]>();

        public bool ExternalVocoderEnabled = false;
        public AmbeVocoder ExtFullRateVocoder = null;
        public AmbeVocoder ExtHalfRateVocoder = null;
        public MBEEncoder Encoder = null;
        public MBEDecoder Decoder = null;

        public MBEToneDetector ToneDetector = new MBEToneDetector();

        public P25Crypto Crypter = new P25Crypto();

        private bool pttToggleMode = false;
        private bool suppressSelectionToggle = false;
        private CancellationTokenSource pendingPttReleaseCts;

        private bool isPrimary = false;

        private CallHistoryWindow callHistoryWindow;

        private static int ChannelIdx = 0;

        /*
        ** Events
        */

        /// <summary>
        /// Event action that handles the PTT button being clicked.
        /// </summary>
        public event EventHandler<ChannelBox> PTTButtonClicked;
        /// <summary>
        /// Event action that handles the PTT button being pressed.
        /// </summary>
        public event EventHandler<ChannelBox> PTTButtonPressed;
        /// <summary>
        /// Event action that handles the PTT button being released.
        /// </summary>
        public event EventHandler<ChannelBox> PTTButtonReleased;
        /// <summary>
        /// Event action that handles the page button being clicked.
        /// </summary>
        public event EventHandler<ChannelBox> PageButtonClicked;
        /// <summary>
        /// Event action that handles the hold channel button being clicked.
        /// </summary>
        public event EventHandler<ChannelBox> HoldChannelButtonClicked;
        /// <summary>
        /// Event action that handles selectable encryption being toggled.
        /// </summary>
        public event EventHandler<ChannelBox> SelectableEncryptionClicked;
        /// <summary>
        /// Event action that occurs when a property changes on this control.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /*
        ** Properties
        */

        /// <summary>
        /// Optional callback used to approve a new outbound PTT start before UI state is latched.
        /// </summary>
        public Func<ChannelBox, bool> CanStartPtt { get; set; }

        /// <summary>
        /// Fixed, codeplug-defined resource card size.
        /// </summary>
        public ResourceCardSize CardSize
        {
            get => cardSize;
            set
            {
                if (cardSize == value)
                    return;

                cardSize = value;
                ApplyCardSizeLayout();
            }
        }

        /// <summary>
        /// Flag indicating whether this resource should only receive traffic.
        /// </summary>
        public bool IsRxOnly
        {
            get => isRxOnly;
            set
            {
                isRxOnly = value;
                ApplyRxOnlyVisualState();
            }
        }

        /// <summary>
        /// Private internal reference ID for this channel.
        /// </summary>
        public int InternalID { get; private set; }

        /// <summary>
        /// Textual name of channel.
        /// </summary>
        public string ChannelName { get; set; }
        /// <summary>
        /// Textual mode of the channel.
        /// </summary>
        public string ChannelMode { get; set; }
        /// <summary>
        /// Textual name of system channel belongs to.
        /// </summary>
        public string SystemName { get; set; }
        /// <summary>
        /// Destination ID.
        /// </summary>
        public string DstId { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="Brush"/> that fills the area between the bounds of the control border.
        /// </summary>
        public new Brush Background
        {
            get => ControlBorder.Background;
            set
            {
                ControlBorder.Background = value;
                SetVolumeMeterBg(value);
            }
        }

        /// <summary>
        /// Configured idle background for this channel when selected but not active.
        /// Defaults to the standard blue gradient.
        /// </summary>
        public Brush ConfiguredIdleBackground { get; set; } = BLUE_GRADIENT;

        /// <summary>
        /// Last Packet Time
        /// </summary>
        public DateTime LastPktTime = DateTime.Now;

        private bool isReceiving = false;
        private bool isReceivingEncrypted = false;

        /// <summary>
        /// Flag indicating whether or not this channel is receiving.
        /// </summary>
        public bool IsReceiving
        {
            get => isReceiving;
            set
            {
                isReceiving = value;
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating whether or not this channel is receiving encrypted.
        /// </summary>
        public bool IsReceivingEncrypted
        {
            get => isReceivingEncrypted;
            set
            {
                isReceivingEncrypted = value;
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating whether or not the console is transmitting with encryption.
        /// </summary>
        public bool IsTxEncrypted
        {
            get => isTxEncrypted;
            set
            {
                if (isTxEncrypted == value)
                    return;

                isTxEncrypted = value;
                UpdatePTTColor();
                UpdateSelectableEncryptionIndicator();
                OnPropertyChanged(nameof(IsTxEncrypted));
            }
        }

        /// <summary>
        /// Flag indicating this channel can toggle secure TX on/off from the card.
        /// </summary>
        public bool IsEncryptionSelectable
        {
            get => isEncryptionSelectable;
            set
            {
                if (isEncryptionSelectable == value)
                    return;

                isEncryptionSelectable = value;
                UpdateSelectableEncryptionIndicator();
                OnPropertyChanged(nameof(IsEncryptionSelectable));
            }
        }

        /// <summary>
        /// Last Source ID received.
        /// </summary>
        public string LastSrcId
        {
            get => lastSrcId;
            set
            {
                if (lastSrcId != value)
                {
                    lastSrcId = value;
                    OnPropertyChanged(nameof(LastSrcId));
                }
            }
        }

        /// <summary>
        /// Flag indicating the current PTT state of this channel.
        /// </summary>
        public bool PttState
        {
            get => pttState;
            set
            {
                pttState = value;
                UpdatePTTColor();
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating programmatic transmit activity (patch forwarding/patch PTT) used for UI indication only.
        /// This does not grant microphone transmit in the main loop.
        /// </summary>
        public bool PatchForwardingTxState
        {
            get => patchForwardingTxState;
            set
            {
                patchForwardingTxState = value;
                UpdatePTTColor();
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating the current page state of this channel.
        /// </summary>
        public bool PageState
        {
            get => pageState;
            set
            {
                pageState = value;
                UpdatePageColor();
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating the hold state of this channel.
        /// </summary>
        public bool HoldState
        {
            get => holdState;
            set
            {
                holdState = value;
                UpdateHoldColor();
                Dispatcher.Invoke(() => UpdateBackground());
            }
        }

        /// <summary>
        /// Flag indicating the channel is in toggle PTT or regular PTT.
        /// </summary>
        public bool PTTToggleMode
        {
            get => pttToggleMode;
            set => pttToggleMode = value;
        }

        /// <summary>
        /// Tail hold duration used before the transmit path is actually de-keyed.
        /// </summary>
        public int PttReleaseTailMs { get; set; } = DEFAULT_PTT_RELEASE_TAIL_MS;

        /// <summary>
        /// 
        /// </summary>
        public string VoiceChannel { get; set; }

        /// <summary>
        /// Flag indicating whether or not this channel is selected.
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                Dispatcher.Invoke(() =>
                {
                    if (!isSelected)
                        DisableControls();
                    else
                        EnableControls();
                    UpdateBackground();
                });
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsPrimary
        {
            get => isPrimary;
            set
            {
                isPrimary = value;
                Dispatcher.Invoke(() =>
                {
                    UpdateBackground();
                });
            }
        }

        /// <summary>
        /// Flag to suppress selection toggling during drag workflow.
        /// </summary>
        public bool SuppressSelectionToggle
        {
            get => suppressSelectionToggle;
            set => suppressSelectionToggle = value;
        }

        /// <summary>
        /// Flag indicating whether this resource belongs to at least one patch group.
        /// </summary>
        public bool IsPatchGroupMember { get; private set; }

        /// <summary>
        /// Flag indicating whether this resource belongs to at least one enabled patch group.
        /// </summary>
        public bool IsPatchGroupActive
        {
            get => isPatchGroupActive;
            private set
            {
                if (isPatchGroupActive != value)
                {
                    isPatchGroupActive = value;
                    OnPropertyChanged(nameof(IsPatchGroupActive));
                }
            }
        }

        /// <summary>
        /// Flag indicating whether this resource belongs to the current multi-select group.
        /// </summary>
        public bool IsMultiSelectMember
        {
            get => isMultiSelectMember;
            private set
            {
                if (isMultiSelectMember != value)
                {
                    isMultiSelectMember = value;
                    OnPropertyChanged(nameof(IsMultiSelectMember));
                }
            }
        }

        /// <summary>
        /// Source path for the top-right indicator icon.
        /// </summary>
        public string IndicatorIconSource
        {
            get => indicatorIconSource;
            private set
            {
                if (indicatorIconSource != value)
                {
                    indicatorIconSource = value;
                    OnPropertyChanged(nameof(IndicatorIconSource));
                }
            }
        }

        /// <summary>
        /// Visibility for the top-right indicator icon.
        /// </summary>
        public Visibility IndicatorIconVisibility
        {
            get => indicatorIconVisibility;
            private set
            {
                if (indicatorIconVisibility != value)
                {
                    indicatorIconVisibility = value;
                    OnPropertyChanged(nameof(IndicatorIconVisibility));
                }
            }
        }

        /// <summary>
        /// Tooltip text for the top-right indicator icon.
        /// </summary>
        public string IndicatorIconToolTip
        {
            get => indicatorIconToolTip;
            private set
            {
                if (indicatorIconToolTip != value)
                {
                    indicatorIconToolTip = value;
                    OnPropertyChanged(nameof(IndicatorIconToolTip));
                }
            }
        }

        /// <summary>
        /// Visibility for the TAR recording badge.
        /// </summary>
        public Visibility TarIndicatorVisibility
        {
            get => tarIndicatorVisibility;
            private set
            {
                if (tarIndicatorVisibility != value)
                {
                    tarIndicatorVisibility = value;
                    OnPropertyChanged(nameof(TarIndicatorVisibility));
                }
            }
        }

        /// <summary>
        /// Tooltip for the TAR recording badge.
        /// </summary>
        public string TarIndicatorToolTip
        {
            get => tarIndicatorToolTip;
            private set
            {
                if (tarIndicatorToolTip != value)
                {
                    tarIndicatorToolTip = value;
                    OnPropertyChanged(nameof(TarIndicatorToolTip));
                }
            }
        }

        /// <summary>
        /// Visibility for the selectable encryption badge.
        /// </summary>
        public Visibility SelectableEncryptionVisibility
        {
            get => selectableEncryptionVisibility;
            private set
            {
                if (selectableEncryptionVisibility != value)
                {
                    selectableEncryptionVisibility = value;
                    OnPropertyChanged(nameof(SelectableEncryptionVisibility));
                }
            }
        }

        /// <summary>
        /// Tooltip for the selectable encryption badge.
        /// </summary>
        public string SelectableEncryptionToolTip
        {
            get => selectableEncryptionToolTip;
            private set
            {
                if (selectableEncryptionToolTip != value)
                {
                    selectableEncryptionToolTip = value;
                    OnPropertyChanged(nameof(SelectableEncryptionToolTip));
                }
            }
        }

        /// <summary>
        /// Foreground color for the selectable encryption badge.
        /// </summary>
        public Brush SelectableEncryptionForeground
        {
            get => selectableEncryptionForeground;
            private set
            {
                if (selectableEncryptionForeground != value)
                {
                    selectableEncryptionForeground = value;
                    OnPropertyChanged(nameof(SelectableEncryptionForeground));
                }
            }
        }
        /// <summary>
        /// Current volume for this channel.
        /// </summary>
        public double Volume
        {
            get => volume;
            set
            {
                double steppedValue = NormalizeVolume(value);
                if (Math.Abs(volume - steppedValue) > 0.0001)
                {
                    volume = steppedValue;
                    OnPropertyChanged(nameof(Volume));
                    audioManager.SetTalkgroupVolume(DstId, (float)steppedValue);
                    SettingsManager.Instance?.UpdateChannelVolume(ChannelName, steppedValue);
                }
            }
        }

        /// <summary>
        /// Initializes the channel volume without immediately touching the audio stream.
        /// </summary>
        public void SetInitialVolume(double value)
        {
            volume = NormalizeVolume(value);
            OnPropertyChanged(nameof(Volume));
        }

        /// <summary>
        /// Applies the current channel volume to the backing audio stream/provider.
        /// </summary>
        public void ApplyCurrentVolume()
        {
            audioManager.SetTalkgroupVolume(DstId, (float)volume);
        }

        /// <summary>
        /// 
        /// </summary>
        public double VolumeMeterLevel
        { 
            set
            {
                OnPropertyChanged(nameof(VolumeMeterLevel));
                Dispatcher.Invoke(() =>
                {
                    VolumeMeter.ViewModel.Level = value;
                    VolumeMeter.Visibility = CardSize != ResourceCardSize.Small && VolumeMeter.ViewModel.Level > VOLUME_METER_VISIBLE_THRESHOLD
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint TxStreamId { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public uint PeerId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public uint RxStreamId { get; set; }

        /*
        ** Methods
        */

        /// <summary>
        /// Static initialize for the <see cref="ChannelBox" class. />
        /// </summary>
        static ChannelBox()
        {
            ChannelIdx = 0;

            GRAY_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            GRAY_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0F0F0F0"), 0.485));
            GRAY_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0C2C2C2"), 0.517));

            DARK_GRAY_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            DARK_GRAY_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0979797"), 0.535));
            DARK_GRAY_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0686767"), 0.567));

            BLUE_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            BLUE_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0150189"), 0.535));
            BLUE_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F00B004B"), 0.567));

            RED_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            RED_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0FF0000"), 0.535));
            RED_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0C60000"), 0.567));

            GREEN_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            GREEN_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F000AF00"), 0.535));
            GREEN_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0008E00"), 0.567));

            ORANGE_GRADIENT = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            ORANGE_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0FFAF00"), 0.535));
            ORANGE_GRADIENT.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F0C68700"), 0.567));
            BORDER_DEFAULT = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            
            BORDER_GREEN = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.Green),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelBox"/> class.
        /// </summary>
        /// <param name="selectedChannelsManager"></param>
        /// <param name="audioManager"></param>
        /// <param name="channelName"></param>
        /// <param name="systemName"></param>
        /// <param name="dstId"></param>
        /// <param name="pttToggleMode"></param>
        public ChannelBox(SelectedChannelsManager selectedChannelsManager, AudioManager audioManager, string channelName, string systemName, string dstId, bool pttToggleMode = false)
        {
            InitializeComponent();

            this.InternalID = ChannelIdx;
            ChannelIdx++;

            DataContext = this;

            this.selectedChannelsManager = selectedChannelsManager;
            this.audioManager = audioManager;

            flashingBackgroundManager = new FlashingBackgroundManager(this);

            callHistoryWindow = new CallHistoryWindow(SettingsManager.Instance, CallHistoryWindow.MAX_CALL_HISTORY);
            callHistoryWindow.Title = $"Call History - Channel: {channelName}";

            ChannelName = channelName;
            ChannelMode = "P25";
            DstId = dstId;
            SystemName = $"System: {systemName}";
            LastSrcId = $"Last ID: {LastSrcId}";

            algId = P25Defines.P25_ALGO_UNENCRYPT;
            kId = 0;
            FneUtils.Memset(mi, 0, P25Defines.P25_MI_LENGTH);

            VolumeMeter.ViewModel = new VuMeterViewModel();
            VolumeMeter.ViewModel.Level = 0;
            VolumeMeter.Visibility = Visibility.Collapsed;

            UpdateBackground();

            MouseLeftButtonDown += ChannelBox_MouseLeftButtonDown;

            PttButton.PreviewMouseLeftButtonDown += PttButton_MouseLeftButtonDown;
            PttButton.PreviewMouseLeftButtonUp += PttButton_MouseLeftButtonUp;
            PttButton.MouseRightButtonDown += PttButton_MouseRightButtonDown;

            this.pttToggleMode = pttToggleMode;

            PttButton.Background = GRAY_GRADIENT;
            PageSelectButton.Background = GRAY_GRADIENT;
            ChannelMarkerBtn.Background = GRAY_GRADIENT;
            ChannelCallHistoryBtn.Background = GRAY_GRADIENT;

            ApplyCardSizeLayout();
            DisableControls();

            if (SystemName == MainWindow.PLAYBACKSYS || ChannelName == MainWindow.PLAYBACKCHNAME || DstId == MainWindow.PLAYBACKTG)
            {
                PttButton.IsEnabled = false;

                PageSelectButton.IsEnabled = false;
                PageSelectButton.Visibility = Visibility.Hidden;
                ChannelMarkerBtn.IsEnabled = false;
                ChannelMarkerBtn.Visibility = Visibility.Hidden;
                ChannelCallHistoryBtn.IsEnabled = false;
                ChannelCallHistoryBtn.Visibility = Visibility.Hidden;
            }

            // initialize external AMBE vocoder
            string path = Assembly.GetExecutingAssembly().Location;

            // if the assembly executing directory contains the external DVSI USB-3000 interface DLL
            // setup the external vocoder code
            if (File.Exists(Path.Combine(Path.GetDirectoryName(path), "AMBE.DLL")))
            {
                ExternalVocoderEnabled = true;
                ExtFullRateVocoder = new AmbeVocoder();
                ExtHalfRateVocoder = new AmbeVocoder(false);
            }
        }

        /// <summary>
        /// Helper to enable controls.
        /// </summary>
        private void EnableControls()
        {
            PttButton.IsEnabled = !IsRxOnly && !forceHidePttButton;
            PageSelectButton.IsEnabled = !IsRxOnly && CardSize != ResourceCardSize.Small;
            ChannelMarkerBtn.IsEnabled = !IsRxOnly && CardSize != ResourceCardSize.Small;
            ChannelCallHistoryBtn.IsEnabled = CardSize != ResourceCardSize.Small;

            VolumeSlider.IsEnabled = true;
        }

        /// <summary>
        /// Helper to disable controls.
        /// </summary>
        private void DisableControls()
        {
            PttButton.IsEnabled = false;
            PageSelectButton.IsEnabled = false;
            ChannelMarkerBtn.IsEnabled = false;
            ChannelCallHistoryBtn.IsEnabled = false;

            VolumeSlider.IsEnabled = false;
        }

        /// <summary>
        /// Helper to hide the PTT button.
        /// </summary>
        public void HidePTTButton()
        {
            forceHidePttButton = true;
            PttButton.IsEnabled = false;
            PttButton.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Parses a codeplug card_size value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ResourceCardSize ParseCardSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ResourceCardSize.Normal;

            return value.Trim().ToLowerInvariant() switch
            {
                "small" => ResourceCardSize.Small,
                "large" => ResourceCardSize.Large,
                _ => ResourceCardSize.Normal
            };
        }

        /// <summary>
        /// Applies fixed size modes without changing channel behavior.
        /// </summary>
        private void ApplyCardSizeLayout()
        {
            if (CardGrid == null)
                return;

            switch (CardSize)
            {
                case ResourceCardSize.Small:
                    ApplySmallCardLayout();
                    break;
                case ResourceCardSize.Large:
                    ApplyLargeCardLayout();
                    break;
                default:
                    ApplyNormalCardLayout();
                    break;
            }

            ApplyRxOnlyVisualState();
            if (CardSize == ResourceCardSize.Small)
                VolumeMeter.Visibility = Visibility.Collapsed;
            UpdateDefaultVolumeMarker();
            UpdateBackground();
        }

        private void ApplyNormalCardLayout()
        {
            Width = NORMAL_CARD_WIDTH;
            Height = NORMAL_CARD_HEIGHT;
            LeftColumn.Width = new GridLength(58);
            RightColumn.Width = new GridLength(30);
            TopSpacerRow.Height = new GridLength(2, GridUnitType.Star);
            InfoRow.Height = new GridLength(51, GridUnitType.Star);
            ControlsRow.Height = new GridLength(32.25);
            BottomSpacerRow.Height = new GridLength(7.75);

            VolumeMeter.Width = 260;
            VolumeMeter.Height = 10;
            VolumeMeter.Margin = new Thickness(0, -58, 0, 0);

            InfoPanel.Width = 147;
            InfoPanel.Margin = new Thickness(4, 5, 0, 4);
            SetInfoFontSizes(12, 10);
            SystemTextBlock.Visibility = Visibility.Visible;

            PatchMemberIcon.Width = 28;
            PatchMemberIcon.Height = 28;
            PatchMemberIcon.Margin = new Thickness(0, 6, 6, 0);

            PttButton.Width = 42;
            PttButton.Height = 42;
            PttButton.Margin = new Thickness(0, 9, 0, 0);
            SetButtonImageSize(PttButton, 39, 40);

            Grid.SetColumn(VolumeSliderBackground, 0);
            Grid.SetColumnSpan(VolumeSliderBackground, 2);
            VolumeSliderBackground.HorizontalAlignment = HorizontalAlignment.Left;
            VolumeSliderBackground.Width = 116;
            VolumeSliderBackground.Height = 40;
            VolumeSliderBackground.Margin = new Thickness(6, -4, 0, 0);
            Grid.SetColumn(VolumeSlider, 0);
            Grid.SetColumnSpan(VolumeSlider, 2);
            VolumeSlider.Margin = new Thickness(12, 0, 112, 0);
            VolumeSlider.Height = 21;

            BottomButtonsPanel.Margin = new Thickness(70, -8, -2, 0);
            SetButtonSize(PageSelectButton, 38, 40, 34, 38);
            SetButtonSize(ChannelMarkerBtn, 38, 40, 34, 38);
            SetButtonSize(ChannelCallHistoryBtn, 38, 40, 30, 38);
            ChannelMarkerBtn.Margin = new Thickness(5, 0, 0, 0);
            ChannelCallHistoryBtn.Margin = new Thickness(5, 0, 0, 0);
        }

        private void ApplySmallCardLayout()
        {
            Width = SMALL_CARD_WIDTH;
            Height = SMALL_CARD_HEIGHT;
            LeftColumn.Width = new GridLength(42);
            RightColumn.Width = new GridLength(24);
            TopSpacerRow.Height = new GridLength(0);
            InfoRow.Height = new GridLength(44);
            ControlsRow.Height = new GridLength(20);
            BottomSpacerRow.Height = new GridLength(0);

            VolumeMeter.Width = 150;
            VolumeMeter.Height = 4;
            VolumeMeter.Margin = new Thickness(0, -21, 0, 0);

            InfoPanel.Width = 86;
            InfoPanel.Margin = new Thickness(2, 4, 0, 0);
            SetInfoFontSizes(10, 8);
            SystemTextBlock.Visibility = Visibility.Collapsed;

            PatchMemberIcon.Width = 18;
            PatchMemberIcon.Height = 18;
            PatchMemberIcon.Margin = new Thickness(0, 4, 4, 0);

            PttButton.Width = 32;
            PttButton.Height = 32;
            PttButton.Margin = new Thickness(0, 8, 0, 0);
            SetButtonImageSize(PttButton, 30, 30);

            Grid.SetColumn(VolumeSliderBackground, 0);
            Grid.SetColumnSpan(VolumeSliderBackground, 3);
            VolumeSliderBackground.Visibility = Visibility.Collapsed;
            VolumeSliderBackground.Width = 0;
            VolumeSliderBackground.Height = 0;
            VolumeSliderBackground.Margin = new Thickness(0);
            Grid.SetColumn(VolumeSlider, 0);
            Grid.SetColumnSpan(VolumeSlider, 3);
            VolumeSlider.Margin = new Thickness(18, -4, 18, 0);
            VolumeSlider.Height = 16;
            BottomButtonsPanel.Visibility = Visibility.Collapsed;
        }

        private void ApplyLargeCardLayout()
        {
            Width = LARGE_CARD_WIDTH;
            Height = LARGE_CARD_HEIGHT;
            LeftColumn.Width = new GridLength(78);
            RightColumn.Width = new GridLength(44);
            TopSpacerRow.Height = new GridLength(3, GridUnitType.Star);
            InfoRow.Height = new GridLength(70, GridUnitType.Star);
            ControlsRow.Height = new GridLength(46);
            BottomSpacerRow.Height = new GridLength(8);

            VolumeMeter.Width = 372;
            VolumeMeter.Height = 12;
            VolumeMeter.Margin = new Thickness(0, -88, 0, 0);

            InfoPanel.Width = 210;
            InfoPanel.Margin = new Thickness(8, 8, 0, 6);
            SetInfoFontSizes(16, 12);
            SystemTextBlock.Visibility = Visibility.Visible;

            PatchMemberIcon.Width = 38;
            PatchMemberIcon.Height = 38;
            PatchMemberIcon.Margin = new Thickness(0, 9, 8, 0);

            PttButton.Width = 60;
            PttButton.Height = 60;
            PttButton.Margin = new Thickness(0, 12, 0, 0);
            SetButtonImageSize(PttButton, 56, 58);

            Grid.SetColumn(VolumeSliderBackground, 0);
            Grid.SetColumnSpan(VolumeSliderBackground, 2);
            VolumeSliderBackground.HorizontalAlignment = HorizontalAlignment.Left;
            VolumeSliderBackground.Width = 166;
            VolumeSliderBackground.Height = 54;
            VolumeSliderBackground.Margin = new Thickness(10, -6, 0, 0);
            Grid.SetColumn(VolumeSlider, 0);
            Grid.SetColumnSpan(VolumeSlider, 2);
            VolumeSlider.Margin = new Thickness(20, 0, 158, 0);
            VolumeSlider.Height = 30;

            BottomButtonsPanel.Margin = new Thickness(104, -10, -2, 0);
            SetButtonSize(PageSelectButton, 58, 54, 50, 52);
            SetButtonSize(ChannelMarkerBtn, 58, 54, 50, 52);
            SetButtonSize(ChannelCallHistoryBtn, 58, 54, 46, 52);
            ChannelMarkerBtn.Margin = new Thickness(6, 0, 0, 0);
            ChannelCallHistoryBtn.Margin = new Thickness(6, 0, 0, 0);
        }

        private void SetInfoFontSizes(double channelFontSize, double metadataFontSize)
        {
            ChannelTextBlock.FontSize = channelFontSize;
            LastSrcIdTextBlock.FontSize = metadataFontSize;
            SystemTextBlock.FontSize = metadataFontSize;
            TarTextBlock.FontSize = metadataFontSize;
            SelectableEncryptionTextBlock.FontSize = metadataFontSize;
        }

        private static void SetButtonSize(Button button, double width, double height, double imageWidth, double imageHeight)
        {
            button.Width = width;
            button.Height = height;
            SetButtonImageSize(button, imageWidth, imageHeight);
        }

        private static void SetButtonImageSize(Button button, double imageWidth, double imageHeight)
        {
            if (button.Content is Image image)
            {
                image.Width = imageWidth;
                image.Height = imageHeight;
            }
        }

        /// <summary>
        /// Applies receive-only UI restrictions to transmit-only controls.
        /// </summary>
        private void ApplyRxOnlyVisualState()
        {
            if (PttButton == null || PageSelectButton == null || ChannelMarkerBtn == null)
                return;

            bool smallCard = CardSize == ResourceCardSize.Small;
            Visibility pttVisibility = (IsRxOnly || forceHidePttButton) ? Visibility.Collapsed : Visibility.Visible;
            Visibility txControlVisibility = (IsRxOnly || smallCard) ? Visibility.Collapsed : Visibility.Visible;
            Visibility secondaryButtonVisibility = smallCard ? Visibility.Collapsed : Visibility.Visible;

            PttButton.Visibility = pttVisibility;
            PageSelectButton.Visibility = txControlVisibility;
            ChannelMarkerBtn.Visibility = txControlVisibility;
            ChannelCallHistoryBtn.Visibility = secondaryButtonVisibility;
            BottomButtonsPanel.Visibility = secondaryButtonVisibility;
            VolumeSliderBackground.Visibility = smallCard ? Visibility.Collapsed : Visibility.Visible;
            VolumeSlider.Visibility = Visibility.Visible;

            if (IsRxOnly)
            {
                CancelPendingPttRelease();
                PttState = false;
                PageState = false;
                HoldState = false;
                PttButton.IsEnabled = false;
                PageSelectButton.IsEnabled = false;
                ChannelMarkerBtn.IsEnabled = false;
                PttButton.ToolTip = "RX-only resource";
                PageSelectButton.ToolTip = "RX-only resource";
                ChannelMarkerBtn.ToolTip = "RX-only resource";
                return;
            }

            PttButton.ToolTip = "Push To Talk";
            PageSelectButton.ToolTip = "Select for Alert Tone";
            ChannelMarkerBtn.ToolTip = "Transmit Channel Marker";
            if (IsSelected)
                EnableControls();
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdatePTTColor()
        {
            if (PttState || PatchForwardingTxState)
            {
                if (IsTxEncrypted)
                    PttButton.Background = ORANGE_GRADIENT;
                else
                    PttButton.Background = RED_GRADIENT;
            }
            else
                PttButton.Background = GRAY_GRADIENT;
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdatePageColor()
        {
            if (PageState)
                PageSelectButton.Background = ORANGE_GRADIENT;
            else
                PageSelectButton.Background = GRAY_GRADIENT;
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdateHoldColor()
        {
            if (HoldState)
                ChannelMarkerBtn.Background = ORANGE_GRADIENT;
            else
                ChannelMarkerBtn.Background = GRAY_GRADIENT;
        }

        /// <summary>
        /// Updates the selectable encryption badge state.
        /// </summary>
        private void UpdateSelectableEncryptionIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                SelectableEncryptionVisibility = IsEncryptionSelectable ? Visibility.Visible : Visibility.Collapsed;
                SelectableEncryptionForeground = IsTxEncrypted
                    ? SELECTABLE_ENCRYPTION_ON_BRUSH
                    : SELECTABLE_ENCRYPTION_OFF_BRUSH;
                SelectableEncryptionToolTip = IsTxEncrypted
                    ? "Selectable encryption: encrypted TX. Click to transmit clear."
                    : "Selectable encryption: clear TX. Click to transmit encrypted.";
            });
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdateBackground()
        {
            if (SystemName == MainWindow.PLAYBACKSYS || ChannelName == MainWindow.PLAYBACKCHNAME || DstId == MainWindow.PLAYBACKTG)
            {
                SetCardBackground(IsSelected ? RED_GRADIENT : DARK_GRAY_GRADIENT);
                return;
            }

            if (IsReceivingEncrypted)
            {
                SetCardBackground(ORANGE_GRADIENT);
            }
            else if (IsReceiving)
            {
                SetCardBackground(GREEN_GRADIENT);
            }
            else
            {
                SetCardBackground(IsSelected ? ConfiguredIdleBackground : DARK_GRAY_GRADIENT);
            }

            if (IsSelected)
            {
                if (IsPrimary)
                    ControlBorder.BorderBrush = BORDER_GREEN.BorderBrush;
                else
                    ControlBorder.BorderBrush = BORDER_DEFAULT.BorderBrush;
            }
            else
            {
                ControlBorder.BorderBrush = BORDER_DEFAULT.BorderBrush;
            }

        }

        /// <summary>
        /// Applies the card background, flattening gradients on small cards where the split is visually cramped.
        /// </summary>
        /// <param name="background"></param>
        private void SetCardBackground(Brush background)
        {
            if (CardSize == ResourceCardSize.Small && background is LinearGradientBrush gradient && gradient.GradientStops.Count > 0)
                ControlBorder.Background = new SolidColorBrush(gradient.GradientStops[0].Color);
            else
                ControlBorder.Background = background;

            SetVolumeMeterBg(ControlBorder.Background);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bg"></param>
        private void SetVolumeMeterBg(Brush bg)
        {
            if (bg is LinearGradientBrush)
            {
                LinearGradientBrush gradient = bg as LinearGradientBrush;
                VolumeMeter.SetBackground(new SolidColorBrush(gradient.GradientStops[0].Color));
            }
            else
                VolumeMeter.SetBackground(bg);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyName"></param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="srcId"></param>
        /// <param name="dstId"></param>
        public void AddCall(string channel, int srcId, int dstId, string ridAlias, string timestamp)
        {
            callHistoryWindow.AddCall(channel, srcId, dstId, ridAlias, timestamp);
        }

        /// <summary>
        /// Clears active highlighting from this channel's local call history.
        /// </summary>
        public void ClearCallHistoryActivity()
        {
            callHistoryWindow.ClearChannelActivity(ChannelName);
        }

        /** WPF Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SuppressSelectionToggle)
                return;

            ProcessSelectionClick(e);
        }

        /// <summary>
        /// Toggles selectable secure TX without changing channel selection.
        /// </summary>
        private void SelectableEncryptionTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (!IsEncryptionSelectable)
                return;

            if (PttState || PatchForwardingTxState)
            {
                MessageBox.Show("Encryption selection cannot be changed while transmitting.", "Selectable Encryption", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsTxEncrypted = !IsTxEncrypted;
            SelectableEncryptionClicked?.Invoke(this, this);
        }

        /// <summary>
        /// Applies channel selection/primary toggle behavior for a resource click.
        /// </summary>
        /// <param name="e"></param>
        public void ProcessSelectionClick(MouseButtonEventArgs e)
        {
            if (IsSelected)
            {
                // Check if either CTRL key is down, if so toggle PRIMARY state instead of deselecting
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    // If current channel is PRIMARY, clear it, otherwise set current to primary
                    if (selectedChannelsManager.PrimaryChannel == this)
                    {
                        selectedChannelsManager.ClearPrimaryChannel();
                        IsPrimary = false;
                    }
                    else
                    {
                        selectedChannelsManager.SetPrimaryChannel(this);
                        IsPrimary = true;
                    }

                    return;
                }
            }

            IsSelected = !IsSelected;

            if (IsSelected)
                selectedChannelsManager.AddSelectedChannel(this);
            else
                selectedChannelsManager.RemoveSelectedChannel(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pttState"></param>
        public void TriggerPTTState(bool pttState)
        {
            if (IsRxOnly)
                return;
            if (!IsSelected)
                return;

            if (pttState && CanStartPtt != null && !CanStartPtt(this))
                return;

            if (pttState)
            {
                CancelPendingPttRelease();
                if (PttState)
                    return;
            }

            if (IsTxEncrypted && !Crypter.HasKey())
            {
                MessageBox.Show($"{ChannelName} {ERR_NO_LOADED_ENC_KEY}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PttState = false;
                return;
            }

            if (pttState)
            {
                PttState = true;
                PTTButtonClicked?.Invoke(null, this);
                return;
            }

            if (!PttState)
                return;

            BeginPttReleaseTail(() =>
            {
                PttState = false;
                PTTButtonClicked?.Invoke(null, this);
            });
        }

        /// <summary>
        /// Updates the top-right indicator icon based on current state.
        /// Multi-select takes priority over patch membership.
        /// </summary>
        private void UpdateIndicatorIcon()
        {
            Dispatcher.Invoke(() =>
            {
                if (IsMultiSelectMember)
                {
                    IndicatorIconSource = "/dvmconsole;component/Assets/msel_inactive.png";
                    IndicatorIconToolTip = "Member of the current multi-select group";
                    IndicatorIconVisibility = Visibility.Visible;
                }
                else if (IsPatchGroupMember)
                {
                    IndicatorIconSource = IsPatchGroupActive
                        ? "/dvmconsole;component/Assets/patch_edit_on.png"
                        : "/dvmconsole;component/Assets/patch_edit_off.png";
                    IndicatorIconToolTip = IsPatchGroupActive
                        ? "Member of one or more enabled patch groups"
                        : "Member of one or more patch groups";
                    IndicatorIconVisibility = Visibility.Visible;
                }
                else
                {
                    IndicatorIconVisibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Sets the patch membership indicator state for this resource.
        /// </summary>
        /// <param name="isMember"></param>
        public void SetPatchMembershipIndicator(bool isMember, bool isActive = false)
        {
            IsPatchGroupMember = isMember;
            IsPatchGroupActive = isMember && isActive;
            UpdateIndicatorIcon();
        }

        /// <summary>
        /// Sets the multi-select indicator state for this resource.
        /// </summary>
        /// <param name="isMember"></param>
        public void SetMultiSelectIndicator(bool isMember)
        {
            IsMultiSelectMember = isMember;
            UpdateIndicatorIcon();
        }

        /// <summary>
        /// Sets the TAR recording badge visibility for this resource.
        /// </summary>
        public void SetTarRecordingIndicator(bool isEnabled)
        {
            TarIndicatorVisibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            TarIndicatorToolTip = isEnabled
                ? "TAR recording enabled for this channel"
                : "TAR recording disabled for this channel";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void PttButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (!IsSelected)
                return;

            if (HasPendingPttRelease)
            {
                CancelPendingPttRelease();
                return;
            }

            bool nextState = !PttState;
            if (nextState && CanStartPtt != null && !CanStartPtt(this))
                return;

            if (IsTxEncrypted && !Crypter.HasKey())
            {
                MessageBox.Show($"{ChannelName} {ERR_NO_LOADED_ENC_KEY}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PttState = false;
                return;
            }

            if (nextState)
            {
                CancelPendingPttRelease();
                PttState = true;
                PTTButtonClicked?.Invoke(sender, this);
                return;
            }

            if (!PttState)
                return;

            BeginPttReleaseTail(() =>
            {
                PttState = false;
                PTTButtonClicked?.Invoke(sender, this);
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async void PttButton_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (pttToggleMode)
                return;

            if (PttState)
                await Task.Delay(500);

            PttButton_Click(sender, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async void PttButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (!IsSelected)
                return;

            if (HasPendingPttRelease)
            {
                CancelPendingPttRelease();
                return;
            }

            if (IsTxEncrypted && !Crypter.HasKey())
            {
                MessageBox.Show($"{ChannelName} {ERR_NO_LOADED_ENC_KEY}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PttState = false;
                return;
            }

            if (PttState)
                await Task.Delay(500);

            if (pttToggleMode)
            {
                // Toggle mode: toggle PttState and invoke clicked event
                bool nextState = !PttState;
                if (nextState && CanStartPtt != null && !CanStartPtt(this))
                    return;

                if (nextState)
                {
                    CancelPendingPttRelease();
                    PttState = true;
                    PTTButtonClicked?.Invoke(sender, this);
                }
                else
                {
                    BeginPttReleaseTail(() =>
                    {
                        PttState = false;
                        PTTButtonClicked?.Invoke(sender, this);
                    });
                }
            }
            else
            {
                // Normal mode: set PttState to true and invoke pressed event
                if (CanStartPtt != null && !CanStartPtt(this))
                    return;

                CancelPendingPttRelease();
                PTTButtonPressed?.Invoke(sender, this);
                PttState = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void PttButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (pttToggleMode)
                return;
            if (!IsSelected)
                return;
            if (!PttState)
                return;

            BeginPttReleaseTail(() =>
            {
                PTTButtonReleased?.Invoke(sender, this);
                PttState = false;
            });
        }

        private bool HasPendingPttRelease => pendingPttReleaseCts != null;

        private void CancelPendingPttRelease()
        {
            pendingPttReleaseCts?.Cancel();
            pendingPttReleaseCts = null;
        }

        private async void BeginPttReleaseTail(Action releaseAction)
        {
            CancelPendingPttRelease();

            CancellationTokenSource cts = new CancellationTokenSource();
            pendingPttReleaseCts = cts;

            try
            {
                if (PttReleaseTailMs > 0)
                    await Task.Delay(PttReleaseTailMs, cts.Token);

                if (cts.IsCancellationRequested || pendingPttReleaseCts != cts)
                    return;

                releaseAction?.Invoke();
            }
            catch (TaskCanceledException)
            {
                /* stub */
            }
            finally
            {
                if (pendingPttReleaseCts == cts)
                    pendingPttReleaseCts = null;

                cts.Dispose();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PageSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (!IsSelected) 
                return;

            PageState = !PageState;
            PageButtonClicked?.Invoke(sender, this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingVolumeSlider)
                return;

            double steppedValue = NormalizeVolume(e.NewValue);
            if (Math.Abs(steppedValue - e.NewValue) > 0.0001)
            {
                updatingVolumeSlider = true;
                VolumeSlider.Value = steppedValue;
                updatingVolumeSlider = false;
            }

            Volume = steppedValue;
        }

        private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustVolumeFromMouseWheel(e);
        }

        private void VolumeBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustVolumeFromMouseWheel(e);
        }

        private void AdjustVolumeFromMouseWheel(MouseWheelEventArgs e)
        {
            if (!VolumeSlider.IsEnabled)
                return;

            double direction = e.Delta > 0 ? VOLUME_STEP : -VOLUME_STEP;
            VolumeSlider.Value = NormalizeVolume(VolumeSlider.Value + direction);
            e.Handled = true;
        }

        private void VolumeSlider_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDefaultVolumeMarker();
        }

        private void VolumeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDefaultVolumeMarker();
        }

        private static double NormalizeVolume(double value)
        {
            double steppedValue = Math.Round(value / VOLUME_STEP) * VOLUME_STEP;
            return Math.Max(0.0, Math.Min(4.0, steppedValue));
        }

        private void UpdateDefaultVolumeMarker()
        {
            VolumeSlider.ApplyTemplate();

            if (VolumeSlider.Template.FindName("PART_DefaultVolumeMarker", VolumeSlider) is not FrameworkElement marker)
                return;

            double sliderWidth = VolumeSlider.ActualWidth;
            double range = VolumeSlider.Maximum - VolumeSlider.Minimum;
            if (sliderWidth <= 0 || range <= 0)
                return;

            double percent = (DEFAULT_VOLUME - VolumeSlider.Minimum) / range;
            double usableWidth = Math.Max(0, sliderWidth - (VOLUME_MARKER_TRACK_PADDING * 2));
            double markerWidth = marker.Width > 0 ? marker.Width : 2.0;
            double x = VOLUME_MARKER_TRACK_PADDING + (usableWidth * percent) - (markerWidth / 2.0);
            marker.Margin = new Thickness(Math.Max(0, x), 0, 0, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelMarkerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsRxOnly)
                return;
            if (!IsSelected) 
                return;

            HoldState = !HoldState;
            HoldChannelButtonClicked.Invoke(sender, this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelCallHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelected)
                return;

            callHistoryWindow.Show();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PttButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsSelected || PttState || PatchForwardingTxState)
                return;

            ((Button)sender).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3FA0FF"));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PttButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsSelected || PttState || PatchForwardingTxState)
                return;

            ((Button)sender).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDDDDDD"));
        }
    } // public partial class ChannelBox : UserControl, INotifyPropertyChanged
} // namespace dvmconsole.Controls
