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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for TarConfigurationWindow.xaml
    /// </summary>
    public partial class TarConfigurationWindow : Window, INotifyPropertyChanged
    {
        private const double TAB_HEADER_SCROLL_STEP = 180.0;

        public sealed class TarChannelConfigItem : INotifyPropertyChanged
        {
            private bool enabled;
            private int retentionDays;
            private string ignoredSubscriberIdsText = string.Empty;

            public string SystemName { get; set; } = string.Empty;
            public string ChannelName { get; set; } = string.Empty;
            public string TalkgroupId { get; set; } = string.Empty;
            public string ResourceKey { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty;

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

            public int RetentionDays
            {
                get => retentionDays;
                set
                {
                    if (retentionDays == value)
                        return;

                    retentionDays = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetentionDays)));
                }
            }

            public string IgnoredSubscriberIdsText
            {
                get => ignoredSubscriberIdsText;
                set
                {
                    if (ignoredSubscriberIdsText == value)
                        return;

                    ignoredSubscriberIdsText = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IgnoredSubscriberIdsText)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class TarZoneConfigGroup
        {
            public string ZoneName { get; set; } = string.Empty;
            public ObservableCollection<TarChannelConfigItem> Channels { get; set; } = new ObservableCollection<TarChannelConfigItem>();
        }

        public ObservableCollection<TarZoneConfigGroup> ZoneGroups { get; }

        private readonly SettingsManager settingsManager;
        private readonly Action savedCallback;
        private readonly Dictionary<string, List<TarChannelConfigItem>> itemsByResource = new Dictionary<string, List<TarChannelConfigItem>>(StringComparer.OrdinalIgnoreCase);

        private ScrollViewer tabHeaderScrollViewer;
        private Button scrollTabsLeftButton;
        private Button scrollTabsRightButton;
        private bool synchronizingTalkgroupItems;
        private string recordingFolderPath = string.Empty;

        public string RecordingFolderPath
        {
            get => recordingFolderPath;
            set
            {
                if (recordingFolderPath == value)
                    return;

                recordingFolderPath = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecordingFolderPath)));
                HideStatus();
            }
        }

        public TarConfigurationWindow(
            SettingsManager settingsManager,
            IEnumerable<Codeplug.Zone> zones,
            Action savedCallback)
        {
            InitializeComponent();

            this.settingsManager = settingsManager;
            this.savedCallback = savedCallback;
            RecordingFolderPath = string.IsNullOrWhiteSpace(settingsManager?.TarRecordingsRootPath)
                ? SettingsManager.DefaultTarRecordingsPath
                : settingsManager.TarRecordingsRootPath.Trim();

            ZoneGroups = new ObservableCollection<TarZoneConfigGroup>(
                BuildZoneGroups(zones ?? Enumerable.Empty<Codeplug.Zone>()));

            if (ZoneGroups.Count == 0)
            {
                ZoneGroups.Add(new TarZoneConfigGroup
                {
                    ZoneName = "Resources"
                });
            }

            DataContext = this;

            Loaded += TarConfigurationWindow_Loaded;
            ZoneTabs.SelectionChanged += ZoneTabs_SelectionChanged;
            ZoneTabs.SizeChanged += ZoneTabs_SizeChanged;
        }

        private IEnumerable<TarZoneConfigGroup> BuildZoneGroups(IEnumerable<Codeplug.Zone> zones)
        {
            foreach (Codeplug.Zone zone in zones)
            {
                if (zone == null)
                    continue;

                TarZoneConfigGroup group = new TarZoneConfigGroup
                {
                    ZoneName = string.IsNullOrWhiteSpace(zone.Name) ? "Tab" : zone.Name.Trim()
                };

                foreach (Codeplug.Channel channel in zone.Channels ?? Enumerable.Empty<Codeplug.Channel>())
                {
                    if (channel == null || string.IsNullOrWhiteSpace(channel.Name) || string.IsNullOrWhiteSpace(channel.Tgid))
                        continue;

                    string resourceKey = ResourceIdentity.Build(channel.System, channel.Tgid);
                    TarChannelConfig config = settingsManager?.GetTarChannelConfig(resourceKey, channel.Name, channel.Tgid) ?? new TarChannelConfig();
                    TarChannelConfigItem item = new TarChannelConfigItem
                    {
                        SystemName = channel.System ?? string.Empty,
                        ChannelName = channel.Name ?? string.Empty,
                        TalkgroupId = channel.Tgid ?? string.Empty,
                        ResourceKey = resourceKey,
                        Mode = (channel.Mode ?? string.Empty).ToUpperInvariant(),
                        Enabled = config.Enabled,
                        RetentionDays = config.RetentionDays,
                        IgnoredSubscriberIdsText = string.Join(", ", config.IgnoredSubscriberIds ?? new List<uint>())
                    };

                    item.PropertyChanged += TarChannelConfigItem_PropertyChanged;
                    RegisterResourceItem(item);
                    group.Channels.Add(item);
                }

                yield return group;
            }
        }

        private void RegisterResourceItem(TarChannelConfigItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ResourceKey))
                return;

            if (!itemsByResource.TryGetValue(item.ResourceKey, out List<TarChannelConfigItem> groupedItems))
            {
                groupedItems = new List<TarChannelConfigItem>();
                itemsByResource[item.ResourceKey] = groupedItems;
            }

            groupedItems.Add(item);
        }

        private void TarConfigurationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HookTabOverflowControls();
            UpdateTabScrollButtons();
        }

        private void HookTabOverflowControls()
        {
            if (scrollTabsLeftButton != null)
                scrollTabsLeftButton.Click -= ScrollTabsLeftButton_Click;
            if (scrollTabsRightButton != null)
                scrollTabsRightButton.Click -= ScrollTabsRightButton_Click;
            if (tabHeaderScrollViewer != null)
                tabHeaderScrollViewer.ScrollChanged -= TabHeaderScrollViewer_ScrollChanged;

            ZoneTabs.ApplyTemplate();

            tabHeaderScrollViewer = ZoneTabs.Template.FindName("TabHeaderScrollViewer", ZoneTabs) as ScrollViewer;
            scrollTabsLeftButton = ZoneTabs.Template.FindName("ScrollTabsLeftButton", ZoneTabs) as Button;
            scrollTabsRightButton = ZoneTabs.Template.FindName("ScrollTabsRightButton", ZoneTabs) as Button;

            if (scrollTabsLeftButton != null)
                scrollTabsLeftButton.Click += ScrollTabsLeftButton_Click;
            if (scrollTabsRightButton != null)
                scrollTabsRightButton.Click += ScrollTabsRightButton_Click;
            if (tabHeaderScrollViewer != null)
                tabHeaderScrollViewer.ScrollChanged += TabHeaderScrollViewer_ScrollChanged;
        }

        private void ScrollTabsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollTabHeader(-TAB_HEADER_SCROLL_STEP);
        }

        private void ScrollTabsRightButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollTabHeader(TAB_HEADER_SCROLL_STEP);
        }

        private void ScrollTabHeader(double delta)
        {
            if (tabHeaderScrollViewer == null)
                HookTabOverflowControls();
            if (tabHeaderScrollViewer == null)
                return;

            double targetOffset = Math.Max(0.0, Math.Min(tabHeaderScrollViewer.ScrollableWidth, tabHeaderScrollViewer.HorizontalOffset + delta));
            tabHeaderScrollViewer.ScrollToHorizontalOffset(targetOffset);
            UpdateTabScrollButtons();
        }

        private void TabHeaderScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateTabScrollButtons();
        }

        private void ZoneTabs_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateTabScrollButtons), DispatcherPriority.Loaded);
        }

        private void ZoneTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, ZoneTabs))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ZoneTabs.SelectedItem != null &&
                    ZoneTabs.ItemContainerGenerator.ContainerFromItem(ZoneTabs.SelectedItem) is TabItem selectedTab)
                    selectedTab.BringIntoView();

                UpdateTabScrollButtons();
            }), DispatcherPriority.Background);
        }

        private void UpdateTabScrollButtons()
        {
            if (tabHeaderScrollViewer == null)
                HookTabOverflowControls();

            bool canScroll = tabHeaderScrollViewer != null && tabHeaderScrollViewer.ScrollableWidth > 0.0;
            if (scrollTabsLeftButton != null)
            {
                scrollTabsLeftButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
                scrollTabsLeftButton.IsEnabled = canScroll && tabHeaderScrollViewer.HorizontalOffset > 0.0;
            }

            if (scrollTabsRightButton != null)
            {
                scrollTabsRightButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
                scrollTabsRightButton.IsEnabled = canScroll && tabHeaderScrollViewer.HorizontalOffset < tabHeaderScrollViewer.ScrollableWidth;
            }
        }

        private void TarChannelConfigItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            HideStatus();

            if (synchronizingTalkgroupItems ||
                sender is not TarChannelConfigItem changedItem ||
                string.IsNullOrWhiteSpace(changedItem.ResourceKey) ||
                !itemsByResource.TryGetValue(changedItem.ResourceKey, out List<TarChannelConfigItem> groupedItems) ||
                groupedItems.Count <= 1)
                return;

            synchronizingTalkgroupItems = true;
            try
            {
                foreach (TarChannelConfigItem item in groupedItems.Where(item => !ReferenceEquals(item, changedItem)))
                {
                    switch (e.PropertyName)
                    {
                        case nameof(TarChannelConfigItem.Enabled):
                            item.Enabled = changedItem.Enabled;
                            break;
                        case nameof(TarChannelConfigItem.RetentionDays):
                            item.RetentionDays = changedItem.RetentionDays;
                            break;
                        case nameof(TarChannelConfigItem.IgnoredSubscriberIdsText):
                            item.IgnoredSubscriberIdsText = changedItem.IgnoredSubscriberIdsText;
                            break;
                    }
                }
            }
            finally
            {
                synchronizingTalkgroupItems = false;
            }
        }

        private void BrowseRecordingFolder_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Select TAR recording folder",
                SelectedPath = string.IsNullOrWhiteSpace(RecordingFolderPath)
                    ? SettingsManager.DefaultTarRecordingsPath
                    : RecordingFolderPath,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            RecordingFolderPath = dialog.SelectedPath;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitPendingEdits();

            if (!TarManager.TryEnsureRecordingRoot(RecordingFolderPath, out string normalizedPath, out string errorMessage))
            {
                System.Windows.MessageBox.Show(
                    $"Unable to use the selected TAR recordings folder. {errorMessage}",
                    "TAR Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            Dictionary<string, TarChannelConfig> configs = new Dictionary<string, TarChannelConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (TarChannelConfigItem item in ZoneGroups.SelectMany(group => group.Channels))
            {
                List<uint> ignoredSubscriberIds = new List<uint>();
                if (!TryParseIgnoredSubscriberIds(item.IgnoredSubscriberIdsText, ignoredSubscriberIds, out string parseError))
                {
                    System.Windows.MessageBox.Show(
                        $"Invalid ignored RID list for channel '{item.ChannelName}'. {parseError}",
                        "TAR Configuration",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(item.ResourceKey))
                    continue;

                configs[item.ResourceKey] = new TarChannelConfig
                {
                    Enabled = item.Enabled,
                    RetentionDays = Math.Max(0, item.RetentionDays),
                    IgnoredSubscriberIds = ignoredSubscriberIds
                };
            }

            settingsManager.SaveTarSettings(normalizedPath, configs);
            savedCallback?.Invoke();

            StatusTextBlock.Text = "Changes saved.";
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void CommitPendingEdits()
        {
            Keyboard.ClearFocus();
            foreach (DataGrid grid in FindVisualChildren<DataGrid>(this))
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                yield break;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    yield return match;

                foreach (T descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HideStatus()
        {
            StatusTextBlock.Text = string.Empty;
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }

        private static bool TryParseIgnoredSubscriberIds(string text, List<uint> output, out string errorMessage)
        {
            errorMessage = string.Empty;
            output.Clear();

            if (string.IsNullOrWhiteSpace(text))
                return true;

            string[] parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (!uint.TryParse(part.Trim(), out uint subscriberId) || subscriberId == 0)
                {
                    errorMessage = $"'{part}' is not a valid subscriber ID.";
                    return false;
                }

                if (!output.Contains(subscriberId))
                    output.Add(subscriberId);
            }

            output.Sort();
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
