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

        public ObservableCollection<SettingsTransferCategoryItem> CategoryItems { get; }

        private readonly SettingsManager settingsManager;
        private readonly Action importedCallback;

        public SettingsTransferWindow(SettingsManager settingsManager, Action importedCallback)
        {
            InitializeComponent();

            this.settingsManager = settingsManager;
            this.importedCallback = importedCallback;

            CategoryItems = new ObservableCollection<SettingsTransferCategoryItem>(
                SettingsManager.GetSettingsTransferCategories()
                .Select(category => new SettingsTransferCategoryItem
                {
                    Id = category.Id,
                    DisplayName = category.DisplayName,
                    Description = category.Description,
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
                settingsManager.ExportSettingsTransfer(dialog.FileName, selectedCategories);
                ShowStatus($"Exported {selectedCategories.Count} settings categories to {dialog.FileName}");
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
                List<string> importedCategories = settingsManager.ImportSettingsTransfer(dialog.FileName, selectedCategories);
                importedCallback?.Invoke();
                ShowStatus($"Imported: {string.Join(", ", importedCategories)}");
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
