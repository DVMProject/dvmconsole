// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for AlertToneManagerWindow.xaml
    /// </summary>
    public partial class AlertToneManagerWindow : Window
    {
        public sealed class AlertToneManagerItem : INotifyPropertyChanged
        {
            private string displayName;
            private string filePath;
            private string tabName;
            public string Id { get; set; } = Guid.NewGuid().ToString("N");

            public string DisplayName
            {
                get => displayName;
                set
                {
                    if (displayName == value)
                        return;

                    displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }

            public string FilePath
            {
                get => filePath;
                set
                {
                    if (filePath == value)
                        return;

                    filePath = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
                }
            }

            public string TabName
            {
                get => tabName;
                set
                {
                    if (tabName == value)
                        return;

                    tabName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabName)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class AlertToneScheduleManagerItem : INotifyPropertyChanged
        {
            private string displayName;
            private string alertToneId;
            private string targetResourceKey;
            private bool enabled = true;
            private string mode = SettingsManager.ALERT_TONE_SCHEDULE_MODE_ONCE;
            private DateTime nextRunLocal = DateTime.Now.AddMinutes(5);
            private double repeatMinutes = 60.0;

            public string Id { get; set; } = Guid.NewGuid().ToString("N");

            public string DisplayName
            {
                get => displayName;
                set
                {
                    if (displayName == value)
                        return;

                    displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }

            public string AlertToneId
            {
                get => alertToneId;
                set
                {
                    string normalizedValue = value?.Trim() ?? string.Empty;
                    if (alertToneId == normalizedValue)
                        return;

                    alertToneId = normalizedValue;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlertToneId)));
                }
            }

            public string TargetResourceKey
            {
                get => targetResourceKey;
                set
                {
                    string normalizedValue = value?.Trim() ?? string.Empty;
                    if (targetResourceKey == normalizedValue)
                        return;

                    targetResourceKey = normalizedValue;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetResourceKey)));
                }
            }

            public bool Enabled
            {
                get => enabled;
                set
                {
                    if (enabled == value)
                        return;

                    enabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
                }
            }

            public string Mode
            {
                get => mode;
                set
                {
                    string normalizedValue = SettingsManager.NormalizeAlertToneScheduleMode(value);
                    if (mode == normalizedValue)
                        return;

                    mode = normalizedValue;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModeDisplay)));
                }
            }

            public string ModeDisplay
            {
                get => string.Equals(Mode, SettingsManager.ALERT_TONE_SCHEDULE_MODE_RECURRING, StringComparison.OrdinalIgnoreCase)
                    ? "Recurring"
                    : "Once";
                set => Mode = string.Equals(value, "Recurring", StringComparison.OrdinalIgnoreCase)
                    ? SettingsManager.ALERT_TONE_SCHEDULE_MODE_RECURRING
                    : SettingsManager.ALERT_TONE_SCHEDULE_MODE_ONCE;
            }

            public DateTime NextRunLocal
            {
                get => nextRunLocal;
                set
                {
                    DateTime normalizedValue = value == default ? DateTime.Now.AddMinutes(5) : DateTime.SpecifyKind(value, DateTimeKind.Local);
                    if (nextRunLocal == normalizedValue)
                        return;

                    nextRunLocal = normalizedValue;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NextRunLocal)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NextRunDisplay)));
                }
            }

            public string NextRunDisplay
            {
                get => NextRunLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
                set
                {
                    if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
                        NextRunLocal = parsed;
                }
            }

            public double RepeatMinutes
            {
                get => repeatMinutes;
                set
                {
                    double normalizedValue = Math.Max(SettingsManager.ALERT_TONE_SCHEDULE_MIN_REPEAT_MINUTES, value);
                    if (Math.Abs(repeatMinutes - normalizedValue) < 0.0001)
                        return;

                    repeatMinutes = normalizedValue;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RepeatMinutes)));
                }
            }

            public DateTime? LastRunUtc { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class AlertToneTargetItem
        {
            public string Key { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

        public ObservableCollection<AlertToneManagerItem> AlertTones { get; }
        public ObservableCollection<AlertToneScheduleManagerItem> Schedules { get; }
        public List<string> AvailableTabs { get; }
        public List<AlertToneTargetItem> TargetResources { get; }
        public List<string> ScheduleModes { get; } = new List<string> { "Once", "Recurring" };
        private readonly Action<IReadOnlyList<AlertToneManagerItem>, IReadOnlyList<AlertToneScheduleManagerItem>> saveCallback;

        public AlertToneManagerWindow(
            IEnumerable<AlertToneManagerItem> alertTones,
            IEnumerable<string> availableTabs,
            IEnumerable<SettingsManager.AlertToneScheduleConfig> schedules,
            IEnumerable<AlertToneTargetItem> targetResources,
            Action<IReadOnlyList<AlertToneManagerItem>, IReadOnlyList<AlertToneScheduleManagerItem>> saveCallback)
        {
            InitializeComponent();
            this.saveCallback = saveCallback;

            AvailableTabs = (availableTabs ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (AvailableTabs.Count == 0)
                AvailableTabs.Add("Tab 1");

            AlertTones = new ObservableCollection<AlertToneManagerItem>(
                (alertTones ?? Enumerable.Empty<AlertToneManagerItem>())
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(item => new AlertToneManagerItem
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                    DisplayName = item.DisplayName,
                    FilePath = item.FilePath,
                    TabName = string.IsNullOrWhiteSpace(item.TabName) ? AvailableTabs[0] : item.TabName
                }));

            TargetResources = (targetResources ?? Enumerable.Empty<AlertToneTargetItem>())
                .Where(target => target != null && !string.IsNullOrWhiteSpace(target.Key))
                .OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Schedules = new ObservableCollection<AlertToneScheduleManagerItem>(
                (schedules ?? Enumerable.Empty<SettingsManager.AlertToneScheduleConfig>())
                .OrderBy(schedule => schedule.NextRunLocal)
                .ThenBy(schedule => schedule.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(CreateScheduleManagerItem));

            AlertTones.CollectionChanged += AlertTones_CollectionChanged;
            foreach (AlertToneManagerItem item in AlertTones)
                item.PropertyChanged += AlertToneItem_PropertyChanged;

            Schedules.CollectionChanged += Schedules_CollectionChanged;
            foreach (AlertToneScheduleManagerItem schedule in Schedules)
                schedule.PropertyChanged += AlertToneItem_PropertyChanged;

            DataContext = this;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select Alert Tone(s)",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            AlertToneManagerItem lastAddedItem = null;
            int addedCount = 0;
            HashSet<string> existingPaths = new HashSet<string>(
                AlertTones
                    .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                    .Select(item => item.FilePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (string alertFilePath in openFileDialog.FileNames.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!existingPaths.Add(alertFilePath))
                    continue;

                AlertToneManagerItem item = new AlertToneManagerItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DisplayName = Path.GetFileNameWithoutExtension(alertFilePath),
                    FilePath = alertFilePath,
                    TabName = AvailableTabs[0]
                };

                AlertTones.Add(item);
                lastAddedItem = item;
                addedCount++;
            }

            if (lastAddedItem != null)
            {
                AlertToneGrid.SelectedItem = lastAddedItem;
                AlertToneGrid.ScrollIntoView(lastAddedItem);
            }

            if (addedCount > 1)
            {
                StatusTextBlock.Text = $"Added {addedCount} alert tones.";
                StatusTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (AlertToneGrid.SelectedItem is not AlertToneManagerItem item)
                return;

            HideStatus();

            MessageBoxResult result = MessageBox.Show(
                $"Delete alert tone '{item.DisplayName}'?",
                "Delete Alert Tone",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            AlertTones.Remove(item);
        }

        private void AddSchedule_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            if (AlertTones.Count == 0)
            {
                MessageBox.Show("Add a custom alert tone before scheduling an announcement.", "Timed Announcements", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (TargetResources.Count == 0)
            {
                MessageBox.Show("Load a codeplug with at least one TX-capable resource before scheduling an announcement.", "Timed Announcements", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AlertToneManagerItem selectedTone = AlertToneGrid.SelectedItem as AlertToneManagerItem ?? AlertTones.First();
            AlertToneTargetItem firstTarget = TargetResources.First();
            AlertToneScheduleManagerItem item = new AlertToneScheduleManagerItem
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = $"{selectedTone.DisplayName} announcement",
                AlertToneId = selectedTone.Id,
                TargetResourceKey = firstTarget.Key,
                Enabled = true,
                Mode = SettingsManager.ALERT_TONE_SCHEDULE_MODE_ONCE,
                NextRunLocal = RoundToNextMinute(DateTime.Now.AddMinutes(5)),
                RepeatMinutes = 60.0
            };

            Schedules.Add(item);
            ScheduleGrid.SelectedItem = item;
            ScheduleGrid.ScrollIntoView(item);
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleGrid.SelectedItem is not AlertToneScheduleManagerItem item)
                return;

            HideStatus();

            MessageBoxResult result = MessageBox.Show(
                $"Delete timed announcement '{item.DisplayName}'?",
                "Delete Timed Announcement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            Schedules.Remove(item);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not AlertToneManagerItem item)
                return;

            HideStatus();

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select Alert Tone"
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            item.FilePath = openFileDialog.FileName;
            if (string.IsNullOrWhiteSpace(item.DisplayName))
                item.DisplayName = Path.GetFileNameWithoutExtension(item.FilePath);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();

            List<AlertToneManagerItem> sanitizedItems = AlertTones
                .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                .Select(item => new AlertToneManagerItem
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                        ? Path.GetFileNameWithoutExtension(item.FilePath)
                        : item.DisplayName.Trim(),
                    FilePath = item.FilePath.Trim(),
                    TabName = string.IsNullOrWhiteSpace(item.TabName) ? AvailableTabs[0] : item.TabName
                })
                .ToList();

            HashSet<string> validAlertToneIds = new HashSet<string>(
                sanitizedItems.Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> validTargetKeys = new HashSet<string>(
                TargetResources.Select(target => target.Key),
                StringComparer.OrdinalIgnoreCase);

            List<AlertToneScheduleManagerItem> sanitizedSchedules = Schedules
                .Where(schedule => schedule != null &&
                                   validAlertToneIds.Contains(schedule.AlertToneId) &&
                                   validTargetKeys.Contains(schedule.TargetResourceKey))
                .Select(schedule => new AlertToneScheduleManagerItem
                {
                    Id = string.IsNullOrWhiteSpace(schedule.Id) ? Guid.NewGuid().ToString("N") : schedule.Id,
                    DisplayName = string.IsNullOrWhiteSpace(schedule.DisplayName) ? "Timed Announcement" : schedule.DisplayName.Trim(),
                    AlertToneId = schedule.AlertToneId,
                    TargetResourceKey = schedule.TargetResourceKey,
                    Enabled = schedule.Enabled,
                    Mode = schedule.Mode,
                    NextRunLocal = schedule.NextRunLocal,
                    RepeatMinutes = schedule.RepeatMinutes,
                    LastRunUtc = schedule.LastRunUtc
                })
                .ToList();

            saveCallback?.Invoke(sanitizedItems, sanitizedSchedules);

            StatusTextBlock.Text = "Changes saved.";
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AlertTones_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AlertToneManagerItem item in e.OldItems.OfType<AlertToneManagerItem>())
                    item.PropertyChanged -= AlertToneItem_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (AlertToneManagerItem item in e.NewItems.OfType<AlertToneManagerItem>())
                    item.PropertyChanged += AlertToneItem_PropertyChanged;
            }
        }

        private void Schedules_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AlertToneScheduleManagerItem item in e.OldItems.OfType<AlertToneScheduleManagerItem>())
                    item.PropertyChanged -= AlertToneItem_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (AlertToneScheduleManagerItem item in e.NewItems.OfType<AlertToneScheduleManagerItem>())
                    item.PropertyChanged += AlertToneItem_PropertyChanged;
            }

            HideStatus();
        }

        private static AlertToneScheduleManagerItem CreateScheduleManagerItem(SettingsManager.AlertToneScheduleConfig config)
        {
            return new AlertToneScheduleManagerItem
            {
                Id = string.IsNullOrWhiteSpace(config?.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                DisplayName = string.IsNullOrWhiteSpace(config?.DisplayName) ? "Timed Announcement" : config.DisplayName,
                AlertToneId = config?.AlertToneId ?? string.Empty,
                TargetResourceKey = config?.TargetResourceKey ?? string.Empty,
                Enabled = config?.Enabled ?? true,
                Mode = config?.Mode ?? SettingsManager.ALERT_TONE_SCHEDULE_MODE_ONCE,
                NextRunLocal = config?.NextRunLocal ?? RoundToNextMinute(DateTime.Now.AddMinutes(5)),
                RepeatMinutes = config?.RepeatMinutes ?? 60.0,
                LastRunUtc = config?.LastRunUtc
            };
        }

        private void CommitGridEdits()
        {
            AlertToneGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            AlertToneGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ScheduleGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ScheduleGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private static DateTime RoundToNextMinute(DateTime value)
        {
            DateTime withoutSeconds = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Local);
            return withoutSeconds < value ? withoutSeconds.AddMinutes(1) : withoutSeconds;
        }

        private void AlertToneItem_PropertyChanged(object sender, PropertyChangedEventArgs e) => HideStatus();

        private void HideStatus()
        {
            StatusTextBlock.Text = string.Empty;
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}
