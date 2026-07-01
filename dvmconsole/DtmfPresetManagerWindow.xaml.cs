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
    /// Interaction logic for DtmfPresetManagerWindow.xaml
    /// </summary>
    public partial class DtmfPresetManagerWindow : Window, INotifyPropertyChanged
    {
        public sealed class DtmfPresetStepItem : INotifyPropertyChanged
        {
            private string kind = SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT;
            private string digit = "1";
            private double durationSeconds = SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS;

            public string Kind
            {
                get => kind;
                set
                {
                    string normalizedKind = string.Equals(value, SettingsManager.TONE_PRESET_STEP_KIND_HOLD, StringComparison.OrdinalIgnoreCase)
                        ? SettingsManager.TONE_PRESET_STEP_KIND_HOLD
                        : SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT;

                    if (kind == normalizedKind)
                        return;

                    kind = normalizedKind;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kind)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DigitDisplay)));
                }
            }

            public string Digit
            {
                get => digit;
                set
                {
                    string normalizedDigit = NormalizeDtmfDigit(value);
                    if (digit == normalizedDigit)
                        return;

                    digit = normalizedDigit;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Digit)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DigitDisplay)));
                }
            }

            public string DigitDisplay
            {
                get => IsHold ? "Hold" : Digit;
                set
                {
                    if (IsHold)
                        return;

                    Digit = value;
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

            private bool IsHold => string.Equals(Kind, SettingsManager.TONE_PRESET_STEP_KIND_HOLD, StringComparison.OrdinalIgnoreCase);

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class DtmfPresetManagerItem : INotifyPropertyChanged
        {
            private string displayName = string.Empty;
            private string targetResourceKey = string.Empty;
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public ObservableCollection<DtmfPresetStepItem> Steps { get; } = new ObservableCollection<DtmfPresetStepItem>();

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

        public sealed class DtmfPresetTargetItem
        {
            public string Key { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public IReadOnlyList<ChannelBox> Channels { get; set; } = Array.Empty<ChannelBox>();
            public bool ClearPageStateAfterSend { get; set; }
        }

        private DtmfPresetManagerItem selectedPreset;
        private DtmfPresetTargetItem selectedTarget;

        private readonly Action<IReadOnlyList<DtmfPresetManagerItem>> saveCallback;
        private readonly Func<DtmfPresetManagerItem, DtmfPresetTargetItem, Task> sendCallback;

        public ObservableCollection<DtmfPresetManagerItem> Presets { get; }
        public List<DtmfPresetTargetItem> Targets { get; }

        public DtmfPresetManagerItem SelectedPreset
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

        public DtmfPresetTargetItem SelectedTarget
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

        public DtmfPresetManagerWindow(
            IEnumerable<SettingsManager.DtmfPresetConfig> presets,
            IEnumerable<DtmfPresetTargetItem> targets,
            Action<IReadOnlyList<DtmfPresetManagerItem>> saveCallback,
            Func<DtmfPresetManagerItem, DtmfPresetTargetItem, Task> sendCallback)
        {
            InitializeComponent();

            this.saveCallback = saveCallback;
            this.sendCallback = sendCallback;

            Presets = new ObservableCollection<DtmfPresetManagerItem>(
                (presets ?? Enumerable.Empty<SettingsManager.DtmfPresetConfig>())
                .OrderBy(preset => preset.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(CreateManagerItem));

            Targets = (targets ?? Enumerable.Empty<DtmfPresetTargetItem>())
                .Where(target => target != null)
                .ToList();

            if (Presets.Count > 0)
                SelectedPreset = Presets[0];
            if (SelectedTarget == null && Targets.Count > 0)
                SelectedTarget = Targets[0];

            Presets.CollectionChanged += Presets_CollectionChanged;
            foreach (DtmfPresetManagerItem preset in Presets)
                AttachPresetEvents(preset);

            DataContext = this;
        }

        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            DtmfPresetManagerItem item = new DtmfPresetManagerItem
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "New DTMF Preset",
                TargetResourceKey = SelectedTarget?.Key ?? string.Empty
            };
            item.Steps.Add(new DtmfPresetStepItem { Kind = SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT, Digit = "1", DurationSeconds = SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS });

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
                $"Delete DTMF preset '{SelectedPreset.DisplayName}'?",
                "Delete DTMF Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int removedIndex = Presets.IndexOf(SelectedPreset);
            Presets.Remove(SelectedPreset);
            SelectedPreset = Presets.ElementAtOrDefault(Math.Min(removedIndex, Presets.Count - 1));
        }

        private void AddDigit_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null)
                AddPreset_Click(sender, e);

            HideStatus();
            SelectedPreset?.Steps.Add(new DtmfPresetStepItem { Kind = SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT, Digit = "1", DurationSeconds = SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS });
        }

        private void AddHold_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null)
                AddPreset_Click(sender, e);

            HideStatus();
            SelectedPreset?.Steps.Add(new DtmfPresetStepItem { Kind = SettingsManager.TONE_PRESET_STEP_KIND_HOLD, Digit = string.Empty, DurationSeconds = 0.75 });
        }

        private void DeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null || DtmfStepGrid.SelectedItem is not DtmfPresetStepItem step)
                return;

            HideStatus();
            SelectedPreset.Steps.Remove(step);
        }

        private void MoveStepUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedStep(-1);
        }

        private void MoveStepDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedStep(1);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits();
            IReadOnlyList<DtmfPresetManagerItem> sanitized = SanitizePresets();
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
                MessageBox.Show("Select or create a DTMF preset first.", "DTMF Presets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedTarget == null || SelectedTarget.Channels == null || SelectedTarget.Channels.Count == 0)
            {
                MessageBox.Show("Select at least one resource before sending a DTMF preset.", "DTMF Presets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await sendCallback(SelectedPreset, SelectedTarget);
                StatusTextBlock.Text = "DTMF preset sent.";
                StatusTextBlock.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send DTMF preset: {ex.Message}", "DTMF Presets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MoveSelectedStep(int direction)
        {
            if (SelectedPreset == null || DtmfStepGrid.SelectedItem is not DtmfPresetStepItem step)
                return;

            int oldIndex = SelectedPreset.Steps.IndexOf(step);
            int newIndex = oldIndex + direction;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= SelectedPreset.Steps.Count)
                return;

            HideStatus();
            SelectedPreset.Steps.Move(oldIndex, newIndex);
            DtmfStepGrid.SelectedItem = step;
        }

        private IReadOnlyList<DtmfPresetManagerItem> SanitizePresets()
        {
            return Presets
                .Select(SanitizePreset)
                .Where(preset => preset != null)
                .ToList();
        }

        private static DtmfPresetManagerItem SanitizePreset(DtmfPresetManagerItem preset)
        {
            if (preset == null)
                return null;

            List<DtmfPresetStepItem> steps = preset.Steps
                .Where(step => step != null)
                .Select(step =>
                {
                    bool isHold = string.Equals(step.Kind, SettingsManager.TONE_PRESET_STEP_KIND_HOLD, StringComparison.OrdinalIgnoreCase);
                    return new DtmfPresetStepItem
                    {
                        Kind = isHold ? SettingsManager.TONE_PRESET_STEP_KIND_HOLD : SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT,
                        Digit = isHold ? string.Empty : NormalizeDtmfDigit(step.Digit),
                        DurationSeconds = Math.Clamp(
                            step.DurationSeconds,
                            SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS,
                            SettingsManager.TONE_PRESET_MAX_DURATION_SECONDS)
                    };
                })
                .ToList();

            if (steps.Count == 0)
                return null;

            DtmfPresetManagerItem sanitized = new DtmfPresetManagerItem
            {
                Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id,
                DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? "DTMF Preset" : preset.DisplayName.Trim(),
                TargetResourceKey = preset.TargetResourceKey
            };

            foreach (DtmfPresetStepItem step in steps)
                sanitized.Steps.Add(step);

            return sanitized;
        }

        private static DtmfPresetManagerItem CreateManagerItem(SettingsManager.DtmfPresetConfig config)
        {
            DtmfPresetManagerItem item = new DtmfPresetManagerItem
            {
                Id = string.IsNullOrWhiteSpace(config?.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                DisplayName = string.IsNullOrWhiteSpace(config?.DisplayName) ? "DTMF Preset" : config.DisplayName,
                TargetResourceKey = config?.TargetResourceKey ?? string.Empty
            };

            foreach (SettingsManager.DtmfPresetStep step in config?.Steps ?? new List<SettingsManager.DtmfPresetStep>())
            {
                bool isHold = string.Equals(step.Kind, SettingsManager.TONE_PRESET_STEP_KIND_HOLD, StringComparison.OrdinalIgnoreCase);
                item.Steps.Add(new DtmfPresetStepItem
                {
                    Kind = isHold ? SettingsManager.TONE_PRESET_STEP_KIND_HOLD : SettingsManager.DTMF_PRESET_STEP_KIND_DIGIT,
                    Digit = isHold ? string.Empty : NormalizeDtmfDigit(step.Digit),
                    DurationSeconds = step.DurationSeconds
                });
            }

            return item;
        }

        private DtmfPresetTargetItem ResolveTargetForPreset(DtmfPresetManagerItem preset)
        {
            if (preset == null)
                return Targets.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preset.TargetResourceKey))
            {
                DtmfPresetTargetItem savedTarget = Targets.FirstOrDefault(target =>
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
            DtmfStepGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DtmfStepGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void Presets_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DtmfPresetManagerItem item in e.OldItems.OfType<DtmfPresetManagerItem>())
                    DetachPresetEvents(item);
            }

            if (e.NewItems != null)
            {
                foreach (DtmfPresetManagerItem item in e.NewItems.OfType<DtmfPresetManagerItem>())
                    AttachPresetEvents(item);
            }
        }

        private void AttachPresetEvents(DtmfPresetManagerItem preset)
        {
            preset.PropertyChanged += Preset_PropertyChanged;
            preset.Steps.CollectionChanged += Steps_CollectionChanged;
            foreach (DtmfPresetStepItem step in preset.Steps)
                step.PropertyChanged += Step_PropertyChanged;
        }

        private void DetachPresetEvents(DtmfPresetManagerItem preset)
        {
            preset.PropertyChanged -= Preset_PropertyChanged;
            preset.Steps.CollectionChanged -= Steps_CollectionChanged;
            foreach (DtmfPresetStepItem step in preset.Steps)
                step.PropertyChanged -= Step_PropertyChanged;
        }

        private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DtmfPresetStepItem step in e.OldItems.OfType<DtmfPresetStepItem>())
                    step.PropertyChanged -= Step_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (DtmfPresetStepItem step in e.NewItems.OfType<DtmfPresetStepItem>())
                    step.PropertyChanged += Step_PropertyChanged;
            }

            HideStatus();
        }

        private static string NormalizeDtmfDigit(string digit)
        {
            string normalizedDigit = (digit ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedDigit.Length > 1)
                normalizedDigit = normalizedDigit.Substring(0, 1);

            return normalizedDigit.Length == 1 && "0123456789*#ABCD".Contains(normalizedDigit)
                ? normalizedDigit
                : "1";
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
