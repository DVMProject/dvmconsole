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
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.ComponentModel;

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for AudioSettingsWindow.xaml.
    /// </summary>
    public partial class AudioSettingsWindow : Window
    {
        private const double TAB_HEADER_SCROLL_STEP = 180.0;
        private const double MIC_GAIN_DB_MIN = -12.0;
        private const double MIC_GAIN_DB_MAX = 9.5;

        private readonly SettingsManager settingsManager;
        private readonly AudioManager audioManager;
        private readonly List<Codeplug.Zone> zones;
        private readonly Action inputDeviceChanged;
        private readonly Action<bool, double, double, double, double> microphoneProcessingPreviewChanged;
        private readonly Action microphoneProcessingPreviewCanceled;
        private readonly Dictionary<string, ComboBox> outputSelectorsByTalkgroup = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        private List<SettingsManager.AudioInputPresetConfig> micPresetDrafts = new List<SettingsManager.AudioInputPresetConfig>();
        private bool loadingMicProcessingControls;
        private bool settingsSaved;

        private ScrollViewer tabHeaderScrollViewer;
        private Button scrollTabsLeftButton;
        private Button scrollTabsRightButton;

        private sealed class AudioDeviceOption
        {
            public string DisplayName { get; set; } = string.Empty;
            public int DeviceNumber { get; set; }
            public string DeviceKey { get; set; } = string.Empty;
        }

        private sealed class AudioOutputSelectorContext
        {
            public string ResourceKey { get; init; } = string.Empty;
            public StackPanel ZonePanel { get; init; }
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioSettingsWindow"/> class.
        /// </summary>
        public AudioSettingsWindow(
            SettingsManager settingsManager,
            AudioManager audioManager,
            List<Codeplug.Zone> zones,
            Action inputDeviceChanged = null,
            Action<bool, double, double, double, double> microphoneProcessingPreviewChanged = null,
            Action microphoneProcessingPreviewCanceled = null)
        {
            InitializeComponent();
            this.settingsManager = settingsManager;
            this.audioManager = audioManager;
            this.zones = zones ?? new List<Codeplug.Zone>();
            this.inputDeviceChanged = inputDeviceChanged;
            this.microphoneProcessingPreviewChanged = microphoneProcessingPreviewChanged;
            this.microphoneProcessingPreviewCanceled = microphoneProcessingPreviewCanceled;

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

            EnsureSavedDeviceOption(inputDevices, settingsManager.AudioInputDeviceKey, "Saved input device unavailable; using Windows Default until it returns");
            EnsureSavedDeviceOption(outputDevices, settingsManager.MasterOutputDeviceKey, "Saved output device unavailable; using Windows Default until it returns");

            InputDeviceComboBox.ItemsSource = inputDevices;
            InputDeviceComboBox.SelectedValue = ResolveSavedDeviceKey(settingsManager.AudioInputDeviceKey);

            MasterOutputComboBox.ItemsSource = outputDevices;
            MasterOutputComboBox.SelectedValue = ResolveSavedDeviceKey(settingsManager.MasterOutputDeviceKey);

            LoadMicProcessingControls();
        }

        private void LoadMicProcessingControls()
        {
            loadingMicProcessingControls = true;
            micPresetDrafts = SettingsManager.NormalizeAudioInputPresets(settingsManager.AudioInputPresets);
            RefreshMicPresetCombo(settingsManager.AudioInputPresetName);

            AgcToggle.IsChecked = settingsManager.AudioInputAgcEnabled;
            MicGainSlider.Value = LinearGainToDb(settingsManager.AudioInputGain);
            MicLowEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqLowGainDb);
            MicMidEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqMidGainDb);
            MicHighEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqHighGainDb);
            MicPresetNameTextBox.Text = settingsManager.AudioInputPresetName?.Trim() ?? string.Empty;

            loadingMicProcessingControls = false;
            UpdateMicProcessingValueLabels();
        }

        private void RefreshMicPresetCombo(string selectedName = null)
        {
            string normalizedSelectedName = selectedName?.Trim() ?? string.Empty;
            MicPresetComboBox.ItemsSource = null;
            MicPresetComboBox.DisplayMemberPath = nameof(SettingsManager.AudioInputPresetConfig.Name);
            MicPresetComboBox.ItemsSource = micPresetDrafts;

            if (!string.IsNullOrWhiteSpace(normalizedSelectedName))
            {
                MicPresetComboBox.SelectedItem = micPresetDrafts.FirstOrDefault(preset =>
                    string.Equals(preset.Name, normalizedSelectedName, StringComparison.OrdinalIgnoreCase));
            }
        }

        private SettingsManager.AudioInputPresetConfig CaptureMicPreset(string presetName)
        {
            return new SettingsManager.AudioInputPresetConfig
            {
                Name = string.IsNullOrWhiteSpace(presetName) ? "Mic Preset" : presetName.Trim(),
                Gain = DbToLinearGain(MicGainSlider.Value),
                LowGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicLowEqSlider.Value),
                MidGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicMidEqSlider.Value),
                HighGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicHighEqSlider.Value)
            };
        }

        private void ApplyMicPresetToControls(SettingsManager.AudioInputPresetConfig preset)
        {
            if (preset == null)
                return;

            loadingMicProcessingControls = true;
            MicPresetNameTextBox.Text = preset.Name;
            MicGainSlider.Value = LinearGainToDb(preset.Gain);
            MicLowEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(preset.LowGainDb);
            MicMidEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(preset.MidGainDb);
            MicHighEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(preset.HighGainDb);
            loadingMicProcessingControls = false;
            UpdateMicProcessingValueLabels();
            PreviewCurrentMicProcessing();
        }

        private void UpdateMicProcessingValueLabels()
        {
            if (MicGainValueTextBlock == null)
                return;

            MicGainValueTextBlock.Text = FormatGainDb(MicGainSlider.Value);
            MicLowEqValueTextBlock.Text = FormatEqGain(MicLowEqSlider.Value);
            MicMidEqValueTextBlock.Text = FormatEqGain(MicMidEqSlider.Value);
            MicHighEqValueTextBlock.Text = FormatEqGain(MicHighEqSlider.Value);
        }

        private static double LinearGainToDb(double gain)
        {
            double normalized = SettingsManager.NormalizeAudioInputGain(gain);
            return NormalizeMicGainDb(20.0 * Math.Log10(normalized));
        }

        private static double DbToLinearGain(double gainDb)
        {
            return SettingsManager.NormalizeAudioInputGain(Math.Pow(10.0, NormalizeMicGainDb(gainDb) / 20.0));
        }

        private static double NormalizeMicGainDb(double gainDb)
        {
            return double.IsNaN(gainDb) || double.IsInfinity(gainDb)
                ? 0.0
                : Math.Clamp(gainDb, MIC_GAIN_DB_MIN, MIC_GAIN_DB_MAX);
        }

        private static string FormatGainDb(double gainDb)
        {
            double normalized = NormalizeMicGainDb(gainDb);
            return normalized >= 0
                ? $"+{normalized:0.#} dB"
                : $"{normalized:0.#} dB";
        }

        private static string FormatEqGain(double gainDb)
        {
            double normalized = SettingsManager.NormalizeAudioInputEqGainDb(gainDb);
            return normalized >= 0
                ? $"+{normalized:0.#} dB"
                : $"{normalized:0.#} dB";
        }

        private void PreviewCurrentMicProcessing()
        {
            microphoneProcessingPreviewChanged?.Invoke(
                AgcToggle.IsChecked == true,
                DbToLinearGain(MicGainSlider.Value),
                SettingsManager.NormalizeAudioInputEqGainDb(MicLowEqSlider.Value),
                SettingsManager.NormalizeAudioInputEqGainDb(MicMidEqSlider.Value),
                SettingsManager.NormalizeAudioInputEqGainDb(MicHighEqSlider.Value));
        }

        /// <summary>
        /// Builds per-zone resource routing tabs.
        /// </summary>
        private void LoadZoneOutputSettings()
        {
            ZoneRoutingTabs.Items.Clear();
            outputSelectorsByTalkgroup.Clear();

            List<AudioDeviceOption> outputDevices = GetAudioOutputDevices(includeInheritOption: true);
            foreach (string savedDeviceKey in settingsManager.ChannelOutputDeviceKeys?.Values ?? Enumerable.Empty<string>())
                EnsureSavedDeviceOption(outputDevices, savedDeviceKey, "Saved output device unavailable; using Master Output until it returns");

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

                foreach (Codeplug.WebStream stream in zone.WebStreams ?? new List<Codeplug.WebStream>())
                    AddWebStreamOutputRow(panel, stream, outputDevices);

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

            string resourceKey = ResourceIdentity.Build(channel.System, channel.Tgid);
            string selectedDeviceKey = null;
            if (!settingsManager.ChannelOutputDeviceKeys.TryGetValue(resourceKey, out selectedDeviceKey))
                settingsManager.ChannelOutputDeviceKeys.TryGetValue(channel.Tgid, out selectedDeviceKey);

            ComboBox selector = new ComboBox
            {
                ItemsSource = outputDevices,
                SelectedValuePath = nameof(AudioDeviceOption.DeviceKey),
                DisplayMemberPath = nameof(AudioDeviceOption.DisplayName),
                SelectedValue = !string.IsNullOrWhiteSpace(selectedDeviceKey)
                    ? ResolveSavedDeviceKey(selectedDeviceKey)
                    : AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY,
                Tag = new AudioOutputSelectorContext
                {
                    ResourceKey = resourceKey,
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

            outputSelectorsByTalkgroup[resourceKey] = selector;
        }

        private void AddWebStreamOutputRow(StackPanel panel, Codeplug.WebStream stream, List<AudioDeviceOption> outputDevices)
        {
            if (stream == null || string.IsNullOrWhiteSpace(stream.Name))
                return;

            string streamKey = stream.Name.Trim();
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            TextBlock label = new TextBlock
            {
                Text = $"{streamKey}  Stream",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{streamKey} ({stream.Url})"
            };

            ComboBox selector = new ComboBox
            {
                ItemsSource = outputDevices,
                SelectedValuePath = nameof(AudioDeviceOption.DeviceKey),
                DisplayMemberPath = nameof(AudioDeviceOption.DisplayName),
                SelectedValue = settingsManager.ChannelOutputDeviceKeys.TryGetValue(streamKey, out string selectedDeviceKey)
                    ? ResolveSavedDeviceKey(selectedDeviceKey)
                    : AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY,
                Tag = new AudioOutputSelectorContext
                {
                    ResourceKey = streamKey,
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

            outputSelectorsByTalkgroup[streamKey] = selector;
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
                new AudioDeviceOption
                {
                    DisplayName = "Windows Default Input",
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    DeviceKey = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY
                }
            };

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                WaveInCapabilities deviceInfo = WaveIn.GetCapabilities(i);
                inputDevices.Add(new AudioDeviceOption
                {
                    DisplayName = deviceInfo.ProductName,
                    DeviceNumber = i,
                    DeviceKey = AudioDeviceResolver.GetInputDeviceKey(i)
                });
            }

            return inputDevices;
        }

        private static List<AudioDeviceOption> GetAudioOutputDevices(bool includeInheritOption)
        {
            List<AudioDeviceOption> outputDevices = new List<AudioDeviceOption>();
            if (includeInheritOption)
            {
                outputDevices.Add(new AudioDeviceOption
                {
                    DisplayName = "Default (Master Output)",
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    DeviceKey = AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY
                });
            }

            outputDevices.Add(new AudioDeviceOption
            {
                DisplayName = "Windows Default Output",
                DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                DeviceKey = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY
            });

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                WaveOutCapabilities deviceInfo = WaveOut.GetCapabilities(i);
                outputDevices.Add(new AudioDeviceOption
                {
                    DisplayName = deviceInfo.ProductName,
                    DeviceNumber = i,
                    DeviceKey = AudioDeviceResolver.GetOutputDeviceKey(i)
                });
            }

            return outputDevices;
        }

        private static string ResolveSavedDeviceKey(string savedDeviceKey)
        {
            return SettingsManager.NormalizeAudioDeviceKey(savedDeviceKey);
        }

        private static void EnsureSavedDeviceOption(List<AudioDeviceOption> devices, string savedDeviceKey, string unavailableDisplayName)
        {
            string normalizedKey = SettingsManager.NormalizeAudioDeviceKey(savedDeviceKey);
            if (AudioDeviceResolver.IsWindowsDefault(normalizedKey) ||
                string.Equals(normalizedKey, AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY, StringComparison.OrdinalIgnoreCase) ||
                devices.Any(device => string.Equals(device.DeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase)))
                return;

            devices.Add(new AudioDeviceOption
            {
                DisplayName = unavailableDisplayName,
                DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                DeviceKey = normalizedKey
            });
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

        private void MicPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loadingMicProcessingControls)
                return;

            if (MicPresetComboBox.SelectedItem is SettingsManager.AudioInputPresetConfig preset)
                ApplyMicPresetToControls(preset);
        }

        private void MicProcessingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (loadingMicProcessingControls)
                return;

            UpdateMicProcessingValueLabels();
            PreviewCurrentMicProcessing();
        }

        private void AgcToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (loadingMicProcessingControls)
                return;

            PreviewCurrentMicProcessing();
        }

        private void SaveMicPreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = MicPresetNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show("Enter a preset name before saving.", "Mic Preset", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SettingsManager.AudioInputPresetConfig preset = CaptureMicPreset(presetName);
            int existingIndex = micPresetDrafts.FindIndex(existing =>
                string.Equals(existing.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
                micPresetDrafts[existingIndex] = preset;
            else
                micPresetDrafts.Add(preset);

            micPresetDrafts = SettingsManager.NormalizeAudioInputPresets(micPresetDrafts);
            RefreshMicPresetCombo(preset.Name);
            PreviewCurrentMicProcessing();
        }

        private void DeleteMicPreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = (MicPresetComboBox.SelectedItem as SettingsManager.AudioInputPresetConfig)?.Name
                ?? MicPresetNameTextBox.Text?.Trim()
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(presetName))
                return;

            micPresetDrafts = micPresetDrafts
                .Where(preset => !string.Equals(preset.Name, presetName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            loadingMicProcessingControls = true;
            MicPresetNameTextBox.Text = string.Empty;
            loadingMicProcessingControls = false;
            RefreshMicPresetCombo();
        }

        private void ResetMicProcessing_Click(object sender, RoutedEventArgs e)
        {
            loadingMicProcessingControls = true;
            MicPresetComboBox.SelectedItem = null;
            MicPresetNameTextBox.Text = string.Empty;
            MicGainSlider.Value = 0.0;
            MicLowEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(0.0);
            MicMidEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(0.0);
            MicHighEqSlider.Value = SettingsManager.NormalizeAudioInputEqGainDb(0.0);
            loadingMicProcessingControls = false;

            UpdateMicProcessingValueLabels();
            PreviewCurrentMicProcessing();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!settingsSaved)
                microphoneProcessingPreviewCanceled?.Invoke();

            base.OnClosing(e);
        }

        private void FillOutputSelectors(object sender, bool fillDown)
        {
            if ((sender as FrameworkElement)?.Tag is not ComboBox sourceSelector)
                return;
            if (sourceSelector.SelectedValue is not string selectedOutput)
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
            string selectedInputKey = InputDeviceComboBox.SelectedValue as string ?? AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
            string selectedMasterOutputKey = MasterOutputComboBox.SelectedValue as string ?? AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
            int selectedInput = AudioDeviceResolver.ResolveInputDeviceNumber(selectedInputKey, settingsManager.AudioInputDevice);
            int selectedMasterOutput = AudioDeviceResolver.ResolveOutputDeviceNumber(selectedMasterOutputKey, settingsManager.MasterOutputDevice);

            settingsManager.AudioInputDevice = SettingsManager.NormalizeAudioDeviceIndex(selectedInput);
            settingsManager.AudioInputDeviceKey = SettingsManager.NormalizeAudioDeviceKey(selectedInputKey);
            settingsManager.MasterOutputDevice = SettingsManager.NormalizeAudioDeviceIndex(selectedMasterOutput);
            settingsManager.MasterOutputDeviceKey = SettingsManager.NormalizeAudioDeviceKey(selectedMasterOutputKey);
            settingsManager.AudioInputAgcEnabled = AgcToggle.IsChecked == true;
            settingsManager.AudioInputGain = DbToLinearGain(MicGainSlider.Value);
            settingsManager.AudioInputEqLowGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicLowEqSlider.Value);
            settingsManager.AudioInputEqMidGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicMidEqSlider.Value);
            settingsManager.AudioInputEqHighGainDb = SettingsManager.NormalizeAudioInputEqGainDb(MicHighEqSlider.Value);
            settingsManager.AudioInputPresetName = MicPresetNameTextBox.Text?.Trim() ?? string.Empty;
            settingsManager.AudioInputPresets = SettingsManager.NormalizeAudioInputPresets(micPresetDrafts);

            foreach (KeyValuePair<string, ComboBox> entry in outputSelectorsByTalkgroup)
            {
                string selectedOutputKey = entry.Value.SelectedValue as string ?? AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY;
                if (string.Equals(selectedOutputKey, AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY, StringComparison.OrdinalIgnoreCase))
                {
                    settingsManager.ChannelOutputDevices.Remove(entry.Key);
                    settingsManager.ChannelOutputDeviceKeys.Remove(entry.Key);
                }
                else
                {
                    int selectedOutput = AudioDeviceResolver.ResolveOutputDeviceNumber(selectedOutputKey);
                    settingsManager.ChannelOutputDevices[entry.Key] = SettingsManager.NormalizeAudioDeviceIndex(selectedOutput);
                    settingsManager.ChannelOutputDeviceKeys[entry.Key] = SettingsManager.NormalizeAudioDeviceKey(selectedOutputKey);
                }
            }

            settingsManager.SaveSettings();
            audioManager.ReloadOutputDevices();
            inputDeviceChanged?.Invoke();
            RestoreSavedMicProcessingPreview();
            settingsSaved = true;
            Close();
        }

        /// <summary>
        /// Cancels any pending audio setting changes.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RestoreSavedMicProcessingPreview()
        {
            microphoneProcessingPreviewCanceled?.Invoke();
        }
    } // public partial class AudioSettingsWindow : Window
} // namespace dvmconsole
