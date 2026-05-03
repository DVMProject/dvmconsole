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
using System.Windows.Input;
using System.Windows.Media;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for ClockManagerWindow.xaml.
    /// </summary>
    public partial class ClockManagerWindow : Window, INotifyPropertyChanged
    {
        public sealed class ClockManagerRow : INotifyPropertyChanged
        {
            private bool enabled;
            private int utcOffsetHours;
            private string colorHex = SettingsManager.DEFAULT_TOOLBAR_CLOCK_COLOR;

            public int SlotNumber { get; init; }

            public string SlotLabel => $"Clock {SlotNumber}";

            public bool Enabled
            {
                get => enabled;
                set
                {
                    if (enabled == value)
                        return;

                    enabled = value;
                    OnPropertyChanged();
                }
            }

            public int UtcOffsetHours
            {
                get => utcOffsetHours;
                set
                {
                    if (utcOffsetHours == value)
                        return;

                    utcOffsetHours = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TimeZoneLabel));
                }
            }

            public string ColorHex
            {
                get => colorHex;
                set
                {
                    string normalized = ClockColorOption.NormalizeColor(value);
                    if (string.Equals(colorHex, normalized, StringComparison.OrdinalIgnoreCase))
                        return;

                    colorHex = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                    OnPropertyChanged(nameof(ColorLabel));
                }
            }

            public string TimeZoneLabel => MainWindow.FormatUtcOffsetLabel(UtcOffsetHours);
            public Brush ColorBrush => ClockColorOption.CreateBrush(ColorHex);
            public string ColorLabel => ClockColorOption.GetLabel(ColorHex);

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public sealed class UtcOffsetOption
        {
            public int OffsetHours { get; init; }
            public string Label { get; init; } = string.Empty;
        }

        public sealed class ClockColorOption
        {
            public string Label { get; init; } = string.Empty;
            public string ColorHex { get; init; } = SettingsManager.DEFAULT_TOOLBAR_CLOCK_COLOR;
            public Brush ColorBrush => CreateBrush(ColorHex);

            public static string NormalizeColor(string colorHex)
            {
                if (string.IsNullOrWhiteSpace(colorHex))
                    return SettingsManager.DEFAULT_TOOLBAR_CLOCK_COLOR;

                string trimmed = colorHex.Trim().ToUpperInvariant();
                return ClockColorOptions.Any(option => string.Equals(option.ColorHex, trimmed, StringComparison.OrdinalIgnoreCase))
                    ? trimmed
                    : SettingsManager.DEFAULT_TOOLBAR_CLOCK_COLOR;
            }

            public static string GetLabel(string colorHex)
            {
                string normalized = NormalizeColor(colorHex);
                return ClockColorOptions.First(option => string.Equals(option.ColorHex, normalized, StringComparison.OrdinalIgnoreCase)).Label;
            }

            public static Brush CreateBrush(string colorHex)
            {
                SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromString(NormalizeColor(colorHex));
                brush.Freeze();
                return brush;
            }
        }

        private static readonly List<ClockColorOption> ClockColorOptions = new List<ClockColorOption>
        {
            new ClockColorOption { Label = "Neutral", ColorHex = "#3A3A3A" },
            new ClockColorOption { Label = "Blue", ColorHex = "#0D47A1" },
            new ClockColorOption { Label = "Green", ColorHex = "#1B5E20" },
            new ClockColorOption { Label = "Amber", ColorHex = "#B26A00" },
            new ClockColorOption { Label = "Red", ColorHex = "#8E2424" },
            new ClockColorOption { Label = "Purple", ColorHex = "#5E35B1" },
            new ClockColorOption { Label = "Teal", ColorHex = "#00695C" },
            new ClockColorOption { Label = "Slate", ColorHex = "#37474F" }
        };

        private readonly SettingsManager settingsManager;
        private readonly Action savedCallback;
        private bool use24HourTime;
        private bool showSeconds;

        public ObservableCollection<ClockManagerRow> ClockRows { get; } = new ObservableCollection<ClockManagerRow>();
        public List<UtcOffsetOption> AvailableTimeZones { get; } = Enumerable.Range(-12, 27)
            .Select(offset => new UtcOffsetOption
            {
                OffsetHours = offset,
                Label = MainWindow.FormatUtcOffsetLabel(offset)
            })
            .ToList();
        public List<ClockColorOption> AvailableColors => ClockColorOptions;

        public bool Use24HourTime
        {
            get => use24HourTime;
            set
            {
                if (use24HourTime == value)
                    return;

                use24HourTime = value;
                OnPropertyChanged();
            }
        }

        public bool ShowSeconds
        {
            get => showSeconds;
            set
            {
                if (showSeconds == value)
                    return;

                showSeconds = value;
                OnPropertyChanged();
            }
        }

        public ClockManagerWindow(
            SettingsManager settingsManager,
            Action savedCallback)
        {
            InitializeComponent();
            DataContext = this;

            this.settingsManager = settingsManager;
            this.savedCallback = savedCallback;
            Use24HourTime = settingsManager.ClockUse24HourTime;
            ShowSeconds = settingsManager.ClockShowSeconds;

            List<SettingsManager.ToolbarClockConfig> normalizedConfigs = settingsManager.GetToolbarClockConfigs()
                .Take(SettingsManager.MAX_TOOLBAR_CLOCKS)
                .ToList();

            while (normalizedConfigs.Count < SettingsManager.MAX_TOOLBAR_CLOCKS)
                normalizedConfigs.Add(new SettingsManager.ToolbarClockConfig());

            for (int i = 0; i < normalizedConfigs.Count; i++)
            {
                SettingsManager.ToolbarClockConfig config = normalizedConfigs[i] ?? new SettingsManager.ToolbarClockConfig();
                ClockRows.Add(new ClockManagerRow
                {
                    SlotNumber = i + 1,
                    Enabled = config.Enabled,
                    UtcOffsetHours = config.UtcOffsetHours,
                    ColorHex = config.ColorHex
                });
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();

            List<SettingsManager.ToolbarClockConfig> configs = ClockRows
                .Select(row => new SettingsManager.ToolbarClockConfig
                {
                    Enabled = row.Enabled,
                    UtcOffsetHours = row.UtcOffsetHours,
                    ColorHex = row.ColorHex
                })
                .ToList();

            settingsManager.SaveToolbarClockSettings(configs, Use24HourTime, ShowSeconds);
            savedCallback?.Invoke();

            string enabledSlots = string.Join(", ", ClockRows
                .Where(row => row.Enabled)
                .Select(row => row.SlotNumber));
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(enabledSlots)
                ? "Changes saved. No clocks enabled."
                : $"Changes saved. Enabled clocks: {enabledSlots}.";
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
