// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace DVMConsole
{
    public partial class CallHistoryWindow : Window
    {
        public CallHistoryViewModel ViewModel { get; set; }

        public CallHistoryWindow()
        {
            InitializeComponent();
            ViewModel = new CallHistoryViewModel();
            DataContext = ViewModel;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

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
    }

    public class CallHistoryViewModel
    {
        public ObservableCollection<CallEntry> CallHistory { get; set; }

        public CallHistoryViewModel()
        {
            CallHistory = new ObservableCollection<CallEntry>();
        }
    }

    public class CallEntry : DependencyObject
    {
        public string Channel { get; set; }
        public int SrcId { get; set; }
        public int DstId { get; set; }

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register(nameof(BackgroundColor), typeof(Brush), typeof(CallEntry), new PropertyMetadata(Brushes.Transparent));

        public Brush BackgroundColor
        {
            get { return (Brush)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }
    }
}
