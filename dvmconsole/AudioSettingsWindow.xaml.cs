// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*   Copyright (C) 2026 C. Lovell, K7CBL
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for AudioSettingsWindow.xaml.
    /// </summary>
    public partial class AudioSettingsWindow : Window
    {
        private const int INHERIT_MASTER_OUTPUT = -2;
        private const double TAB_HEADER_SCROLL_STEP = 180.0;

        private readonly SettingsManager settingsManager;
        private readonly AudioManager audioManager;
        private readonly List<Codeplug.Zone> zones;
        private readonly Action inputDeviceChanged;
        private readonly Dictionary<string, ComboBox> outputSelectorsByTalkgroup = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);

        private ScrollViewer tabHeaderScrollViewer;
        private Button scrollTabsLeftButton;
        private Button scrollTabsRightButton;

        private sealed class AudioDeviceOption
        {
            public string DisplayName { get; set; } = string.Empty;
            public int DeviceNumber { get; set; }
        }

        private sealed class AudioOutputSelectorContext
        {
            public string TalkgroupId { get; init; } = string.Empty;
            public StackPanel ZonePanel { get; init; }
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioSettingsWindow"/> class.
        /// </summary>
        public AudioSettingsWindow(SettingsManager settingsManager, AudioManager audioManager, List<Codeplug.Zone> zones, Action inputDeviceChanged = null)
        {
            InitializeComponent();
            this.settingsManager = settingsManager;
            this.audioManager = audioManager;
            this.zones = zones ?? new List<Codeplug.Zone>();
            this.inputDeviceChanged = inputDeviceChanged;

            Loaded += AudioSettingsWindow_Loaded;
            ZoneRoutingTabs.SelectionChanged += ZoneRoutingTabs_SelectionChanged;
            ZoneRoutingTabs.SizeChanged += ZoneRoutingTabs_SizeChanged;

            LoadAudioDevices();
            LoadZoneOutputSettings();
        }

        /// <summary>
        /// Loads global input and master output device choices.
        /// </summary>
        private void LoadAudioDevices()
        {
            List<AudioDeviceOption> inputDevices = GetAudioInputDevices();
            List<AudioDeviceOption> outputDevices = GetAudioOutputDevices(includeInheritOption: false);

            InputDeviceComboBox.ItemsSource = inputDevices;
            InputDeviceComboBox.SelectedValue = ResolveSavedDevice(settingsManager.AudioInputDevice, WaveIn.DeviceCount);

            MasterOutputComboBox.ItemsSource = outputDevices;
            MasterOutputComboBox.SelectedValue = ResolveSavedDevice(settingsManager.MasterOutputDevice, WaveOut.DeviceCount);

            AgcToggle.IsChecked = settingsManager.AudioInputAgcEnabled;
        }

        /// <summary>
        /// Builds per-zone resource routing tabs.
        /// </summary>
        private void LoadZoneOutputSettings()
        {
            ZoneRoutingTabs.Items.Clear();
            outputSelectorsByTalkgroup.Clear();

            List<AudioDeviceOption> outputDevices = GetAudioOutputDevices(includeInheritOption: true);
            foreach (Codeplug.Zone zone in zones)
            {
                if (zone == null)
                    continue;

                StackPanel panel = new StackPanel { Margin = new Thickness(8) };
                panel.SetResourceReference(TextElement.ForegroundProperty, "MaterialDesignBody");
                TextBlock hint = new TextBlock
                {
                    Text = "Choose Default to inherit the Master Output, or select a device to override this resource.",
                    Opacity = 0.72,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                panel.Children.Add(hint);

                foreach (Codeplug.Channel channel in zone.Channels ?? new List<Codeplug.Channel>())
                    AddResourceOutputRow(panel, channel, outputDevices);

                ScrollViewer scrollViewer = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                scrollViewer.SetResourceReference(Control.BackgroundProperty, "MaterialDesignPaper");
                scrollViewer.SetResourceReference(Control.ForegroundProperty, "MaterialDesignBody");

                ZoneRoutingTabs.Items.Add(new TabItem
                {
                    Header = string.IsNullOrWhiteSpace(zone.Name) ? "Tab" : zone.Name,
                    Content = scrollViewer
                });
            }

            if (ZoneRoutingTabs.Items.Count == 0)
            {
                ZoneRoutingTabs.Items.Add(new TabItem
                {
                    Header = "Resources",
                    Content = new TextBlock
                    {
                        Text = "No resources are available. Load a codeplug to configure audio routing.",
                        Margin = new Thickness(8),
                        Opacity = 0.72
                    }
                });
            }

            ZoneRoutingTabs.SelectedIndex = 0;
            Dispatcher.BeginInvoke(new Action(UpdateTabScrollButtons), DispatcherPriority.Loaded);
        }

        private void AddResourceOutputRow(StackPanel panel, Codeplug.Channel channel, List<AudioDeviceOption> outputDevices)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Tgid))
                return;

            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            TextBlock label = new TextBlock
            {
                Text = $"{channel.Name}  TG {channel.Tgid}",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{channel.Name} ({channel.System}) TG {channel.Tgid}"
            };

            ComboBox selector = new ComboBox
            {
                ItemsSource = outputDevices,
                SelectedValuePath = nameof(AudioDeviceOption.DeviceNumber),
                DisplayMemberPath = nameof(AudioDeviceOption.DisplayName),
                SelectedValue = settingsManager.ChannelOutputDevices.TryGetValue(channel.Tgid, out int selectedDevice)
                    ? ResolveSavedDevice(selectedDevice, WaveOut.DeviceCount)
                    : INHERIT_MASTER_OUTPUT,
                Tag = new AudioOutputSelectorContext
                {
                    TalkgroupId = channel.Tgid,
                    ZonePanel = panel
                },
                MinWidth = 240
            };
            selector.ContextMenu = BuildOutputSelectorContextMenu(selector);

            Grid.SetColumn(label, 0);
            Grid.SetColumn(selector, 1);
            row.Children.Add(label);
            row.Children.Add(selector);
            panel.Children.Add(row);

            outputSelectorsByTalkgroup[channel.Tgid] = selector;
        }

        private ContextMenu BuildOutputSelectorContextMenu(ComboBox selector)
        {
            ContextMenu menu = new ContextMenu();

            MenuItem fillUpItem = new MenuItem
            {
                Header = "Fill Up",
                ToolTip = "Apply this output device to resources above this row.",
                Tag = selector
            };
            fillUpItem.Click += FillOutputUp_Click;

            MenuItem fillDownItem = new MenuItem
            {
                Header = "Fill Down",
                ToolTip = "Apply this output device to resources below this row.",
                Tag = selector
            };
            fillDownItem.Click += FillOutputDown_Click;

            menu.Items.Add(fillUpItem);
            menu.Items.Add(fillDownItem);
            return menu;
        }

        private static List<AudioDeviceOption> GetAudioInputDevices()
        {
            List<AudioDeviceOption> inputDevices = new List<AudioDeviceOption>
            {
                new AudioDeviceOption { DisplayName = "Windows Default Input", DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE }
            };

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                WaveInCapabilities deviceInfo = WaveIn.GetCapabilities(i);
                inputDevices.Add(new AudioDeviceOption { DisplayName = deviceInfo.ProductName, DeviceNumber = i });
            }

            return inputDevices;
        }

        private static List<AudioDeviceOption> GetAudioOutputDevices(bool includeInheritOption)
        {
            List<AudioDeviceOption> outputDevices = new List<AudioDeviceOption>();
            if (includeInheritOption)
                outputDevices.Add(new AudioDeviceOption { DisplayName = "Default (Master Output)", DeviceNumber = INHERIT_MASTER_OUTPUT });

            outputDevices.Add(new AudioDeviceOption { DisplayName = "Windows Default Output", DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE });

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                WaveOutCapabilities deviceInfo = WaveOut.GetCapabilities(i);
                outputDevices.Add(new AudioDeviceOption { DisplayName = deviceInfo.ProductName, DeviceNumber = i });
            }

            return outputDevices;
        }

        private static int ResolveSavedDevice(int savedDevice, int deviceCount)
        {
            if (savedDevice >= 0 && savedDevice < deviceCount)
                return savedDevice;

            return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
        }

        /** WPF Events */

        private void AudioSettingsWindow_Loaded(object sender, RoutedEventArgs e)
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

            ZoneRoutingTabs.ApplyTemplate();

            tabHeaderScrollViewer = ZoneRoutingTabs.Template.FindName("TabHeaderScrollViewer", ZoneRoutingTabs) as ScrollViewer;
            scrollTabsLeftButton = ZoneRoutingTabs.Template.FindName("ScrollTabsLeftButton", ZoneRoutingTabs) as Button;
            scrollTabsRightButton = ZoneRoutingTabs.Template.FindName("ScrollTabsRightButton", ZoneRoutingTabs) as Button;

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

        private void ZoneRoutingTabs_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateTabScrollButtons), DispatcherPriority.Loaded);
        }

        private void ZoneRoutingTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, ZoneRoutingTabs))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ZoneRoutingTabs.SelectedItem is TabItem selectedTab)
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

        private void FillOutputUp_Click(object sender, RoutedEventArgs e)
        {
            FillOutputSelectors(sender, fillDown: false);
        }

        private void FillOutputDown_Click(object sender, RoutedEventArgs e)
        {
            FillOutputSelectors(sender, fillDown: true);
        }

        private void FillOutputSelectors(object sender, bool fillDown)
        {
            if ((sender as FrameworkElement)?.Tag is not ComboBox sourceSelector)
                return;
            if (sourceSelector.SelectedValue is not int selectedOutput)
                return;
            if (sourceSelector.Tag is not AudioOutputSelectorContext context || context.ZonePanel == null)
                return;

            List<ComboBox> zoneSelectors = context.ZonePanel.Children
                .OfType<Grid>()
                .SelectMany(row => row.Children.OfType<ComboBox>())
                .ToList();

            int sourceIndex = zoneSelectors.IndexOf(sourceSelector);
            if (sourceIndex < 0)
                return;

            IEnumerable<ComboBox> targets = fillDown
                ? zoneSelectors.Skip(sourceIndex + 1)
                : zoneSelectors.Take(sourceIndex);

            foreach (ComboBox target in targets)
                target.SelectedValue = selectedOutput;
        }

        /// <summary>
        /// Saves audio routing settings.
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedInput = InputDeviceComboBox.SelectedValue is int inputDevice
                ? inputDevice
                : SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            int selectedMasterOutput = MasterOutputComboBox.SelectedValue is int outputDevice
                ? outputDevice
                : SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            settingsManager.AudioInputDevice = SettingsManager.NormalizeAudioDeviceIndex(selectedInput);
            settingsManager.MasterOutputDevice = SettingsManager.NormalizeAudioDeviceIndex(selectedMasterOutput);
            settingsManager.AudioInputAgcEnabled = AgcToggle.IsChecked == true;

            foreach (KeyValuePair<string, ComboBox> entry in outputSelectorsByTalkgroup)
            {
                int selectedOutput = entry.Value.SelectedValue is int value ? value : INHERIT_MASTER_OUTPUT;
                if (selectedOutput == INHERIT_MASTER_OUTPUT)
                    settingsManager.ChannelOutputDevices.Remove(entry.Key);
                else
                    settingsManager.ChannelOutputDevices[entry.Key] = SettingsManager.NormalizeAudioDeviceIndex(selectedOutput);
            }

            settingsManager.SaveSettings();
            audioManager.ReloadOutputDevices();
            inputDeviceChanged?.Invoke();
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Cancels any pending audio setting changes.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    } // public partial class AudioSettingsWindow : Window
} // namespace dvmconsole
