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
*
*/

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using fnecore.P25;

namespace dvmconsole.Controls
{
    /// <summary>
    /// 
    /// </summary>
    public partial class ChannelBox : UserControl, INotifyPropertyChanged
    {
        private readonly static Brush DESELECTED_COLOR = Brushes.Gray;
        private readonly static Brush SELECTED_COLOR = (Brush)new BrushConverter().ConvertFrom("#FF0B004B");
        private readonly static Brush PLYBK_SELECTED_COLOR = (Brush)new BrushConverter().ConvertFrom("#FFC90000");

        private readonly SelectedChannelsManager selectedChannelsManager;
        private readonly AudioManager audioManager;

        private bool pttState;
        private bool pageState;
        private bool holdState;
        private bool emergency;
        private string lastSrcId = "0";
        private double volume = 1.0;
        private bool isSelected;

        internal LinearGradientBrush grayGradient;
        internal LinearGradientBrush redGradient;
        internal LinearGradientBrush orangeGradient;

        public FlashingBackgroundManager flashingBackgroundManager;

        public byte[] netLDU1 = new byte[9 * 25];
        public byte[] netLDU2 = new byte[9 * 25];

        public int p25N { get; set; } = 0;
        public int p25SeqNo { get; set; } = 0;
        public int p25Errs { get; set; } = 0;

        public byte[] mi = new byte[P25Defines.P25_MI_LENGTH];     // Message Indicator
        public byte algId = 0;                                     // Algorithm ID
        public ushort kId = 0;                                     // Key ID

        public List<byte[]> chunkedPcm = new List<byte[]>();

#if WIN32
        public AmbeVocoder extFullRateVocoder;
        public AmbeVocoder extHalfRateVocoder;
#endif
        public MBEEncoder encoder;
        public MBEDecoder decoder;

        public MBEToneDetector toneDetector = new MBEToneDetector();

        public P25Crypto crypter = new P25Crypto();

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public string ChannelName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string SystemName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string DstId { get; set; }

        /*
        ** Events
        */

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<ChannelBox> PTTButtonClicked;
        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<ChannelBox> PageButtonClicked;
        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<ChannelBox> HoldChannelButtonClicked;
        /// <summary>
        /// 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 
        /// </summary>
        public bool IsReceiving { get; set; } = false;
        /// <summary>
        /// 
        /// </summary>
        public bool IsReceivingEncrypted { get; set; } = false;

        /// <summary>
        /// 
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
        /// 
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
        /// 
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
        /// 
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
        /// 
        /// </summary>
        public bool Emergency
        {
            get => emergency;
            set
            {
                emergency = value;

                Dispatcher.Invoke(() =>
                {
                    if (value)
                        flashingBackgroundManager.Start();
                    else
                        flashingBackgroundManager.Stop();
                });
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string VoiceChannel { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsEditMode { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                UpdateBackground();
            }
        }

        /// <summary>
        /// 
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
        public uint txStreamId { get; internal set; }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelBox"/> class.
        /// </summary>
        /// <param name="selectedChannelsManager"></param>
        /// <param name="audioManager"></param>
        /// <param name="channelName"></param>
        /// <param name="systemName"></param>
        /// <param name="dstId"></param>
        public ChannelBox(SelectedChannelsManager selectedChannelsManager, AudioManager audioManager, string channelName, string systemName, string dstId)
        {
            InitializeComponent();
            DataContext = this;
            this.selectedChannelsManager = selectedChannelsManager;
            this.audioManager = audioManager;
            flashingBackgroundManager = new FlashingBackgroundManager(this);
            ChannelName = channelName;
            DstId = dstId;
            SystemName = $"System: {systemName}";
            LastSrcId = $"Last ID: {LastSrcId}";
            UpdateBackground();
            MouseLeftButtonDown += ChannelBox_MouseLeftButtonDown;

            grayGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            grayGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFF0F0F0"), 0.485));
            grayGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFDCDCDC"), 0.517));

            redGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            redGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFFF0000"), 0.485));
            redGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFD50000"), 0.517));

            orangeGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };

            orangeGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFAF00"), 0.485));
            orangeGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFEEA400"), 0.517));

            PttButton.Background = grayGradient;
            PageSelectButton.Background = grayGradient;
            ChannelMarkerBtn.Background = grayGradient;

            if (SystemName == MainWindow.PLAYBACKSYS || ChannelName == MainWindow.PLAYBACKCHNAME || DstId == MainWindow.PLAYBACKTG)
            {
                PttButton.IsEnabled = false;
                PageSelectButton.IsEnabled = false;
                ChannelMarkerBtn.IsEnabled = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsEditMode) 
                return;

            IsSelected = !IsSelected;
            ControlBorder.Background = IsSelected ? SELECTED_COLOR : DESELECTED_COLOR;

            if (IsSelected)
                selectedChannelsManager.AddSelectedChannel(this);
            else
                selectedChannelsManager.RemoveSelectedChannel(this);
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdatePTTColor()
        {
            if (IsEditMode) 
                return;

            if (PttState)
                PttButton.Background = redGradient;
            else
                PttButton.Background = grayGradient;
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdatePageColor()
        {
            if (IsEditMode) 
                return;

            if (PageState)
                PageSelectButton.Background = orangeGradient;
            else
                PageSelectButton.Background = grayGradient;
        }

        private void UpdateHoldColor()
        {
            if (IsEditMode) 
                return;

            if (HoldState)
                ChannelMarkerBtn.Background = orangeGradient;
            else
                ChannelMarkerBtn.Background = grayGradient;
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdateBackground()
        {
            if (SystemName == MainWindow.PLAYBACKSYS || ChannelName == MainWindow.PLAYBACKCHNAME || DstId == MainWindow.PLAYBACKTG)
            {
                ControlBorder.Background = IsSelected ? PLYBK_SELECTED_COLOR : DESELECTED_COLOR;
                return;
            }

            ControlBorder.Background = IsSelected ? SELECTED_COLOR : DESELECTED_COLOR;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PTTButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSelected) 
                return;

            if (PttState)
                await Task.Delay(500);

            PttState = !PttState;

            PTTButtonClicked.Invoke(sender, this);
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
            PageButtonClicked.Invoke(sender, this);
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
        /// <param name="propertyName"></param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
