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
        private List<AudioDeviceOption> cachedInputDeviceOptions;
        private List<AudioDeviceOption> cachedOutputDeviceOptions;
        private AudioBackendKind? cachedDeviceOptionsBackend;
        private List<SettingsManager.AudioInputPresetConfig> micPresetDrafts = new List<SettingsManager.AudioInputPresetConfig>();
        private bool loadingMicProcessingControls;
        private bool loadingAudioDeviceLists;
        private bool audioDeviceListsLoaded;
        private bool settingsSaved;
        private AudioBackendKind deviceListBackend = AudioBackendKind.Wasapi;
        private int audioDeviceLoadVersion;

        private ScrollViewer tabHeaderScrollViewer;
        private Button scrollTabsLeftButton;
        private Button scrollTabsRightButton;

        private sealed class AudioDeviceOption
        {
            public string DisplayName { get; set; } = string.Empty;
            public int DeviceNumber { get; set; }
            public string DeviceKey { get; set; } = string.Empty;
            public AudioBackendKind Backend { get; set; }
        }

        private sealed class AudioBackendFilterOption
        {
            public string DisplayName { get; set; } = string.Empty;
            public AudioBackendKind Backend { get; set; }
        }

        private sealed class AudioOutputSelectorContext
        {
            public string ResourceKey { get; init; } = string.Empty;
            public StackPanel ZonePanel { get; init; }
        }

        private sealed class SelectedOutputDevice
        {
            public string DeviceKey { get; init; } = string.Empty;
            public int LegacyDeviceNumber { get; init; } = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
        }

        private sealed class AudioDeviceListSnapshot
        {
            public AudioBackendKind Backend { get; init; }
            public List<AudioDeviceOption> InputDevices { get; init; } = new List<AudioDeviceOption>();
            public List<AudioDeviceOption> OutputDevices { get; init; } = new List<AudioDeviceOption>();
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

            InitializeDeviceBackendFilter();
            LoadMicProcessingControls();
            ShowAudioDeviceLoadingState();
        }

        /// <summary>
        /// Loads global input and master output device choices.
        /// </summary>
        private void LoadAudioDevices()
        {
            LoadAudioDevices(settingsManager.AudioInputDeviceKey, settingsManager.MasterOutputDeviceKey, loadMicProcessing: true);
        }

        private void LoadAudioDevices(string selectedInputDeviceKey, string selectedMasterOutputDeviceKey, bool loadMicProcessing)
        {
            List<AudioDeviceOption> inputDevices = GetAudioInputDevices();
            List<AudioDeviceOption> outputDevices = GetAudioOutputDevices(includeInheritOption: false);

            EnsureSavedDeviceOption(inputDevices, selectedInputDeviceKey, settingsManager.AudioInputDevice, "Saved input device unavailable; using Windows Default until it returns");
            EnsureSavedDeviceOption(outputDevices, selectedMasterOutputDeviceKey, settingsManager.MasterOutputDevice, "Saved output device unavailable; using Windows Default until it returns");

            InputDeviceComboBox.ItemsSource = inputDevices;
            InputDeviceComboBox.SelectedValue = ResolveSavedDeviceKey(selectedInputDeviceKey);

            MasterOutputComboBox.ItemsSource = outputDevices;
            MasterOutputComboBox.SelectedValue = ResolveSavedDeviceKey(selectedMasterOutputDeviceKey);

            if (loadMicProcessing)
                LoadMicProcessingControls();
        }

        private async Task ReloadAudioDeviceListsAsync(
            string selectedInputDeviceKey,
            string selectedMasterOutputDeviceKey,
            IReadOnlyDictionary<string, string> selectedOutputDeviceKeys,
            int selectedZoneIndex,
            bool loadMicProcessing)
        {
            int loadVersion = ++audioDeviceLoadVersion;
            AudioBackendKind backend = deviceListBackend;

            SetAudioDeviceControlsLoading(true);
            try
            {
                AudioDeviceListSnapshot snapshot = await Task.Run(() => BuildAudioDeviceListSnapshot(backend));
                if (loadVersion != audioDeviceLoadVersion || !IsLoaded)
                    return;

                cachedDeviceOptionsBackend = snapshot.Backend;
                cachedInputDeviceOptions = snapshot.InputDevices;
                cachedOutputDeviceOptions = snapshot.OutputDevices;
                audioDeviceListsLoaded = true;

                LoadAudioDevices(selectedInputDeviceKey, selectedMasterOutputDeviceKey, loadMicProcessing);
                LoadZoneOutputSettings(selectedOutputDeviceKeys, selectedZoneIndex);
            }
            catch (Exception ex)
            {
                Log.WriteError($"Failed to load audio device list: {ex.Message}");
                if (loadVersion == audioDeviceLoadVersion)
                    ShowAudioDeviceLoadFailedState();
            }
            finally
            {
                if (loadVersion == audioDeviceLoadVersion)
                    SetAudioDeviceControlsLoading(false);
            }
        }

        private void ShowAudioDeviceLoadingState()
        {
            List<AudioDeviceOption> loadingInputDevices = new List<AudioDeviceOption>
            {
                new AudioDeviceOption
                {
                    DisplayName = "Loading audio devices...",
                    DeviceKey = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY,
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    Backend = deviceListBackend
                }
            };

            List<AudioDeviceOption> loadingOutputDevices = new List<AudioDeviceOption>
            {
                new AudioDeviceOption
                {
                    DisplayName = "Loading audio devices...",
                    DeviceKey = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY,
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    Backend = deviceListBackend
                }
            };

            InputDeviceComboBox.ItemsSource = loadingInputDevices;
            InputDeviceComboBox.SelectedIndex = 0;
            MasterOutputComboBox.ItemsSource = loadingOutputDevices;
            MasterOutputComboBox.SelectedIndex = 0;

            ZoneRoutingTabs.Items.Clear();
            ZoneRoutingTabs.Items.Add(new TabItem
            {
                Header = "Resources",
                Content = new TextBlock
                {
                    Text = "Loading audio devices...",
                    Margin = new Thickness(8),
                    Opacity = 0.72
                }
            });
        }

        private void ShowAudioDeviceLoadFailedState()
        {
            audioDeviceListsLoaded = false;
            ZoneRoutingTabs.Items.Clear();
            ZoneRoutingTabs.Items.Add(new TabItem
            {
                Header = "Resources",
                Content = new TextBlock
                {
                    Text = "Audio devices could not be loaded. Close this window and try again.",
                    Margin = new Thickness(8),
                    Opacity = 0.72
                }
            });
        }

        private void SetAudioDeviceControlsLoading(bool isLoading)
        {
            DeviceBackendComboBox.IsEnabled = !isLoading;
            InputDeviceComboBox.IsEnabled = !isLoading;
            MasterOutputComboBox.IsEnabled = !isLoading;
            ZoneRoutingTabs.IsEnabled = !isLoading;
            SaveButton.IsEnabled = !isLoading && audioDeviceListsLoaded;
        }

        private void InitializeDeviceBackendFilter()
        {
            deviceListBackend = AudioBackendKind.Wasapi;

            loadingAudioDeviceLists = true;
            DeviceBackendComboBox.ItemsSource = new List<AudioBackendFilterOption>
            {
                new AudioBackendFilterOption
                {
                    DisplayName = "WASAPI Devices",
                    Backend = AudioBackendKind.Wasapi
                },
                new AudioBackendFilterOption
                {
                    DisplayName = "Legacy MME Devices",
                    Backend = AudioBackendKind.Mme
                }
            };
            DeviceBackendComboBox.SelectedValue = deviceListBackend;
            loadingAudioDeviceLists = false;
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
            LoadZoneOutputSettings(null, selectedZoneIndex: 0);
        }

        private void LoadZoneOutputSettings(IReadOnlyDictionary<string, string> selectedOutputDeviceKeys, int selectedZoneIndex)
        {
            ZoneRoutingTabs.Items.Clear();
            outputSelectorsByTalkgroup.Clear();

            List<AudioDeviceOption> outputDevices = GetAudioOutputDevices(includeInheritOption: true);
            Dictionary<string, SelectedOutputDevice> visibleOutputSelections = ResolveVisibleOutputDeviceSelections(selectedOutputDeviceKeys);
            foreach (SelectedOutputDevice selectedDevice in visibleOutputSelections.Values)
                EnsureSavedDeviceOption(outputDevices, selectedDevice.DeviceKey, selectedDevice.LegacyDeviceNumber, "Selected output device is hidden by the current Device List");

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
                    AddResourceOutputRow(panel, channel, outputDevices, visibleOutputSelections);

                foreach (Codeplug.WebStream stream in zone.WebStreams ?? new List<Codeplug.WebStream>())
                    AddWebStreamOutputRow(panel, stream, outputDevices, visibleOutputSelections);

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

            ZoneRoutingTabs.SelectedIndex = Math.Max(0, Math.Min(selectedZoneIndex, ZoneRoutingTabs.Items.Count - 1));
            Dispatcher.BeginInvoke(new Action(UpdateTabScrollButtons), DispatcherPriority.Loaded);
        }

        private Dictionary<string, SelectedOutputDevice> ResolveVisibleOutputDeviceSelections(IReadOnlyDictionary<string, string> selectedOutputDeviceKeys)
        {
            Dictionary<string, SelectedOutputDevice> selections = new Dictionary<string, SelectedOutputDevice>(StringComparer.OrdinalIgnoreCase);

            foreach (Codeplug.Zone zone in zones)
            {
                if (zone == null)
                    continue;

                foreach (Codeplug.Channel channel in zone.Channels ?? new List<Codeplug.Channel>())
                {
                    if (channel == null || string.IsNullOrWhiteSpace(channel.Tgid))
                        continue;

                    string resourceKey = ResourceIdentity.Build(channel.System, channel.Tgid);
                    if (TryResolveSelectedOutputDevice(resourceKey, channel.Tgid, selectedOutputDeviceKeys, out SelectedOutputDevice selectedDevice))
                        selections[resourceKey] = selectedDevice;
                }

                foreach (Codeplug.WebStream stream in zone.WebStreams ?? new List<Codeplug.WebStream>())
                {
                    if (stream == null || string.IsNullOrWhiteSpace(stream.Name))
                        continue;

                    string streamKey = stream.Name.Trim();
                    if (TryResolveSelectedOutputDevice(streamKey, null, selectedOutputDeviceKeys, out SelectedOutputDevice selectedDevice))
                        selections[streamKey] = selectedDevice;
                }
            }

            return selections;
        }

        private bool TryResolveSelectedOutputDevice(
            string resourceKey,
            string legacyResourceKey,
            IReadOnlyDictionary<string, string> selectedOutputDeviceKeys,
            out SelectedOutputDevice selectedDevice)
        {
            selectedDevice = null;
            string selectedDeviceKey = null;
            int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            if (selectedOutputDeviceKeys != null && selectedOutputDeviceKeys.TryGetValue(resourceKey, out selectedDeviceKey))
            {
                settingsManager.ChannelOutputDevices.TryGetValue(resourceKey, out legacyDeviceNumber);
            }
            else if (settingsManager.ChannelOutputDeviceKeys.TryGetValue(resourceKey, out selectedDeviceKey))
            {
                settingsManager.ChannelOutputDevices.TryGetValue(resourceKey, out legacyDeviceNumber);
            }
            else if (!string.IsNullOrWhiteSpace(legacyResourceKey) &&
                settingsManager.ChannelOutputDeviceKeys.TryGetValue(legacyResourceKey, out selectedDeviceKey))
            {
                settingsManager.ChannelOutputDevices.TryGetValue(legacyResourceKey, out legacyDeviceNumber);
            }

            if (string.IsNullOrWhiteSpace(selectedDeviceKey))
                return false;

            selectedDevice = new SelectedOutputDevice
            {
                DeviceKey = selectedDeviceKey,
                LegacyDeviceNumber = legacyDeviceNumber
            };
            return true;
        }

        private void AddResourceOutputRow(StackPanel panel, Codeplug.Channel channel, List<AudioDeviceOption> outputDevices, IReadOnlyDictionary<string, SelectedOutputDevice> visibleOutputSelections = null)
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
            if (visibleOutputSelections == null || !visibleOutputSelections.TryGetValue(resourceKey, out SelectedOutputDevice selectedOutputDevice))
            {
                if (!settingsManager.ChannelOutputDeviceKeys.TryGetValue(resourceKey, out selectedDeviceKey))
                    settingsManager.ChannelOutputDeviceKeys.TryGetValue(channel.Tgid, out selectedDeviceKey);
            }
            else
            {
                selectedDeviceKey = selectedOutputDevice.DeviceKey;
            }

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

        private void AddWebStreamOutputRow(StackPanel panel, Codeplug.WebStream stream, List<AudioDeviceOption> outputDevices, IReadOnlyDictionary<string, SelectedOutputDevice> visibleOutputSelections = null)
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
                SelectedValue = visibleOutputSelections != null && visibleOutputSelections.TryGetValue(streamKey, out SelectedOutputDevice selectedOutputDevice)
                    ? ResolveSavedDeviceKey(selectedOutputDevice.DeviceKey)
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

        private List<AudioDeviceOption> GetAudioOutputDevices(bool includeInheritOption)
        {
            EnsureDeviceOptionCache();
            List<AudioDeviceOption> outputDevices = cachedOutputDeviceOptions
                .Select(CloneAudioDeviceOption)
                .ToList();

            if (includeInheritOption)
            {
                outputDevices.Insert(0, new AudioDeviceOption
                {
                    DisplayName = "Default (Master Output)",
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    DeviceKey = AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY,
                    Backend = AudioBackendKind.Wasapi
                });
            }

            return outputDevices;
        }

        private static string ResolveSavedDeviceKey(string savedDeviceKey)
        {
            return SettingsManager.NormalizeAudioDeviceKey(savedDeviceKey);
        }

        private static void EnsureSavedDeviceOption(List<AudioDeviceOption> devices, string savedDeviceKey, int legacyDeviceNumber, string unavailableDisplayName)
        {
            string normalizedKey = SettingsManager.NormalizeAudioDeviceKey(savedDeviceKey);
            if (AudioDeviceResolver.IsWindowsDefault(normalizedKey) ||
                string.Equals(normalizedKey, AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY, StringComparison.OrdinalIgnoreCase) ||
                devices.Any(device => string.Equals(device.DeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase)))
                return;

            devices.Add(new AudioDeviceOption
            {
                DisplayName = GetSavedDevicePlaceholderName(normalizedKey, unavailableDisplayName),
                DeviceNumber = SettingsManager.NormalizeAudioDeviceIndex(legacyDeviceNumber),
                DeviceKey = normalizedKey,
                Backend = AudioDeviceResolver.IsWasapiDeviceKey(normalizedKey)
                    ? AudioBackendKind.Wasapi
                    : AudioBackendKind.Mme
            });
        }

        private List<AudioDeviceOption> GetAudioInputDevices()
        {
            EnsureDeviceOptionCache();
            return cachedInputDeviceOptions
                .Select(CloneAudioDeviceOption)
                .ToList();
        }

        private void EnsureDeviceOptionCache()
        {
            if (cachedDeviceOptionsBackend == deviceListBackend &&
                cachedInputDeviceOptions != null &&
                cachedOutputDeviceOptions != null)
                return;

            cachedDeviceOptionsBackend = deviceListBackend;
            cachedInputDeviceOptions = AudioDeviceResolver.GetInputDeviceOptions(deviceListBackend)
                .Select(CreateAudioDeviceOption)
                .ToList();
            cachedOutputDeviceOptions = AudioDeviceResolver.GetOutputDeviceOptions(includeInheritOption: false, deviceListBackend)
                .Select(CreateAudioDeviceOption)
                .ToList();
        }

        private static AudioDeviceListSnapshot BuildAudioDeviceListSnapshot(AudioBackendKind backend)
        {
            return new AudioDeviceListSnapshot
            {
                Backend = backend,
                InputDevices = AudioDeviceResolver.GetInputDeviceOptions(backend)
                    .Select(CreateAudioDeviceOption)
                    .ToList(),
                OutputDevices = AudioDeviceResolver.GetOutputDeviceOptions(includeInheritOption: false, backend)
                    .Select(CreateAudioDeviceOption)
                    .ToList()
            };
        }

        private void ClearDeviceOptionCache()
        {
            cachedDeviceOptionsBackend = null;
            cachedInputDeviceOptions = null;
            cachedOutputDeviceOptions = null;
        }

        private static AudioDeviceOption CreateAudioDeviceOption(AudioDeviceResolver.AudioDeviceOption device)
        {
            return new AudioDeviceOption
            {
                DisplayName = device.DisplayName,
                DeviceNumber = device.DeviceNumber,
                DeviceKey = device.DeviceKey,
                Backend = device.Backend
            };
        }

        private static AudioDeviceOption CloneAudioDeviceOption(AudioDeviceOption device)
        {
            return new AudioDeviceOption
            {
                DisplayName = device.DisplayName,
                DeviceNumber = device.DeviceNumber,
                DeviceKey = device.DeviceKey,
                Backend = device.Backend
            };
        }

        private static bool IsLegacyMmeDeviceKey(string deviceKey)
        {
            string normalizedKey = SettingsManager.NormalizeAudioDeviceKey(deviceKey);
            return !AudioDeviceResolver.IsWindowsDefault(normalizedKey) &&
                !AudioDeviceResolver.IsWasapiDeviceKey(normalizedKey) &&
                !string.Equals(normalizedKey, AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSavedDevicePlaceholderName(string normalizedKey, string unavailableDisplayName)
        {
            if (AudioDeviceResolver.IsWasapiDeviceKey(normalizedKey))
                return "Current WASAPI selection (switch Device List to WASAPI to edit)";

            if (IsLegacyMmeDeviceKey(normalizedKey))
                return "Current Legacy MME selection (switch Device List to Legacy MME to edit)";

            return unavailableDisplayName;
        }

        private Dictionary<string, string> CaptureOutputSelections()
        {
            return outputSelectorsByTalkgroup.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.SelectedValue as string ?? AudioDeviceResolver.INHERIT_MASTER_OUTPUT_KEY,
                StringComparer.OrdinalIgnoreCase);
        }

        /** WPF Events */

        private async void DeviceBackendComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loadingAudioDeviceLists)
                return;

            AudioBackendFilterOption selectedOption = DeviceBackendComboBox.SelectedItem as AudioBackendFilterOption;
            if (selectedOption == null || selectedOption.Backend == deviceListBackend)
                return;

            string selectedInputDeviceKey = InputDeviceComboBox.SelectedValue as string ?? settingsManager.AudioInputDeviceKey;
            string selectedMasterOutputDeviceKey = MasterOutputComboBox.SelectedValue as string ?? settingsManager.MasterOutputDeviceKey;
            Dictionary<string, string> selectedOutputDeviceKeys = CaptureOutputSelections();
            int selectedZoneIndex = Math.Max(0, ZoneRoutingTabs.SelectedIndex);

            deviceListBackend = selectedOption.Backend;
            ClearDeviceOptionCache();
            audioDeviceListsLoaded = false;
            await ReloadAudioDeviceListsAsync(
                selectedInputDeviceKey,
                selectedMasterOutputDeviceKey,
                selectedOutputDeviceKeys,
                selectedZoneIndex,
                loadMicProcessing: false);
        }

        private async void AudioSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HookTabOverflowControls();
            UpdateTabScrollButtons();
            await ReloadAudioDeviceListsAsync(
                settingsManager.AudioInputDeviceKey,
                settingsManager.MasterOutputDeviceKey,
                null,
                selectedZoneIndex: 0,
                loadMicProcessing: false);
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
            if (!audioDeviceListsLoaded)
            {
                MessageBox.Show("Audio devices are still loading. Try again in a moment.", "Audio Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string selectedInputKey = InputDeviceComboBox.SelectedValue as string ?? AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
            string selectedMasterOutputKey = MasterOutputComboBox.SelectedValue as string ?? AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
            int selectedInput = (InputDeviceComboBox.SelectedItem as AudioDeviceOption)?.DeviceNumber ??
                AudioDeviceResolver.ResolveInputDeviceNumber(selectedInputKey, settingsManager.AudioInputDevice);
            int selectedMasterOutput = (MasterOutputComboBox.SelectedItem as AudioDeviceOption)?.DeviceNumber ??
                AudioDeviceResolver.ResolveOutputDeviceNumber(selectedMasterOutputKey, settingsManager.MasterOutputDevice);

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
                    int selectedOutput = (entry.Value.SelectedItem as AudioDeviceOption)?.DeviceNumber ??
                        AudioDeviceResolver.ResolveOutputDeviceNumber(selectedOutputKey);
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
