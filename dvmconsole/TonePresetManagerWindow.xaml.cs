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
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using dvmconsole.Controls;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for TonePresetManagerWindow.xaml
    /// </summary>
    public partial class TonePresetManagerWindow : Window, INotifyPropertyChanged
    {
        public sealed class TonePresetStepItem : INotifyPropertyChanged
        {
            private string kind = "Tone";
            private double frequencyHz = 1000;
            private double durationSeconds = 1;

            public string Kind
            {
                get => kind;
                set
                {
                    string normalizedKind = string.Equals(value, "Hold", StringComparison.OrdinalIgnoreCase) ? "Hold" : "Tone";
                    if (kind == normalizedKind)
                        return;

                    kind = normalizedKind;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kind)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrequencyDisplay)));
                }
            }

            public string FrequencyDisplay
            {
                get => IsHold ? "Hold" : FrequencyHz.ToString("F1");
                set
                {
                    if (IsHold)
                        return;

                    if (Double.TryParse(value, out double parsedFrequency))
                        FrequencyHz = parsedFrequency;
                }
            }

            private bool IsHold => string.Equals(Kind, "Hold", StringComparison.OrdinalIgnoreCase);

            public double FrequencyHz
            {
                get => frequencyHz;
                set
                {
                    if (Math.Abs(frequencyHz - value) < 0.0001)
                        return;

                    frequencyHz = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrequencyHz)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrequencyDisplay)));
                }
            }

            public double DurationSeconds
            {
                get => durationSeconds;
                set
                {
                    if (Math.Abs(durationSeconds - value) < 0.0001)
                        return;

                    durationSeconds = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationSeconds)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class TonePresetManagerItem : INotifyPropertyChanged
        {
            private string displayName = string.Empty;
            private string targetResourceKey = string.Empty;
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public ObservableCollection<TonePresetStepItem> Steps { get; } = new ObservableCollection<TonePresetStepItem>();

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

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class TonePresetTargetItem
        {
            public string Key { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public IReadOnlyList<ChannelBox> Channels { get; set; } = Array.Empty<ChannelBox>();
            public bool ClearPageStateAfterSend { get; set; }
        }

        private TonePresetManagerItem selectedPreset;
        private TonePresetTargetItem selectedTarget;

        private readonly Action<IReadOnlyList<TonePresetManagerItem>> saveCallback;
        private readonly Func<TonePresetManagerItem, TonePresetTargetItem, Task> sendCallback;

        public ObservableCollection<TonePresetManagerItem> Presets { get; }
        public List<TonePresetTargetItem> Targets { get; }

        public TonePresetManagerItem SelectedPreset
        {
            get => selectedPreset;
            set
            {
                if (selectedPreset == value)
                    return;

                selectedPreset = value;
                SelectedTarget = ResolveTargetForPreset(selectedPreset);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPreset)));
            }
        }

        public TonePresetTargetItem SelectedTarget
        {
            get => selectedTarget;
            set
            {
                if (selectedTarget == value)
                    return;

                selectedTarget = value;
                if (selectedPreset != null && selectedTarget != null && !string.IsNullOrWhiteSpace(selectedTarget.Key))
                    selectedPreset.TargetResourceKey = selectedTarget.Key;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTarget)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public TonePresetManagerWindow(
            IEnumerable<SettingsManager.TonePresetConfig> presets,
            IEnumerable<TonePresetTargetItem> targets,
            Action<IReadOnlyList<TonePresetManagerItem>> saveCallback,
            Func<TonePresetManagerItem, TonePresetTargetItem, Task> sendCallback)
        {
            InitializeComponent();

            this.saveCallback = saveCallback;
            this.sendCallback = sendCallback;

            Presets = new ObservableCollection<TonePresetManagerItem>(
                (presets ?? Enumerable.Empty<SettingsManager.TonePresetConfig>())
                .OrderBy(preset => preset.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(CreateManagerItem));

            Targets = (targets ?? Enumerable.Empty<TonePresetTargetItem>())
                .Where(target => target != null)
                .ToList();

            if (Presets.Count > 0)
                SelectedPreset = Presets[0];
            if (SelectedTarget == null && Targets.Count > 0)
                SelectedTarget = Targets[0];

            Presets.CollectionChanged += Presets_CollectionChanged;
            foreach (TonePresetManagerItem preset in Presets)
                AttachPresetEvents(preset);

            DataContext = this;
        }

        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            TonePresetManagerItem item = new TonePresetManagerItem
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "New Tone Preset",
                TargetResourceKey = SelectedTarget?.Key ?? string.Empty
            };
            item.Steps.Add(new TonePresetStepItem { Kind = "Tone", FrequencyHz = 1000, DurationSeconds = 1 });

            Presets.Add(item);
            SelectedPreset = item;
            PresetGrid.SelectedItem = item;
            PresetGrid.ScrollIntoView(item);
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null)
                return;

            HideStatus();

            MessageBoxResult result = MessageBox.Show(
                $"Delete tone preset '{SelectedPreset.DisplayName}'?",
                "Delete Tone Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int removedIndex = Presets.IndexOf(SelectedPreset);
            Presets.Remove(SelectedPreset);
            SelectedPreset = Presets.ElementAtOrDefault(Math.Min(removedIndex, Presets.Count - 1));
        }

        private void AddTone_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null)
                AddPreset_Click(sender, e);

            HideStatus();
            SelectedPreset?.Steps.Add(new TonePresetStepItem { Kind = "Tone", FrequencyHz = 1000, DurationSeconds = 1 });
        }

        private void AddHold_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null)
                AddPreset_Click(sender, e);

            HideStatus();
            SelectedPreset?.Steps.Add(new TonePresetStepItem { Kind = "Hold", FrequencyHz = 0, DurationSeconds = 0.75 });
        }

        private void DeleteTone_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null || ToneStepGrid.SelectedItem is not TonePresetStepItem step)
                return;

            HideStatus();
            SelectedPreset.Steps.Remove(step);
        }

        private void MoveToneUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedTone(-1);
        }

        private void MoveToneDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedTone(1);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            IReadOnlyList<TonePresetManagerItem> sanitized = SanitizePresets();
            saveCallback?.Invoke(sanitized);

            StatusTextBlock.Text = "Changes saved.";
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            HideStatus();

            if (SelectedPreset == null || SanitizePreset(SelectedPreset) == null)
            {
                MessageBox.Show("Select or create a tone preset first.", "Tone Presets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedTarget == null || SelectedTarget.Channels == null || SelectedTarget.Channels.Count == 0)
            {
                MessageBox.Show("Select at least one resource before sending a tone preset.", "Tone Presets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await sendCallback(SelectedPreset, SelectedTarget);
                StatusTextBlock.Text = "Tone preset sent.";
                StatusTextBlock.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send tone preset: {ex.Message}", "Tone Presets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MoveSelectedTone(int direction)
        {
            if (SelectedPreset == null || ToneStepGrid.SelectedItem is not TonePresetStepItem step)
                return;

            int oldIndex = SelectedPreset.Steps.IndexOf(step);
            int newIndex = oldIndex + direction;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= SelectedPreset.Steps.Count)
                return;

            HideStatus();
            SelectedPreset.Steps.Move(oldIndex, newIndex);
            ToneStepGrid.SelectedItem = step;
        }

        private IReadOnlyList<TonePresetManagerItem> SanitizePresets()
        {
            return Presets
                .Select(SanitizePreset)
                .Where(preset => preset != null)
                .ToList();
        }

        private static TonePresetManagerItem SanitizePreset(TonePresetManagerItem preset)
        {
            if (preset == null)
                return null;

            List<TonePresetStepItem> steps = preset.Steps
                .Where(step => step != null)
                .Select(step =>
                {
                    bool isHold = string.Equals(step.Kind, "Hold", StringComparison.OrdinalIgnoreCase);
                    return new TonePresetStepItem
                    {
                        Kind = isHold ? "Hold" : "Tone",
                        FrequencyHz = isHold ? 0 : Math.Clamp(step.FrequencyHz, 1, 4000),
                        DurationSeconds = Math.Clamp(
                            step.DurationSeconds,
                            SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS,
                            SettingsManager.TONE_PRESET_MAX_DURATION_SECONDS)
                    };
                })
                .ToList();

            if (steps.Count == 0)
                return null;

            TonePresetManagerItem sanitized = new TonePresetManagerItem
            {
                Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id,
                DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? "Tone Preset" : preset.DisplayName.Trim(),
                TargetResourceKey = preset.TargetResourceKey
            };

            foreach (TonePresetStepItem step in steps)
                sanitized.Steps.Add(step);

            return sanitized;
        }

        private static TonePresetManagerItem CreateManagerItem(SettingsManager.TonePresetConfig config)
        {
            TonePresetManagerItem item = new TonePresetManagerItem
            {
                Id = string.IsNullOrWhiteSpace(config?.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                DisplayName = string.IsNullOrWhiteSpace(config?.DisplayName) ? "Tone Preset" : config.DisplayName,
                TargetResourceKey = config?.TargetResourceKey ?? string.Empty
            };

            foreach (SettingsManager.TonePresetStep step in config?.Steps ?? new List<SettingsManager.TonePresetStep>())
            {
                item.Steps.Add(new TonePresetStepItem
                {
                    Kind = string.Equals(step.Kind, SettingsManager.TONE_PRESET_STEP_KIND_HOLD, StringComparison.OrdinalIgnoreCase)
                        ? "Hold"
                        : "Tone",
                    FrequencyHz = step.FrequencyHz,
                    DurationSeconds = step.DurationSeconds
                });
            }

            return item;
        }

        private TonePresetTargetItem ResolveTargetForPreset(TonePresetManagerItem preset)
        {
            if (preset == null)
                return Targets.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preset.TargetResourceKey))
            {
                TonePresetTargetItem savedTarget = Targets.FirstOrDefault(target =>
                    string.Equals(target.Key, preset.TargetResourceKey, StringComparison.OrdinalIgnoreCase));

                if (savedTarget != null)
                    return savedTarget;
            }

            return selectedTarget ?? Targets.FirstOrDefault();
        }

        private void CommitGridEdits()
        {
            PresetGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            PresetGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ToneStepGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ToneStepGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void Presets_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (TonePresetManagerItem item in e.OldItems.OfType<TonePresetManagerItem>())
                    DetachPresetEvents(item);
            }

            if (e.NewItems != null)
            {
                foreach (TonePresetManagerItem item in e.NewItems.OfType<TonePresetManagerItem>())
                    AttachPresetEvents(item);
            }
        }

        private void AttachPresetEvents(TonePresetManagerItem preset)
        {
            preset.PropertyChanged += Preset_PropertyChanged;
            preset.Steps.CollectionChanged += Steps_CollectionChanged;
            foreach (TonePresetStepItem step in preset.Steps)
                step.PropertyChanged += Step_PropertyChanged;
        }

        private void DetachPresetEvents(TonePresetManagerItem preset)
        {
            preset.PropertyChanged -= Preset_PropertyChanged;
            preset.Steps.CollectionChanged -= Steps_CollectionChanged;
            foreach (TonePresetStepItem step in preset.Steps)
                step.PropertyChanged -= Step_PropertyChanged;
        }

        private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (TonePresetStepItem step in e.OldItems.OfType<TonePresetStepItem>())
                    step.PropertyChanged -= Step_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (TonePresetStepItem step in e.NewItems.OfType<TonePresetStepItem>())
                    step.PropertyChanged += Step_PropertyChanged;
            }

            HideStatus();
        }

        private void Preset_PropertyChanged(object sender, PropertyChangedEventArgs e) => HideStatus();

        private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e) => HideStatus();

        private void HideStatus()
        {
            StatusTextBlock.Text = string.Empty;
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}
