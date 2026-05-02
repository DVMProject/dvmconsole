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
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Linq;

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for TarViewerWindow.xaml
    /// </summary>
    public partial class TarViewerWindow : Window, INotifyPropertyChanged
    {
        public sealed class TarRecordingListItem
        {
            public TarRecordingMetadata Metadata { get; init; }
            public DateTime UtcStartSortKey => Metadata.UtcStartTime;
            public DateTime LocalStartTime => Metadata.UtcStartTime.ToLocalTime();
            public string LocalStartDisplay => LocalStartTime.ToString("g");
            public string Direction => Metadata.Direction.ToString();
            public string Protocol => Metadata.Protocol ?? string.Empty;
            public string SystemName => Metadata.SystemName;
            public string ChannelName => Metadata.ChannelName;
            public string TalkgroupId => Metadata.TalkgroupId?.ToString() ?? string.Empty;
            public string SubscriberId => Metadata.SubscriberId?.ToString() ?? string.Empty;
            public string SubscriberAlias => Metadata.SubscriberAlias;
            public string DurationDisplay => TimeSpan.FromMilliseconds(Math.Max(0, Metadata.DurationMs)).ToString(@"hh\:mm\:ss");
            public string EncryptionSummary => !Metadata.IsEncrypted
                ? "Clear"
                : string.IsNullOrWhiteSpace(Metadata.EncryptionAlgorithm)
                    ? "Encrypted"
                    : Metadata.EncryptionKeyId.HasValue
                        ? $"{Metadata.EncryptionAlgorithm} / {Metadata.EncryptionKeyId.Value:X4}"
                        : Metadata.EncryptionAlgorithm;
        }

        public ObservableCollection<TarRecordingListItem> Recordings { get; } = new ObservableCollection<TarRecordingListItem>();
        public ICollectionView RecordingsView { get; }

        public IReadOnlyList<string> DirectionFilters { get; } = new[] { "All", "RX", "TX" };
        public IReadOnlyList<string> ProtocolFilters { get; } = new[] { "All", "P25", "DMR" };
        public IReadOnlyList<string> EncryptionFilters { get; } = new[] { "All", "Clear", "Encrypted" };

        private readonly TarManager tarManager;
        private WaveOutEvent playbackOutput;
        private AudioFileReader playbackReader;
        private string currentPlaybackPath = string.Empty;

        private string searchText = string.Empty;
        private string selectedDirectionFilter = "All";
        private string selectedProtocolFilter = "All";
        private string selectedEncryptionFilter = "All";
        private string systemFilter = string.Empty;
        private string channelFilter = string.Empty;
        private string talkgroupFilter = string.Empty;
        private string sourceIdFilter = string.Empty;
        private string aliasFilter = string.Empty;
        private DateTime? startDateFilter;
        private DateTime? endDateFilter;

        public string SearchText
        {
            get => searchText;
            set => SetFilterProperty(ref searchText, value);
        }

        public string SelectedDirectionFilter
        {
            get => selectedDirectionFilter;
            set => SetFilterProperty(ref selectedDirectionFilter, value);
        }

        public string SelectedProtocolFilter
        {
            get => selectedProtocolFilter;
            set => SetFilterProperty(ref selectedProtocolFilter, value);
        }

        public string SelectedEncryptionFilter
        {
            get => selectedEncryptionFilter;
            set => SetFilterProperty(ref selectedEncryptionFilter, value);
        }

        public string SystemFilter
        {
            get => systemFilter;
            set => SetFilterProperty(ref systemFilter, value);
        }

        public string ChannelFilter
        {
            get => channelFilter;
            set => SetFilterProperty(ref channelFilter, value);
        }

        public string TalkgroupFilter
        {
            get => talkgroupFilter;
            set => SetFilterProperty(ref talkgroupFilter, value);
        }

        public string SourceIdFilter
        {
            get => sourceIdFilter;
            set => SetFilterProperty(ref sourceIdFilter, value);
        }

        public string AliasFilter
        {
            get => aliasFilter;
            set => SetFilterProperty(ref aliasFilter, value);
        }

        public DateTime? StartDateFilter
        {
            get => startDateFilter;
            set => SetFilterProperty(ref startDateFilter, value);
        }

        public DateTime? EndDateFilter
        {
            get => endDateFilter;
            set => SetFilterProperty(ref endDateFilter, value);
        }

        public TarViewerWindow(TarManager tarManager)
        {
            InitializeComponent();
            this.tarManager = tarManager;
            RecordingsView = CollectionViewSource.GetDefaultView(Recordings);
            RecordingsView.Filter = FilterRecording;
            RecordingsView.SortDescriptions.Add(new SortDescription(nameof(TarRecordingListItem.UtcStartSortKey), ListSortDirection.Descending));
            InitializeColumnVisibilityMenu();
            DataContext = this;
            RefreshRecordings();
        }

        public void RefreshView()
        {
            RefreshRecordings();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopPlayback();
            base.OnClosed(e);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshRecordings();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsGrid.SelectedItem is not TarRecordingListItem item)
                return;

            if (item.Metadata == null || string.IsNullOrWhiteSpace(item.Metadata.FilePath) || !File.Exists(item.Metadata.FilePath))
            {
                System.Windows.MessageBox.Show("The selected TAR recording file is missing.", "TAR Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshRecordings();
                return;
            }

            try
            {
                StopPlayback();
                playbackReader = new AudioFileReader(item.Metadata.FilePath);
                playbackOutput = new WaveOutEvent();
                playbackOutput.Init(playbackReader);
                currentPlaybackPath = item.Metadata.FilePath;
                playbackOutput.Play();

                SetStatus($"Playing {item.Metadata.FileName}");
            }
            catch (Exception ex)
            {
                StopPlayback();
                System.Windows.MessageBox.Show($"Unable to play TAR recording. {ex.Message}", "TAR Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsGrid.SelectedItem is not TarRecordingListItem item || string.IsNullOrWhiteSpace(item.Metadata?.FilePath))
                return;

            string targetPath = item.Metadata.FilePath;
            if (!File.Exists(targetPath))
            {
                System.Windows.MessageBox.Show("The selected TAR recording file is missing.", "TAR Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshRecordings();
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"")
            {
                UseShellExecute = true
            });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsGrid.SelectedItem is not TarRecordingListItem item)
                return;

            MessageBoxResult result = System.Windows.MessageBox.Show(
                $"Delete TAR recording '{item.Metadata.FileName}'?",
                "Delete TAR Recording",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            if (string.Equals(currentPlaybackPath, item.Metadata.FilePath, StringComparison.OrdinalIgnoreCase))
                StopPlayback();

            tarManager.DeleteRecording(item.Metadata);
            RefreshRecordings();
            SetStatus("Recording deleted.");
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            SearchText = string.Empty;
            SelectedDirectionFilter = "All";
            SelectedProtocolFilter = "All";
            SelectedEncryptionFilter = "All";
            SystemFilter = string.Empty;
            ChannelFilter = string.Empty;
            TalkgroupFilter = string.Empty;
            SourceIdFilter = string.Empty;
            AliasFilter = string.Empty;
            StartDateFilter = null;
            EndDateFilter = null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColumnsContextMenu == null)
                return;

            ColumnsContextMenu.PlacementTarget = ColumnsButton;
            ColumnsContextMenu.IsOpen = true;
        }

        private void ColumnVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string columnKey)
                return;

            DataGridColumn column = GetColumnByKey(columnKey);
            if (column == null)
                return;

            column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshRecordings()
        {
            FolderPathTextBlock.Text = TarManager.TryEnsureRecordingRoot(
                tarManager.GetConfiguredRecordingRoot(),
                out string rootPath,
                out string errorMessage)
                ? $"Recording Folder: {rootPath}"
                : $"Recording Folder Unavailable: {errorMessage}";

            Recordings.Clear();
            foreach (TarRecordingMetadata metadata in tarManager.LoadRecordings())
                Recordings.Add(new TarRecordingListItem { Metadata = metadata });

            RecordingsView.Refresh();

            if (RecordingsView.Cast<object>().Any())
            {
                HideStatus();
                if (RecordingsGrid.SelectedItem == null)
                    RecordingsGrid.SelectedIndex = 0;
            }
            else
            {
                SetStatus("No TAR recordings match the current view.");
            }
        }

        private void InitializeColumnVisibilityMenu()
        {
            TimeColumnMenuItem.IsChecked = TimeColumn.Visibility == Visibility.Visible;
            DurationColumnMenuItem.IsChecked = DurationColumn.Visibility == Visibility.Visible;
            ChannelColumnMenuItem.IsChecked = ChannelColumn.Visibility == Visibility.Visible;
            TalkgroupColumnMenuItem.IsChecked = TalkgroupColumn.Visibility == Visibility.Visible;
            SourceIdColumnMenuItem.IsChecked = SourceIdColumn.Visibility == Visibility.Visible;
            AliasColumnMenuItem.IsChecked = AliasColumn.Visibility == Visibility.Visible;
            DirectionColumnMenuItem.IsChecked = DirectionColumn.Visibility == Visibility.Visible;
            ProtocolColumnMenuItem.IsChecked = ProtocolColumn.Visibility == Visibility.Visible;
            SystemColumnMenuItem.IsChecked = SystemColumn.Visibility == Visibility.Visible;
            EncryptionColumnMenuItem.IsChecked = EncryptionColumn.Visibility == Visibility.Visible;
        }

        private DataGridColumn GetColumnByKey(string columnKey)
        {
            return columnKey switch
            {
                "Time" => TimeColumn,
                "Duration" => DurationColumn,
                "Channel" => ChannelColumn,
                "Talkgroup" => TalkgroupColumn,
                "SourceId" => SourceIdColumn,
                "Alias" => AliasColumn,
                "Direction" => DirectionColumn,
                "Protocol" => ProtocolColumn,
                "System" => SystemColumn,
                "Encryption" => EncryptionColumn,
                _ => null
            };
        }

        private bool FilterRecording(object obj)
        {
            if (obj is not TarRecordingListItem item || item.Metadata == null)
                return false;

            if (!MatchesTextFilter(SearchText,
                item.SystemName,
                item.ChannelName,
                item.TalkgroupId,
                item.SubscriberId,
                item.SubscriberAlias,
                item.Protocol,
                item.Metadata.FileName))
                return false;

            if (!MatchesTextFilter(SystemFilter, item.SystemName))
                return false;
            if (!MatchesTextFilter(ChannelFilter, item.ChannelName))
                return false;
            if (!MatchesTextFilter(TalkgroupFilter, item.TalkgroupId))
                return false;
            if (!MatchesTextFilter(SourceIdFilter, item.SubscriberId))
                return false;
            if (!MatchesTextFilter(AliasFilter, item.SubscriberAlias))
                return false;

            if (!string.Equals(SelectedDirectionFilter, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Direction, SelectedDirectionFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(SelectedProtocolFilter, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Protocol, SelectedProtocolFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(SelectedEncryptionFilter, "Clear", StringComparison.OrdinalIgnoreCase) && item.Metadata.IsEncrypted)
                return false;
            if (string.Equals(SelectedEncryptionFilter, "Encrypted", StringComparison.OrdinalIgnoreCase) && !item.Metadata.IsEncrypted)
                return false;

            DateTime localDate = item.LocalStartTime.Date;
            if (StartDateFilter.HasValue && localDate < StartDateFilter.Value.Date)
                return false;
            if (EndDateFilter.HasValue && localDate > EndDateFilter.Value.Date)
                return false;

            return true;
        }

        private static bool MatchesTextFilter(string filter, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string trimmedFilter = filter.Trim();
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf(trimmedFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void SetFilterProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            RecordingsView?.Refresh();

            if (!RecordingsView.Cast<object>().Any())
                SetStatus("No TAR recordings match the current view.");
            else
                HideStatus();
        }

        private void StopPlayback()
        {
            if (playbackOutput != null)
            {
                try
                {
                    playbackOutput.Stop();
                }
                catch
                {
                    /* best effort */
                }

                playbackOutput.Dispose();
                playbackOutput = null;
            }

            playbackReader?.Dispose();
            playbackReader = null;
            currentPlaybackPath = string.Empty;
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void HideStatus()
        {
            StatusTextBlock.Text = string.Empty;
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
