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
*
*/

using System.ComponentModel;
using System.IO;
using System.Reflection;
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
        public const int MAX_CALL_HISTORY = 10;

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
        private bool pageState;
        private bool holdState;
        private string lastSrcId = "0";
        private double volume = 1.0;
        private bool isSelected;

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
        /// Event action that occurs when a property changes on this control.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /*
        ** Properties
        */

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
        /// Last Packet Time
        /// </summary>
        public DateTime LastPktTime = DateTime.Now;

        /// <summary>
        /// Flag indicating whether or not this channel is receiving.
        /// </summary>
        public bool IsReceiving { get; set; } = false;
        /// <summary>
        /// Flag indicating whether or not this channel is receiving encrypted.
        /// </summary>
        public bool IsReceivingEncrypted { get; set; } = false;

        /// <summary>
        /// Flag indicating whether or not the console is transmitting with encryption.
        /// </summary>
        public bool IsTxEncrypted { get; set; } = false;

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
        /// Current volume for this channel.
        /// </summary>
        public double Volume
        {
            get => volume;
            set
            {
                if (volume != value)
                {
                    volume = value;
                    OnPropertyChanged(nameof(Volume));
                    audioManager.SetTalkgroupVolume(DstId, (float)value);
                }
            }
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

            callHistoryWindow = new CallHistoryWindow(SettingsManager.Instance, MAX_CALL_HISTORY);
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
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);

            // if the assembly executing directory contains the external DVSI USB-3000 interface DLL
            // setup the external vocoder code
            if (File.Exists(Path.Combine(new string[] { Path.GetDirectoryName(path), "AMBE.DLL" })))
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
            PttButton.IsEnabled = true;
            PageSelectButton.IsEnabled = true;
            ChannelMarkerBtn.IsEnabled = true;
            ChannelCallHistoryBtn.IsEnabled = true;

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
            PttButton.IsEnabled = false;
            PttButton.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdatePTTColor()
        {
            if (PttState)
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
        /// 
        /// </summary>
        private void UpdateBackground()
        {
            if (SystemName == MainWindow.PLAYBACKSYS || ChannelName == MainWindow.PLAYBACKCHNAME || DstId == MainWindow.PLAYBACKTG)
            {
                ControlBorder.Background = IsSelected ? RED_GRADIENT : DARK_GRAY_GRADIENT;
                SetVolumeMeterBg(ControlBorder.Background);
                return;
            }

            ControlBorder.Background = IsSelected ? BLUE_GRADIENT : DARK_GRAY_GRADIENT;
            if (IsSelected)
                if (IsPrimary)
                    ControlBorder.BorderBrush = BORDER_GREEN.BorderBrush;
                else
                    ControlBorder.BorderBrush = BORDER_DEFAULT.BorderBrush;
            else
                ControlBorder.BorderBrush = BORDER_DEFAULT.BorderBrush;

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
        public void AddCall(string channel, int srcId, int dstId, string timestamp)
        {
            callHistoryWindow.AddCall(channel, srcId, dstId, timestamp);
        }

        /** WPF Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
                    // Shortcut return, do not run the rest of this method
                    return;
                }
            }
            
            
            IsSelected = !IsSelected;
            ControlBorder.Background = IsSelected ? BLUE_GRADIENT : DARK_GRAY_GRADIENT;

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
            if (!IsSelected)
                return;

            if (IsTxEncrypted && !Crypter.HasKey())
            {
                MessageBox.Show($"{ChannelName} {ERR_NO_LOADED_ENC_KEY}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PttState = false;
                return;
            }

            PttState = pttState;
            PTTButtonClicked?.Invoke(null, this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void PttButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelected)
                return;

            if (IsTxEncrypted && !Crypter.HasKey())
            {
                MessageBox.Show($"{ChannelName} {ERR_NO_LOADED_ENC_KEY}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PttState = false;
                return;
            }

            PttState = !PttState;
            PTTButtonClicked?.Invoke(sender, this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async void PttButton_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
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
            if (!IsSelected)
                return;

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
                PttState = !PttState;
                PTTButtonClicked?.Invoke(sender, this);
            }
            else
            {
                // Normal mode: set PttState to true and invoke pressed event
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
            if (pttToggleMode)
                return;
            if (!IsSelected)
                return;

            PTTButtonReleased?.Invoke(sender, this);
            PttState = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PageSelectButton_Click(object sender, RoutedEventArgs e)
        {
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
            Volume = e.NewValue;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelMarkerBtn_Click(object sender, RoutedEventArgs e)
        {
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
            if (!IsSelected || PttState) 
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
            if (!IsSelected || PttState) 
                return;

            ((Button)sender).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDDDDDD"));
        }
    } // public partial class ChannelBox : UserControl, INotifyPropertyChanged
} // namespace dvmconsole.Controls
