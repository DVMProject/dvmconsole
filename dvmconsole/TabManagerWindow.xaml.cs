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
using System.Runtime.CompilerServices;
using System.Windows;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for TabManagerWindow.xaml.
    /// </summary>
    public partial class TabManagerWindow : Window
    {
        public ObservableCollection<ZoneVisibilityItem> Zones { get; } = new ObservableCollection<ZoneVisibilityItem>();

        public IReadOnlyList<string> HiddenZoneNames => Zones
            .Where(zone => !zone.IsVisible)
            .Select(zone => zone.ZoneName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        public TabManagerWindow(IEnumerable<ZoneVisibilityItem> zones)
        {
            InitializeComponent();
            DataContext = this;

            foreach (ZoneVisibilityItem zone in zones ?? Enumerable.Empty<ZoneVisibilityItem>())
            {
                ZoneVisibilityItem item = new ZoneVisibilityItem
                {
                    ZoneName = zone.ZoneName,
                    IsVisible = zone.IsVisible,
                    ChannelCount = zone.ChannelCount,
                    WebStreamCount = zone.WebStreamCount
                };
                item.PropertyChanged += ZoneVisibilityItem_PropertyChanged;
                Zones.Add(item);
            }

            UpdateStatus();
        }

        private void ZoneVisibilityItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ZoneVisibilityItem.IsVisible))
                UpdateStatus();
        }

        private void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ZoneVisibilityItem zone in Zones)
                zone.IsVisible = true;

            UpdateStatus();
        }

        private void HideAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ZoneVisibilityItem zone in Zones)
                zone.IsVisible = false;

            UpdateStatus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Zones.Any(zone => zone.IsVisible))
            {
                StatusTextBlock.Text = "Keep at least one zone visible.";
                return;
            }

            DialogResult = true;
            Close();
        }

        private void UpdateStatus()
        {
            int visibleCount = Zones.Count(zone => zone.IsVisible);
            int hiddenCount = Zones.Count - visibleCount;
            StatusTextBlock.Text = hiddenCount == 0
                ? "All zones are currently visible."
                : $"{hiddenCount} hidden, {visibleCount} visible.";
        }

        public sealed class ZoneVisibilityItem : INotifyPropertyChanged
        {
            private bool isVisible = true;

            public string ZoneName { get; set; } = string.Empty;

            public bool IsVisible
            {
                get => isVisible;
                set
                {
                    if (isVisible == value)
                        return;

                    isVisible = value;
                    OnPropertyChanged();
                }
            }

            public int ChannelCount { get; set; }

            public int WebStreamCount { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
