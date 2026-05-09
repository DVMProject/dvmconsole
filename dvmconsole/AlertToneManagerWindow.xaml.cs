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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

        public ObservableCollection<AlertToneManagerItem> AlertTones { get; }
        public List<string> AvailableTabs { get; }
        private readonly Action<IReadOnlyList<AlertToneManagerItem>> saveCallback;

        public AlertToneManagerWindow(IEnumerable<AlertToneManagerItem> alertTones, IEnumerable<string> availableTabs, Action<IReadOnlyList<AlertToneManagerItem>> saveCallback)
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

            AlertTones.CollectionChanged += AlertTones_CollectionChanged;
            foreach (AlertToneManagerItem item in AlertTones)
                item.PropertyChanged += AlertToneItem_PropertyChanged;

            DataContext = this;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select Alert Tone"
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            string alertFilePath = openFileDialog.FileName;
            if (AlertTones.Any(item => string.Equals(item.FilePath, alertFilePath, StringComparison.OrdinalIgnoreCase)))
                return;

            AlertToneManagerItem item = new AlertToneManagerItem
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = Path.GetFileNameWithoutExtension(alertFilePath),
                FilePath = alertFilePath,
                TabName = AvailableTabs[0]
            };

            AlertTones.Add(item);
            AlertToneGrid.SelectedItem = item;
            AlertToneGrid.ScrollIntoView(item);
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
            AlertToneGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            AlertToneGrid.CommitEdit(DataGridEditingUnit.Row, true);

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

            saveCallback?.Invoke(sanitizedItems);

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

        private void AlertToneItem_PropertyChanged(object sender, PropertyChangedEventArgs e) => HideStatus();

        private void HideStatus()
        {
            StatusTextBlock.Text = string.Empty;
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}
