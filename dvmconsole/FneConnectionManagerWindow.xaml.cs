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
*/
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace dvmconsole
{
    public partial class FneConnectionManagerWindow : Window
    {
        private sealed class FneConnectionRow : INotifyPropertyChanged
        {
            private bool isConnected;
            private bool isBusy;

            public string SystemName { get; init; } = string.Empty;

            public bool IsConnected
            {
                get => isConnected;
                set
                {
                    if (isConnected == value)
                        return;

                    isConnected = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(StatusText));
                    NotifyPropertyChanged(nameof(StatusBrush));
                    NotifyPropertyChanged(nameof(ToggleButtonText));
                }
            }

            public bool IsBusy
            {
                get => isBusy;
                set
                {
                    if (isBusy == value)
                        return;

                    isBusy = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(ButtonsEnabled));
                }
            }

            public string StatusText => IsConnected ? "Connected" : "Disconnected";
            public Brush StatusBrush => IsConnected ? Brushes.LightGreen : Brushes.IndianRed;
            public string ToggleButtonText => IsConnected ? "Stop" : "Start";
            public bool ButtonsEnabled => !IsBusy;

            public event PropertyChangedEventHandler PropertyChanged;

            private void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private readonly ObservableCollection<FneConnectionRow> rows = new ObservableCollection<FneConnectionRow>();

        public FneConnectionManagerWindow()
        {
            InitializeComponent();
            SystemsListView.ItemsSource = rows;
            Loaded += FneConnectionManagerWindow_Loaded;
            Closed += FneConnectionManagerWindow_Closed;
        }

        private void FneConnectionManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.FneConnectionStateChanged += MainWindow_FneConnectionStateChanged;
                LoadSnapshots(mainWindow.GetFneConnectionSnapshots());
            }
        }

        private void FneConnectionManagerWindow_Closed(object sender, EventArgs e)
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.FneConnectionStateChanged -= MainWindow_FneConnectionStateChanged;

            Loaded -= FneConnectionManagerWindow_Loaded;
            Closed -= FneConnectionManagerWindow_Closed;
        }

        private async void StartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow mainWindow || sender is not FrameworkElement element || element.DataContext is not FneConnectionRow row)
                return;

            if (row.IsConnected)
                await mainWindow.StopFneSystemAsync(row.SystemName);
            else
                await mainWindow.StartFneSystemAsync(row.SystemName);
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow mainWindow || sender is not FrameworkElement element || element.DataContext is not FneConnectionRow row)
                return;

            await mainWindow.RestartFneSystemAsync(row.SystemName);
        }

        private void MainWindow_FneConnectionStateChanged(FneConnectionSnapshot snapshot)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (string.IsNullOrWhiteSpace(snapshot.SystemName))
                {
                    LoadSnapshots((Owner as MainWindow)?.GetFneConnectionSnapshots() ?? Array.Empty<FneConnectionSnapshot>());
                    return;
                }

                FneConnectionRow row = rows.FirstOrDefault(existing => string.Equals(existing.SystemName, snapshot.SystemName, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new FneConnectionRow { SystemName = snapshot.SystemName };
                    rows.Add(row);
                    SortRows();
                }

                row.IsConnected = snapshot.IsConnected;
                row.IsBusy = snapshot.IsBusy;
            });
        }

        private void LoadSnapshots(IEnumerable<FneConnectionSnapshot> snapshots)
        {
            rows.Clear();
            foreach (FneConnectionSnapshot snapshot in snapshots.OrderBy(item => item.SystemName, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new FneConnectionRow
                {
                    SystemName = snapshot.SystemName,
                    IsConnected = snapshot.IsConnected,
                    IsBusy = snapshot.IsBusy
                });
            }
        }

        private void SortRows()
        {
            List<FneConnectionRow> sortedRows = rows.OrderBy(row => row.SystemName, StringComparer.OrdinalIgnoreCase).ToList();
            rows.Clear();
            foreach (FneConnectionRow row in sortedRows)
                rows.Add(row);
        }
    }
}
