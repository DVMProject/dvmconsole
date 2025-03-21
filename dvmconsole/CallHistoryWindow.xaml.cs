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
*
*/

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace dvmconsole
{
    /// <summary>
    /// Data structure representing a call entry.
    /// </summary>
    public class CallEntry : DependencyObject
    {
        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register(nameof(BackgroundColor), typeof(Brush), typeof(CallEntry), new PropertyMetadata(Brushes.Transparent));

        /*
        ** Properties
        */

        /// <summary>
        /// Textual name of channel call was received on.
        /// </summary>
        public string Channel { get; set; }
        /// <summary>
        /// Source ID.
        /// </summary>
        public int SrcId { get; set; }
        /// <summary>
        /// Destination ID.
        /// </summary>
        public int DstId { get; set; }

        /// <summary>
        /// Background color for call entry.
        /// </summary>
        public Brush BackgroundColor
        {
            get { return (Brush)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }
    } // public class CallEntry : DependencyObject

    /// <summary>
    /// Data view model representing the call history.
    /// </summary>
    public class CallHistoryViewModel
    {
        /*
        ** Properties
        */

        /// <summary>
        /// Collection of call history entries.
        /// </summary>
        public ObservableCollection<CallEntry> CallHistory { get; set; }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHistoryViewModel"/> class.
        /// </summary>
        public CallHistoryViewModel()
        {
            CallHistory = new ObservableCollection<CallEntry>();
        }
    } // public class CallHistoryViewModel

    /// <summary>
    /// Interaction logic for CallHistoryWindow.xaml.
    /// </summary>
    public partial class CallHistoryWindow : Window
    {
        /*
        ** Properties
        */

        /// <summary>
        /// Gets or sets the <see cref="CallHistoryViewModel"/> view model for the window.
        /// </summary>
        public CallHistoryViewModel ViewModel { get; set; }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHistoryWindow"/> class.
        /// </summary>
        public CallHistoryWindow()
        {
            InitializeComponent();
            ViewModel = new CallHistoryViewModel();
            DataContext = ViewModel;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="srcId"></param>
        /// <param name="dstId"></param>
        public void AddCall(string channel, int srcId, int dstId)
        {
            Dispatcher.Invoke(() =>
            {
                ViewModel.CallHistory.Insert(0, new CallEntry
                {
                    Channel = channel,
                    SrcId = srcId,
                    DstId = dstId,
                    BackgroundColor = Brushes.Transparent
                });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="srcId"></param>
        /// <param name="encrypted"></param>
        public void ChannelKeyed(string channel, int srcId, bool encrypted)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var entry in ViewModel.CallHistory.Where(c => c.Channel == channel && c.SrcId == srcId))
                {
                    if (!encrypted)
                        entry.BackgroundColor = Brushes.LightGreen;
                    else
                        entry.BackgroundColor = Brushes.Orange;
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="srcId"></param>
        public void ChannelUnkeyed(string channel, int srcId)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var entry in ViewModel.CallHistory.Where(c => c.Channel == channel && c.SrcId == srcId))
                {
                    entry.BackgroundColor = Brushes.Transparent;
                }
            });
        }
    } // public partial class CallHistoryWindow : Window
} // namespace dvmconsole
