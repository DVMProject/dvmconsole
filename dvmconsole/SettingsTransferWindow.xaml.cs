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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for SettingsTransferWindow.xaml
    /// </summary>
    public partial class SettingsTransferWindow : Window
    {
        public sealed class SettingsTransferCategoryItem : INotifyPropertyChanged
        {
            private bool isSelected = true;

            public string Id { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value)
                        return;

                    isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class SettingsTransferZoneItem : INotifyPropertyChanged
        {
            private bool isSelected = true;

            public string ZoneName { get; init; } = string.Empty;

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value)
                        return;

                    isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public ObservableCollection<SettingsTransferCategoryItem> CategoryItems { get; }
        public ObservableCollection<SettingsTransferZoneItem> ZoneItems { get; }
        public bool HasZoneItems => ZoneItems.Count > 0;
        public string ZoneScopeHint => HasZoneItems
            ? "Leave this unchecked for a full profile transfer. Enable it to export/import only resource-scoped settings for the checked zone tabs."
            : "Load a codeplug before opening this window to limit a transfer to specific zone tabs.";

        private readonly SettingsManager settingsManager;
        private readonly Action importedCallback;
        private readonly Func<IEnumerable<string>, SettingsManager.SettingsTransferScope> zoneScopeFactory;

        public SettingsTransferWindow(
            SettingsManager settingsManager,
            Action importedCallback,
            IEnumerable<string> zoneNames = null,
            Func<IEnumerable<string>, SettingsManager.SettingsTransferScope> zoneScopeFactory = null)
        {
            InitializeComponent();

            this.settingsManager = settingsManager;
            this.importedCallback = importedCallback;
            this.zoneScopeFactory = zoneScopeFactory;

            CategoryItems = new ObservableCollection<SettingsTransferCategoryItem>(
                SettingsManager.GetSettingsTransferCategories()
                .Select(category => new SettingsTransferCategoryItem
                {
                    Id = category.Id,
                    DisplayName = category.DisplayName,
                    Description = category.Description,
                    IsSelected = true
                }));

            ZoneItems = new ObservableCollection<SettingsTransferZoneItem>(
                (zoneNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new SettingsTransferZoneItem
                {
                    ZoneName = name.Trim(),
                    IsSelected = true
                }));

            DataContext = this;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllCategoriesSelected(true);
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            SetAllCategoriesSelected(false);
        }

        private void SelectAllZones_Click(object sender, RoutedEventArgs e)
        {
            SetAllZonesSelected(true);
        }

        private void SelectNoZones_Click(object sender, RoutedEventArgs e)
        {
            SetAllZonesSelected(false);
        }

        private void ExportSelected_Click(object sender, RoutedEventArgs e)
        {
            List<string> selectedCategories = GetSelectedCategoryIds();
            if (selectedCategories.Count == 0)
            {
                ShowError("Select at least one settings category to export.");
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export Settings",
                Filter = "dvmconsole Settings (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"dvmconsole-settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                SettingsManager.SettingsTransferScope zoneScope = GetSelectedZoneScope();
                settingsManager.ExportSettingsTransfer(dialog.FileName, selectedCategories, zoneScope);
                string scopeText = zoneScope?.HasScope == true
                    ? $" scoped to {zoneScope.ZoneNames.Count} zone tab(s)"
                    : string.Empty;
                ShowStatus($"Exported {selectedCategories.Count} settings categories{scopeText} to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                ShowError($"Unable to export settings. {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        private void ImportSelected_Click(object sender, RoutedEventArgs e)
        {
            List<string> selectedCategories = GetSelectedCategoryIds();
            if (selectedCategories.Count == 0)
            {
                ShowError("Select at least one settings category to import.");
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import Settings",
                Filter = "dvmconsole Settings (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            MessageBoxResult confirm = MessageBox.Show(
                "Importing settings will overwrite the selected categories in this console profile. Continue?",
                "Import Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                SettingsManager.SettingsTransferScope zoneScope = GetSelectedZoneScope();
                List<string> importedCategories = settingsManager.ImportSettingsTransfer(dialog.FileName, selectedCategories, zoneScope);
                importedCallback?.Invoke();
                string scopeText = zoneScope?.HasScope == true
                    ? $" scoped to {zoneScope.ZoneNames.Count} zone tab(s)"
                    : string.Empty;
                ShowStatus($"Imported{scopeText}: {string.Join(", ", importedCategories)}");
            }
            catch (Exception ex)
            {
                ShowError($"Unable to import settings. {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                SetAllCategoriesSelected(true);
                SetAllZonesSelected(true);
                e.Handled = true;
            }
        }

        private List<string> GetSelectedCategoryIds()
        {
            return CategoryItems
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        private void SetAllCategoriesSelected(bool selected)
        {
            foreach (SettingsTransferCategoryItem item in CategoryItems)
                item.IsSelected = selected;

            HideStatus();
        }

        private void SetAllZonesSelected(bool selected)
        {
            foreach (SettingsTransferZoneItem item in ZoneItems)
                item.IsSelected = selected;

            HideStatus();
        }

        private SettingsManager.SettingsTransferScope GetSelectedZoneScope()
        {
            if (LimitToZonesCheckBox.IsChecked != true)
                return null;

            List<string> selectedZones = ZoneItems
                .Where(item => item.IsSelected)
                .Select(item => item.ZoneName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (selectedZones.Count == 0)
                throw new InvalidOperationException("Select at least one zone tab, or turn off zone-scoped transfer.");

            SettingsManager.SettingsTransferScope scope = zoneScopeFactory?.Invoke(selectedZones) ??
                new SettingsManager.SettingsTransferScope { ZoneNames = selectedZones };
            if (scope?.HasScope != true)
                throw new InvalidOperationException("The selected zone tabs do not contain any scoped settings targets.");

            return scope;
        }

        private void LimitToZones_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HideStatus();
        }

        private void ShowStatus(string message)
        {
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void ShowError(string message)
        {
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void HideStatus()
        {
            StatusTextBlock.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = string.Empty;
        }
    }
}
