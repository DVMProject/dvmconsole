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
*
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private sealed class ToolbarClockDisplayItem : INotifyPropertyChanged
        {
            private string timeText = string.Empty;

            public int UtcOffsetHours { get; init; }

            public string TimeZoneLabel { get; init; } = string.Empty;
            public Brush BackgroundBrush { get; init; }
            public Brush ForegroundBrush { get; init; }

            public string TimeText
            {
                get => timeText;
                set
                {
                    if (timeText == value)
                        return;

                    timeText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly ObservableCollection<ToolbarClockDisplayItem> toolbarClockItems = new ObservableCollection<ToolbarClockDisplayItem>();
        private DispatcherTimer toolbarClockTimer;

        public static string FormatUtcOffsetLabel(int offsetHours)
        {
            string sign = offsetHours >= 0 ? "+" : "-";
            return $"UTC{sign}{Math.Abs(offsetHours):00}";
        }

        private static string FormatToolbarClockTime(DateTimeOffset time, bool use24Hour, bool showSeconds)
        {
            if (use24Hour)
                return time.ToString(showSeconds ? "HH:mm:ss" : "HH:mm");

            return time.ToString(showSeconds ? "hh:mm:ss tt" : "hh:mm tt");
        }

        private void InitializeToolbarClocks()
        {
            ToolbarClockStrip.ItemsSource = toolbarClockItems;

            toolbarClockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            toolbarClockTimer.Tick += ToolbarClockTimer_Tick;

            RefreshToolbarClocks();
        }

        private void ShutdownToolbarClocks()
        {
            if (toolbarClockTimer == null)
                return;

            toolbarClockTimer.Stop();
            toolbarClockTimer.Tick -= ToolbarClockTimer_Tick;
        }

        private void ToolbarClockTimer_Tick(object sender, EventArgs e)
        {
            UpdateToolbarClockTimes();
        }

        private void UpdateToolbarClockTimes()
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            bool use24Hour = settingsManager.ClockUse24HourTime;
            bool showSeconds = settingsManager.ClockShowSeconds;

            foreach (ToolbarClockDisplayItem item in toolbarClockItems)
            {
                DateTimeOffset offsetTime = utcNow.ToOffset(TimeSpan.FromHours(item.UtcOffsetHours));
                item.TimeText = FormatToolbarClockTime(offsetTime, use24Hour, showSeconds);
            }
        }

        private void RefreshToolbarClocks()
        {
            toolbarClockItems.Clear();

            foreach (SettingsManager.ToolbarClockConfig config in settingsManager.GetToolbarClockConfigs())
            {
                if (!config.Enabled)
                    continue;

                toolbarClockItems.Add(new ToolbarClockDisplayItem
                {
                    UtcOffsetHours = config.UtcOffsetHours,
                    TimeZoneLabel = FormatUtcOffsetLabel(config.UtcOffsetHours),
                    BackgroundBrush = CreateFrozenBrush(config.ColorHex),
                    ForegroundBrush = Brushes.White
                });
            }

            ToolbarClockContainer.Visibility = toolbarClockItems.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateToolbarClockTimes();

            if (toolbarClockItems.Count > 0)
                toolbarClockTimer?.Start();
            else
                toolbarClockTimer?.Stop();
        }

        private void ClockManager_Click(object sender, RoutedEventArgs e)
        {
            ClockManagerWindow clockManagerWindow = new ClockManagerWindow(settingsManager, RefreshToolbarClocks)
            {
                Owner = this
            };

            clockManagerWindow.ShowDialog();
        }

        private static Brush CreateFrozenBrush(string colorHex)
        {
            string normalized = string.IsNullOrWhiteSpace(colorHex)
                ? SettingsManager.DEFAULT_TOOLBAR_CLOCK_COLOR
                : colorHex.Trim();

            SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromString(normalized);
            brush.Freeze();
            return brush;
        }
    }
}
