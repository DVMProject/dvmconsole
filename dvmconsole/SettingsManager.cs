// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using fnecore.Utility;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class SettingsManager
    {
        public const int WINDOWS_DEFAULT_AUDIO_DEVICE = -1;
        public const string LEGACY_GLOBAL_INPUT_DEVICE_KEY = "GLOBAL_INPUT";
        public const int MAX_TOOLBAR_CLOCKS = 8;
        public const string DEFAULT_TOOLBAR_CLOCK_COLOR = "#3A3A3A";
        public const double DEFAULT_CALL_HISTORY_WINDOW_WIDTH = 551;
        public const double DEFAULT_CALL_HISTORY_WINDOW_HEIGHT = 450;

        public static readonly string UserAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        public static readonly string DefaultTarRecordingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DVMConsole",
            "TAR");

        public static readonly string RootAppDataPath = "DVMProject" + Path.DirectorySeparatorChar + "dvmconsole";
        public static string UserAppDataPath = UserAppData + Path.DirectorySeparatorChar + RootAppDataPath;

        private static string SettingsFilePath = UserAppDataPath + Path.DirectorySeparatorChar + "UserSettings.json";
        private const string SETTINGS_TRANSFER_FORMAT = "dvmconsole-settings-transfer";

        private static SettingsManager _instance = null;

        /*
        ** Properties
        */

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static SettingsManager Instance {  get { return _instance; } }

        /// <summary>
        /// Flag indicating whether or not system status widgets will be displayed.
        /// </summary>
        public bool ShowSystemStatus { get; set; } = true;
        /// <summary>
        /// Flag indicating whether or not channel widgets will be displayed.
        /// </summary>
        public bool ShowChannels { get; set; } = true;
        /// <summary>
        /// Flag indicating whether or not alert tone widgets will be displayed.
        /// </summary>
        public bool ShowAlertTones { get; set; } = true;

        /// <summary>
        /// Full path to last loaded console codeplug.
        /// </summary>
        public string LastCodeplugPath { get; set; } = null;

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, ChannelPosition> ChannelPositions { get; set; } = new Dictionary<string, ChannelPosition>();
        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, ChannelPosition> SystemStatusPositions { get; set; } = new Dictionary<string, ChannelPosition>();

        /// <summary>
        /// 
        /// </summary>
        public List<string> AlertToneFilePaths { get; set; } = new List<string>();

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, ChannelPosition> AlertTonePositions { get; set; } = new Dictionary<string, ChannelPosition>();
        /// <summary>
        /// Saved web stream chip positions keyed by stream name.
        /// </summary>
        public Dictionary<string, ChannelPosition> WebStreamPositions { get; set; } = new Dictionary<string, ChannelPosition>();
        /// <summary>
        /// Saved tab assignment for alert tone widgets.
        /// </summary>
        public Dictionary<string, string> AlertToneTabs { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// Saved alert tone configurations using a stable ID.
        /// </summary>
        public List<AlertToneConfig> AlertTones { get; set; } = new List<AlertToneConfig>();

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, int> ChannelOutputDevices { get; set; } = new Dictionary<string, int>();
        /// <summary>
        /// Stable per-resource output device identities keyed by talkgroup ID or stream name.
        /// </summary>
        public Dictionary<string, string> ChannelOutputDeviceKeys { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// Global input device override. -1 follows the current Windows default device.
        /// </summary>
        public int AudioInputDevice { get; set; } = WINDOWS_DEFAULT_AUDIO_DEVICE;
        /// <summary>
        /// Stable global input device identity. Empty or windows-default follows Windows default.
        /// </summary>
        public string AudioInputDeviceKey { get; set; } = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
        /// <summary>
        /// Master output device used by resources without a per-TG override. -1 follows Windows default.
        /// </summary>
        public int MasterOutputDevice { get; set; } = WINDOWS_DEFAULT_AUDIO_DEVICE;
        /// <summary>
        /// Stable master output device identity. Empty or windows-default follows Windows default.
        /// </summary>
        public string MasterOutputDeviceKey { get; set; } = AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY;
        /// <summary>
        /// Enables simple console microphone automatic gain control.
        /// </summary>
        public bool AudioInputAgcEnabled { get; set; } = false;
        /// <summary>
        /// Suppresses local RX speaker playback while the console is transmitting.
        /// </summary>
        public bool MuteRxAudioWhileTransmitting { get; set; } = false;
        /// <summary>
        /// Saved per-channel volume levels.
        /// </summary>
        public Dictionary<string, double> ChannelVolumes { get; set; } = new Dictionary<string, double>();
        /// <summary>
        /// Saved selectable encryption TX state keyed by system and talkgroup.
        /// </summary>
        public Dictionary<string, bool> SelectableEncryptionStates { get; set; } = new Dictionary<string, bool>();
        /// <summary>
        /// Saved web stream chip volumes keyed by stream name.
        /// </summary>
        public Dictionary<string, double> WebStreamVolumes { get; set; } = new Dictionary<string, double>();
        /// <summary>
        /// Root folder where TAR recordings are stored.
        /// </summary>
        public string TarRecordingsRootPath { get; set; } = DefaultTarRecordingsPath;
        /// <summary>
        /// Per-talkgroup TAR settings keyed by talkgroup ID.
        /// </summary>
        public Dictionary<string, TarChannelConfig> TarChannelConfigs { get; set; } = new Dictionary<string, TarChannelConfig>();
        /// <summary>
        /// Saved toolbar clock configuration rows.
        /// </summary>
        public List<ToolbarClockConfig> ToolbarClockConfigs { get; set; } = CreateDefaultToolbarClockConfigs();
        /// <summary>
        /// Saved toolbar clock configuration keyed by clock slot.
        /// </summary>
        public Dictionary<string, ToolbarClockConfig> ToolbarClockConfigSlots { get; set; } = CreateDefaultToolbarClockConfigSlots();
        /// <summary>
        /// Flag indicating toolbar clocks should use 24-hour time.
        /// </summary>
        public bool ClockUse24HourTime { get; set; } = true;
        /// <summary>
        /// Flag indicating toolbar clocks should show seconds.
        /// </summary>
        public bool ClockShowSeconds { get; set; } = true;
        /// <summary>
        /// Saved patch group memberships scoped by codeplug context key.
        /// </summary>
        public Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>> PatchGroupMemberships { get; set; } = new Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>>();
        /// <summary>
        /// Saved patch group one-way mode scoped by codeplug context key.
        /// </summary>
        public Dictionary<string, Dictionary<string, bool>> PatchGroupModes { get; set; } = new Dictionary<string, Dictionary<string, bool>>();
        /// <summary>
        /// Saved patch enabled state scoped by codeplug context key.
        /// </summary>
        public Dictionary<string, Dictionary<string, bool>> PatchGroupEnabledStates { get; set; } = new Dictionary<string, Dictionary<string, bool>>();

        /// <summary>
        /// Stored member identity for a patch talkgroup.
        /// </summary>
        public class PatchTalkgroupMember
        {
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;
        }

        /// <summary>
        /// Persisted configuration for a custom alert tone widget.
        /// </summary>
        public class AlertToneConfig
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string DisplayName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string TabName { get; set; } = string.Empty;
            public ChannelPosition Position { get; set; } = new ChannelPosition { X = 20, Y = 20 };
        }

        /// <summary>
        /// Persisted configuration for a toolbar clock slot.
        /// </summary>
        public class ToolbarClockConfig
        {
            public bool Enabled { get; set; } = false;
            public int UtcOffsetHours { get; set; } = 0;
            public string ColorHex { get; set; } = DEFAULT_TOOLBAR_CLOCK_COLOR;
        }

        /// <summary>
        /// Flag indicating the PTT mode, Toggle PTT or Regular PTT.
        /// </summary>
        public bool TogglePTTMode { get; set; } = false;

        /// <summary>
        /// Flag indicating channel and other widgets are locked in place.
        /// </summary>
        public bool LockWidgets { get; set; } = true;
        /// <summary>
        /// Flag indicating whether or not the call history window should be snapped to the right of the main window.
        /// </summary>
        public bool SnapCallHistoryToWindow { get; set; } = false;

        /// <summary>
        /// Flag indicating whether or not to keep the window on top.
        /// </summary>
        public bool KeepWindowOnTop { get; set; } = false;

        /// <summary>
        /// Flag indicating window maximized state.
        /// </summary>
        public bool Maximized { get; set; } = false;
        /// <summary>
        /// Flag indicating whether or not the window operates in dark mode.
        /// </summary>
        public bool DarkMode { get; set; } = false;
        /// <summary>
        /// Last width of the console window.
        /// </summary>
        public double WindowWidth { get; set; } = MainWindow.MIN_WIDTH;
        /// <summary>
        /// Last height of the console window.
        /// </summary>
        public double WindowHeight { get; set; } = MainWindow.MIN_HEIGHT;
        /// <summary>
        /// Last width of the console canvas display area.
        /// </summary>
        public double CanvasWidth { get; set; } = MainWindow.MIN_WIDTH;
        /// <summary>
        /// Last height of the console canvas display area.
        /// </summary>
        public double CanvasHeight { get; set; } = MainWindow.MIN_HEIGHT;

        /// <summary>
        /// Full path to a user defined background image.
        /// </summary>
        public string UserBackgroundImage { get; set; } = null;

        /// <summary>
        /// Flag enabling trace logging.
        /// </summary>
        public bool SaveTraceLog { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public Keys GlobalPTTShortcut { get; set; } = Keys.None;
        /// <summary>
        /// 
        /// </summary>
        public bool GlobalPTTKeysAllChannels { get; set; }
        /// <summary>
        ///
        /// </summary>
        public bool TalkPermitTone { get; set; } = false;
        /// <summary>
        /// Flag indicating whether patch enabled state should be restored on startup.
        /// </summary>
        public bool RetainPatchStateOnStartup { get; set; } = false;
        /// <summary>
        /// Flag indicating whether selected channels should be restored on startup.
        /// </summary>
        public bool RestoreSelectedChannelsOnStartup { get; set; } = false;

        /// <summary>
        /// Saved list of selected channel names to restore on startup.
        /// </summary>
        public List<string> SelectedChannels { get; set; } = new List<string>();
        /// <summary>
        /// Saved list of active web stream names to restore on startup.
        /// </summary>
        public List<string> SelectedWebStreams { get; set; } = new List<string>();
        /// <summary>
        /// Saved call history window position and size.
        /// </summary>
        public WindowPlacementConfig CallHistoryWindowPlacement { get; set; } = new WindowPlacementConfig
        {
            Width = DEFAULT_CALL_HISTORY_WINDOW_WIDTH,
            Height = DEFAULT_CALL_HISTORY_WINDOW_HEIGHT
        };
        /// <summary>
        /// Saved call history column order, visibility, and widths.
        /// </summary>
        public List<GridColumnConfig> CallHistoryColumns { get; set; } = CreateDefaultCallHistoryColumns();
        /*
        ** Methods
        */

        /// <summary>
        /// Persisted floating window position and size.
        /// </summary>
        public class WindowPlacementConfig
        {
            public double? Left { get; set; }
            public double? Top { get; set; }
            public double Width { get; set; } = DEFAULT_CALL_HISTORY_WINDOW_WIDTH;
            public double Height { get; set; } = DEFAULT_CALL_HISTORY_WINDOW_HEIGHT;
        }

        /// <summary>
        /// Persisted data grid column display preferences.
        /// </summary>
        public class GridColumnConfig
        {
            public string Key { get; set; } = string.Empty;
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool Visible { get; set; } = true;
        }

        /// <summary>
        /// Import/export category shown in the settings transfer window.
        /// </summary>
        public class SettingsTransferCategoryDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> PropertyNames { get; set; } = new List<string>();
        }

        private class SettingsTransferFile
        {
            public string Format { get; set; } = SETTINGS_TRANSFER_FORMAT;
            public int Version { get; set; } = 1;
            public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
            public List<string> Categories { get; set; } = new List<string>();
            public JObject Settings { get; set; } = new JObject();
        }

        private static readonly List<SettingsTransferCategoryDefinition> SETTINGS_TRANSFER_CATEGORIES = new List<SettingsTransferCategoryDefinition>
        {
            new SettingsTransferCategoryDefinition
            {
                Id = "layout",
                DisplayName = "Console Layout",
                Description = "Resource, system status, alert tone, web stream, window, canvas, and background placement.",
                PropertyNames = new List<string>
                {
                    nameof(ChannelPositions),
                    nameof(SystemStatusPositions),
                    nameof(AlertTonePositions),
                    nameof(WebStreamPositions),
                    nameof(WindowWidth),
                    nameof(WindowHeight),
                    nameof(CanvasWidth),
                    nameof(CanvasHeight),
                    nameof(Maximized),
                    nameof(UserBackgroundImage)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "audio",
                DisplayName = "Audio Routing",
                Description = "Input device, master output, per-resource output overrides, AGC, RX mute, and saved volumes.",
                PropertyNames = new List<string>
                {
                    nameof(ChannelOutputDevices),
                    nameof(ChannelOutputDeviceKeys),
                    nameof(AudioInputDevice),
                    nameof(AudioInputDeviceKey),
                    nameof(MasterOutputDevice),
                    nameof(MasterOutputDeviceKey),
                    nameof(AudioInputAgcEnabled),
                    nameof(MuteRxAudioWhileTransmitting),
                    nameof(ChannelVolumes),
                    nameof(WebStreamVolumes)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "tar",
                DisplayName = "Talkgroup Audio Recorder",
                Description = "TAR recording folder, enabled TGs, retention, and ignored RID lists.",
                PropertyNames = new List<string>
                {
                    nameof(TarRecordingsRootPath),
                    nameof(TarChannelConfigs)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "groups",
                DisplayName = "Groups and Patches",
                Description = "Patch/multi-select memberships, one-way patch modes, and retained patch enabled state.",
                PropertyNames = new List<string>
                {
                    nameof(PatchGroupMemberships),
                    nameof(PatchGroupModes),
                    nameof(PatchGroupEnabledStates)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "alerts",
                DisplayName = "Custom Alert Tones",
                Description = "Custom tone list, labels, file paths, tab assignments, and tone positions.",
                PropertyNames = new List<string>
                {
                    nameof(AlertTones),
                    nameof(AlertToneFilePaths),
                    nameof(AlertToneTabs),
                    nameof(AlertTonePositions)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "clocks",
                DisplayName = "Toolbar Clocks",
                Description = "Enabled clocks, UTC offsets, colors, 12/24-hour mode, and seconds display.",
                PropertyNames = new List<string>
                {
                    nameof(ToolbarClockConfigs),
                    nameof(ToolbarClockConfigSlots),
                    nameof(ClockUse24HourTime),
                    nameof(ClockShowSeconds)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "startup",
                DisplayName = "Startup and Sticky State",
                Description = "Last codeplug path, restored selected channels/web streams, patch state retention, and startup toggles.",
                PropertyNames = new List<string>
                {
                    nameof(LastCodeplugPath),
                    nameof(RestoreSelectedChannelsOnStartup),
                    nameof(SelectedChannels),
                    nameof(SelectedWebStreams),
                    nameof(RetainPatchStateOnStartup)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "preferences",
                DisplayName = "Operator Preferences",
                Description = "Theme, widget visibility, PTT mode, lock widgets, talk permit tone, always-on-top, and trace logging.",
                PropertyNames = new List<string>
                {
                    nameof(ShowSystemStatus),
                    nameof(ShowChannels),
                    nameof(ShowAlertTones),
                    nameof(TogglePTTMode),
                    nameof(LockWidgets),
                    nameof(SnapCallHistoryToWindow),
                    nameof(KeepWindowOnTop),
                    nameof(DarkMode),
                    nameof(TalkPermitTone),
                    nameof(SaveTraceLog)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "call-history",
                DisplayName = "Call History Window",
                Description = "Call history window size, position, column order, and visible columns.",
                PropertyNames = new List<string>
                {
                    nameof(CallHistoryWindowPlacement),
                    nameof(CallHistoryColumns),
                    nameof(SnapCallHistoryToWindow)
                }
            },
            new SettingsTransferCategoryDefinition
            {
                Id = "keys-security",
                DisplayName = "Keybinds and Selectable Encryption",
                Description = "Global PTT keybind and per-resource selectable encryption state.",
                PropertyNames = new List<string>
                {
                    nameof(GlobalPTTShortcut),
                    nameof(SelectableEncryptionStates)
                }
            }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsManager"/> class.
        /// </summary>
        public SettingsManager()
        {
            _instance = this;
        }

        /// <summary>
        /// Load user settings.
        /// </summary>
        public bool LoadSettings()
        {
            // was the user profile path being overridden?
            if (App.USER_PROFILE_PATH_OVERRIDE != string.Empty)
            {
                UserAppDataPath = App.USER_PROFILE_PATH_OVERRIDE;
                SettingsFilePath = UserAppDataPath + Path.DirectorySeparatorChar + "UserSettings.json";
            }
            else
            {
                if (!Directory.Exists(UserAppDataPath))
                    Directory.CreateDirectory(UserAppDataPath);
            }

            if (!File.Exists(SettingsFilePath))
                return false;

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                SettingsManager loadedSettings = JsonConvert.DeserializeObject<SettingsManager>(json);
                _instance = this;

                if (loadedSettings != null)
                {
                    GlobalPTTKeysAllChannels = loadedSettings.GlobalPTTKeysAllChannels;
                    ShowSystemStatus = loadedSettings.ShowSystemStatus;
                    ShowChannels = loadedSettings.ShowChannels;
                    ShowAlertTones = loadedSettings.ShowAlertTones;
                    LastCodeplugPath = loadedSettings.LastCodeplugPath;
                    ChannelPositions = loadedSettings.ChannelPositions ?? new Dictionary<string, ChannelPosition>();
                    SystemStatusPositions = loadedSettings.SystemStatusPositions ?? new Dictionary<string, ChannelPosition>();
                    AlertToneFilePaths = loadedSettings.AlertToneFilePaths ?? new List<string>();
                    AlertTonePositions = loadedSettings.AlertTonePositions ?? new Dictionary<string, ChannelPosition>();
                    WebStreamPositions = loadedSettings.WebStreamPositions ?? new Dictionary<string, ChannelPosition>();
                    AlertToneTabs = loadedSettings.AlertToneTabs ?? new Dictionary<string, string>();
                    AlertTones = loadedSettings.AlertTones ?? new List<AlertToneConfig>();
                    ChannelOutputDevices = loadedSettings.ChannelOutputDevices ?? new Dictionary<string, int>();
                    ChannelOutputDeviceKeys = loadedSettings.ChannelOutputDeviceKeys ?? new Dictionary<string, string>();
                    AudioInputDevice = NormalizeAudioDeviceIndex(loadedSettings.AudioInputDevice);
                    AudioInputDeviceKey = NormalizeAudioDeviceKey(loadedSettings.AudioInputDeviceKey);
                    MasterOutputDevice = NormalizeAudioDeviceIndex(loadedSettings.MasterOutputDevice);
                    MasterOutputDeviceKey = NormalizeAudioDeviceKey(loadedSettings.MasterOutputDeviceKey);
                    AudioInputAgcEnabled = loadedSettings.AudioInputAgcEnabled;
                    MuteRxAudioWhileTransmitting = loadedSettings.MuteRxAudioWhileTransmitting;
                    MigrateLegacyAudioSettings();
                    ChannelVolumes = loadedSettings.ChannelVolumes ?? new Dictionary<string, double>();
                    SelectableEncryptionStates = loadedSettings.SelectableEncryptionStates ?? new Dictionary<string, bool>();
                    WebStreamVolumes = loadedSettings.WebStreamVolumes ?? new Dictionary<string, double>();
                    TarRecordingsRootPath = string.IsNullOrWhiteSpace(loadedSettings.TarRecordingsRootPath)
                        ? DefaultTarRecordingsPath
                        : loadedSettings.TarRecordingsRootPath.Trim();
                    TarChannelConfigs = loadedSettings.TarChannelConfigs ?? new Dictionary<string, TarChannelConfig>();
                    ToolbarClockConfigSlots = NormalizeToolbarClockConfigSlots(
                        loadedSettings.ToolbarClockConfigSlots,
                        loadedSettings.ToolbarClockConfigs);
                    ToolbarClockConfigs = ToolbarClockConfigSlotsToList(ToolbarClockConfigSlots);
                    ClockUse24HourTime = loadedSettings.ClockUse24HourTime;
                    ClockShowSeconds = loadedSettings.ClockShowSeconds;
                    PatchGroupMemberships = loadedSettings.PatchGroupMemberships ?? new Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>>();
                    PatchGroupModes = loadedSettings.PatchGroupModes ?? new Dictionary<string, Dictionary<string, bool>>();
                    PatchGroupEnabledStates = loadedSettings.PatchGroupEnabledStates ?? new Dictionary<string, Dictionary<string, bool>>();
                    TogglePTTMode = loadedSettings.TogglePTTMode;
                    LockWidgets = loadedSettings.LockWidgets;
                    SnapCallHistoryToWindow = loadedSettings.SnapCallHistoryToWindow;
                    KeepWindowOnTop = loadedSettings.KeepWindowOnTop;
                    TalkPermitTone = loadedSettings.TalkPermitTone;
                    RetainPatchStateOnStartup = loadedSettings.RetainPatchStateOnStartup;
                    Maximized = loadedSettings.Maximized;
                    DarkMode = loadedSettings.DarkMode;
                    WindowWidth = loadedSettings.WindowWidth;
                    if (WindowWidth == 0)
                        WindowWidth = MainWindow.MIN_WIDTH;
                    WindowHeight = loadedSettings.WindowHeight;
                    if (WindowHeight == 0)
                        WindowHeight = MainWindow.MIN_HEIGHT;
                    CanvasWidth = loadedSettings.CanvasWidth;
                    if (CanvasWidth == 0)
                        CanvasWidth = MainWindow.MIN_WIDTH;
                    CanvasHeight = loadedSettings.CanvasHeight;
                    if (CanvasHeight == 0)
                        CanvasHeight = MainWindow.MIN_HEIGHT;

                    if (CanvasWidth < WindowWidth)
                        CanvasWidth = WindowWidth;
                    if (CanvasHeight < WindowHeight)
                        CanvasHeight = WindowHeight;

                    UserBackgroundImage = loadedSettings.UserBackgroundImage;

                    SaveTraceLog = loadedSettings.SaveTraceLog;
                    GlobalPTTShortcut = loadedSettings.GlobalPTTShortcut;
                    RestoreSelectedChannelsOnStartup = loadedSettings.RestoreSelectedChannelsOnStartup;
                    SelectedChannels = loadedSettings.SelectedChannels ?? new List<string>();
                    SelectedWebStreams = loadedSettings.SelectedWebStreams ?? new List<string>();
                    CallHistoryWindowPlacement = NormalizeWindowPlacement(
                        loadedSettings.CallHistoryWindowPlacement,
                        DEFAULT_CALL_HISTORY_WINDOW_WIDTH,
                        DEFAULT_CALL_HISTORY_WINDOW_HEIGHT);
                    CallHistoryColumns = NormalizeCallHistoryColumns(loadedSettings.CallHistoryColumns);

                    if (AlertTones == null || AlertTones.Count == 0)
                        AlertTones = MigrateLegacyAlertTones();

                    SyncLegacyAlertToneState();

                    if (SaveTraceLog)
                        Log.SetupTextWriter(Environment.CurrentDirectory, "dvmconsole.log");

                    Assembly asm = Assembly.GetExecutingAssembly();
#if DEBUG
                    SemVersion _SEM_VERSION = new SemVersion(asm, "DEBUG_FACTORY_LABTOOL");
#else
                    SemVersion _SEM_VERSION = new SemVersion(asm);
#endif

                    AssemblyProductAttribute asmProd = asm.GetCustomAttributes(typeof(AssemblyProductAttribute), false)[0] as AssemblyProductAttribute;
                    AssemblyCopyrightAttribute asmCopyright = asm.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0] as AssemblyCopyrightAttribute;
                    DateTime buildDate = new DateTime(2000, 1, 1).AddDays(asm.GetName().Version.Build).AddSeconds(asm.GetName().Version.Revision * 2);

                    Log.WriteLine($"{asmProd.Product} {_SEM_VERSION.ToString()} (Built: {buildDate.ToShortDateString() + " at " + buildDate.ToShortTimeString()})");
                    Log.WriteLine($"{asmCopyright.Copyright}");
                    Log.WriteLine(">> Desktop Dispatch Console");

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _instance = this;
                Log.WriteLine($"Error loading settings: {ex.Message}");
                Log.StackTrace(ex, false);
                return false;
            }
        }

        /// <summary>
        /// Save user settings.
        /// </summary>
        public void SaveSettings()
        {
            _instance = this;

            if (!Directory.Exists(UserAppDataPath))
                Directory.CreateDirectory(UserAppDataPath);

            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving settings: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Saves call history window geometry and column preferences.
        /// </summary>
        /// <param name="placement"></param>
        /// <param name="columns"></param>
        public void SaveCallHistoryWindowSettings(WindowPlacementConfig placement, IEnumerable<GridColumnConfig> columns)
        {
            CallHistoryWindowPlacement = NormalizeWindowPlacement(
                placement,
                DEFAULT_CALL_HISTORY_WINDOW_WIDTH,
                DEFAULT_CALL_HISTORY_WINDOW_HEIGHT);
            CallHistoryColumns = NormalizeCallHistoryColumns(columns);
            SaveSettings();
        }

        private static WindowPlacementConfig NormalizeWindowPlacement(WindowPlacementConfig placement, double defaultWidth, double defaultHeight)
        {
            double width = placement?.Width ?? defaultWidth;
            double height = placement?.Height ?? defaultHeight;

            return new WindowPlacementConfig
            {
                Left = IsFinite(placement?.Left) ? placement.Left : null,
                Top = IsFinite(placement?.Top) ? placement.Top : null,
                Width = width > 0 ? width : defaultWidth,
                Height = height > 0 ? height : defaultHeight
            };
        }

        private static List<GridColumnConfig> CreateDefaultCallHistoryColumns()
        {
            return new List<GridColumnConfig>
            {
                new GridColumnConfig { Key = "Timestamp", DisplayIndex = 0, Width = 120, Visible = true },
                new GridColumnConfig { Key = "Channel", DisplayIndex = 1, Width = 150, Visible = true },
                new GridColumnConfig { Key = "RidAlias", DisplayIndex = 2, Width = 160, Visible = true },
                new GridColumnConfig { Key = "Rid", DisplayIndex = 3, Width = 90, Visible = true },
                new GridColumnConfig { Key = "Tgid", DisplayIndex = 4, Width = 80, Visible = true }
            };
        }

        private static List<GridColumnConfig> NormalizeCallHistoryColumns(IEnumerable<GridColumnConfig> columns)
        {
            Dictionary<string, GridColumnConfig> savedColumns = (columns ?? Enumerable.Empty<GridColumnConfig>())
                .Where(column => !string.IsNullOrWhiteSpace(column?.Key))
                .GroupBy(column => column.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            List<GridColumnConfig> defaults = CreateDefaultCallHistoryColumns();
            List<GridColumnConfig> normalized = new List<GridColumnConfig>();
            foreach (GridColumnConfig defaultColumn in defaults)
            {
                if (!savedColumns.TryGetValue(defaultColumn.Key, out GridColumnConfig savedColumn))
                {
                    normalized.Add(defaultColumn);
                    continue;
                }

                normalized.Add(new GridColumnConfig
                {
                    Key = defaultColumn.Key,
                    DisplayIndex = savedColumn.DisplayIndex,
                    Width = savedColumn.Width > 0 ? savedColumn.Width : defaultColumn.Width,
                    Visible = savedColumn.Visible
                });
            }

            normalized = normalized
                .OrderBy(column => column.DisplayIndex)
                .Select((column, index) => new GridColumnConfig
                {
                    Key = column.Key,
                    DisplayIndex = index,
                    Width = column.Width,
                    Visible = column.Visible
                })
                .ToList();

            return normalized;
        }

        private static bool IsFinite(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        public static IReadOnlyList<SettingsTransferCategoryDefinition> GetSettingsTransferCategories()
        {
            return SETTINGS_TRANSFER_CATEGORIES;
        }

        public void ExportSettingsTransfer(string filePath, IEnumerable<string> categoryIds)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Export path is required.", nameof(filePath));

            List<SettingsTransferCategoryDefinition> selectedCategories = ResolveTransferCategories(categoryIds).ToList();
            if (selectedCategories.Count == 0)
                throw new InvalidOperationException("Select at least one settings category to export.");

            JObject settingsPayload = new JObject();
            foreach (string propertyName in selectedCategories.SelectMany(c => c.PropertyNames).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                PropertyInfo property = typeof(SettingsManager).GetProperty(propertyName);
                if (property == null || !property.CanRead)
                    continue;

                object value = property.GetValue(this);
                settingsPayload[propertyName] = value == null
                    ? JValue.CreateNull()
                    : JToken.FromObject(value);
            }

            SettingsTransferFile transferFile = new SettingsTransferFile
            {
                ExportedUtc = DateTime.UtcNow,
                Categories = selectedCategories.Select(c => c.Id).ToList(),
                Settings = settingsPayload
            };

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, JsonConvert.SerializeObject(transferFile, Formatting.Indented));
        }

        public List<string> ImportSettingsTransfer(string filePath, IEnumerable<string> categoryIds)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Import path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Settings transfer file was not found.", filePath);

            SettingsTransferFile transferFile = JsonConvert.DeserializeObject<SettingsTransferFile>(File.ReadAllText(filePath));
            if (transferFile == null || transferFile.Settings == null)
                throw new InvalidOperationException("The selected file is not a valid settings transfer file.");
            if (!string.Equals(transferFile.Format, SETTINGS_TRANSFER_FORMAT, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected file is not a dvmconsole settings transfer file.");

            HashSet<string> exportedCategories = new HashSet<string>(
                transferFile.Categories ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            List<SettingsTransferCategoryDefinition> selectedCategories = ResolveTransferCategories(categoryIds)
                .Where(category => exportedCategories.Count == 0 || exportedCategories.Contains(category.Id))
                .ToList();

            if (selectedCategories.Count == 0)
                throw new InvalidOperationException("None of the selected categories exist in this transfer file.");

            foreach (string propertyName in selectedCategories.SelectMany(c => c.PropertyNames).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!transferFile.Settings.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out JToken token))
                    continue;

                PropertyInfo property = typeof(SettingsManager).GetProperty(propertyName);
                if (property == null || !property.CanWrite)
                    continue;

                object value = ConvertSettingsTransferToken(token, property.PropertyType);
                property.SetValue(this, value);
            }

            NormalizeImportedSettings();
            SaveSettings();
            return selectedCategories.Select(c => c.DisplayName).ToList();
        }

        private static IEnumerable<SettingsTransferCategoryDefinition> ResolveTransferCategories(IEnumerable<string> categoryIds)
        {
            HashSet<string> selectedIds = new HashSet<string>(
                categoryIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (selectedIds.Count == 0)
                yield break;

            foreach (SettingsTransferCategoryDefinition category in SETTINGS_TRANSFER_CATEGORIES)
            {
                if (selectedIds.Contains(category.Id))
                    yield return category;
            }
        }

        private static object ConvertSettingsTransferToken(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                Type nullableType = Nullable.GetUnderlyingType(targetType);
                if (!targetType.IsValueType || nullableType != null)
                    return null;

                return Activator.CreateInstance(targetType);
            }

            return token.ToObject(targetType);
        }

        private void NormalizeImportedSettings()
        {
            ChannelPositions ??= new Dictionary<string, ChannelPosition>();
            SystemStatusPositions ??= new Dictionary<string, ChannelPosition>();
            AlertToneFilePaths ??= new List<string>();
            AlertTonePositions ??= new Dictionary<string, ChannelPosition>();
            WebStreamPositions ??= new Dictionary<string, ChannelPosition>();
            AlertToneTabs ??= new Dictionary<string, string>();
            AlertTones ??= new List<AlertToneConfig>();
            ChannelOutputDevices ??= new Dictionary<string, int>();
            ChannelOutputDeviceKeys ??= new Dictionary<string, string>();
            ChannelVolumes ??= new Dictionary<string, double>();
            SelectableEncryptionStates ??= new Dictionary<string, bool>();
            WebStreamVolumes ??= new Dictionary<string, double>();
            TarChannelConfigs ??= new Dictionary<string, TarChannelConfig>();
            PatchGroupMemberships ??= new Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>>();
            PatchGroupModes ??= new Dictionary<string, Dictionary<string, bool>>();
            PatchGroupEnabledStates ??= new Dictionary<string, Dictionary<string, bool>>();
            SelectedChannels ??= new List<string>();
            SelectedWebStreams ??= new List<string>();

            AudioInputDevice = NormalizeAudioDeviceIndex(AudioInputDevice);
            AudioInputDeviceKey = NormalizeAudioDeviceKey(AudioInputDeviceKey);
            MasterOutputDevice = NormalizeAudioDeviceIndex(MasterOutputDevice);
            MasterOutputDeviceKey = NormalizeAudioDeviceKey(MasterOutputDeviceKey);
            MigrateLegacyAudioSettings();

            TarRecordingsRootPath = string.IsNullOrWhiteSpace(TarRecordingsRootPath)
                ? DefaultTarRecordingsPath
                : TarRecordingsRootPath.Trim();
            ToolbarClockConfigSlots = NormalizeToolbarClockConfigSlots(ToolbarClockConfigSlots, ToolbarClockConfigs);
            ToolbarClockConfigs = ToolbarClockConfigSlotsToList(ToolbarClockConfigSlots);
            CallHistoryWindowPlacement = NormalizeWindowPlacement(
                CallHistoryWindowPlacement,
                DEFAULT_CALL_HISTORY_WINDOW_WIDTH,
                DEFAULT_CALL_HISTORY_WINDOW_HEIGHT);
            CallHistoryColumns = NormalizeCallHistoryColumns(CallHistoryColumns);

            if (AlertTones.Count == 0)
                AlertTones = MigrateLegacyAlertTones();
            SyncLegacyAlertToneState();
        }

        /// <summary>
        /// Reset user settings.
        /// </summary>
        public void Reset()
        {
            if (File.Exists(SettingsFilePath))
                File.Delete(SettingsFilePath);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newFilePath"></param>
        public void UpdateAlertTonePaths(string newFilePath)
        {
            if (!AlertToneFilePaths.Contains(newFilePath))
            {
                AlertToneFilePaths.Add(newFilePath);
                SaveSettings();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alertFileName"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void UpdateAlertTonePosition(string alertFileName, double x, double y)
        {
            AlertTonePositions[alertFileName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        /// <summary>
        /// Returns a normalized copy of persisted alert tone configs.
        /// </summary>
        public List<AlertToneConfig> GetAlertToneConfigs()
        {
            if (AlertTones == null || AlertTones.Count == 0)
                AlertTones = MigrateLegacyAlertTones();

            List<AlertToneConfig> normalized = AlertTones
                .Where(t => !string.IsNullOrWhiteSpace(t?.FilePath))
                .Select(t => new AlertToneConfig
                {
                    Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N") : t.Id,
                    DisplayName = string.IsNullOrWhiteSpace(t.DisplayName) ? Path.GetFileNameWithoutExtension(t.FilePath) : t.DisplayName,
                    FilePath = t.FilePath,
                    TabName = t.TabName ?? string.Empty,
                    Position = t.Position ?? new ChannelPosition { X = 20, Y = 20 }
                })
                .ToList();

            AlertTones = normalized
                .Select(t => new AlertToneConfig
                {
                    Id = t.Id,
                    DisplayName = t.DisplayName,
                    FilePath = t.FilePath,
                    TabName = t.TabName,
                    Position = t.Position
                })
                .ToList();
            SyncLegacyAlertToneState();

            return normalized
                .Select(t => new AlertToneConfig
                {
                    Id = t.Id,
                    DisplayName = t.DisplayName,
                    FilePath = t.FilePath,
                    TabName = t.TabName,
                    Position = t.Position
                })
                .ToList();
        }

        /// <summary>
        /// Saves the current alert tone configurations and keeps legacy fields in sync.
        /// </summary>
        public void SaveAlertToneConfigs(IEnumerable<AlertToneConfig> configs)
        {
            AlertTones = (configs ?? Enumerable.Empty<AlertToneConfig>())
                .Where(t => !string.IsNullOrWhiteSpace(t?.FilePath))
                .Select(t => new AlertToneConfig
                {
                    Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N") : t.Id,
                    DisplayName = string.IsNullOrWhiteSpace(t.DisplayName) ? Path.GetFileNameWithoutExtension(t.FilePath) : t.DisplayName.Trim(),
                    FilePath = t.FilePath.Trim(),
                    TabName = t.TabName?.Trim() ?? string.Empty,
                    Position = t.Position ?? new ChannelPosition { X = 20, Y = 20 }
                })
                .ToList();

            SyncLegacyAlertToneState();
            SaveSettings();
        }

        /// <summary>
        /// Updates an alert tone position by stable config ID.
        /// </summary>
        public void UpdateAlertTonePositionById(string alertToneId, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(alertToneId))
                return;

            AlertToneConfig tone = GetAlertToneConfigs()
                .FirstOrDefault(t => string.Equals(t.Id, alertToneId, StringComparison.OrdinalIgnoreCase));
            if (tone == null)
                return;

            tone.Position = new ChannelPosition { X = x, Y = y };
            SaveAlertToneConfigs(GetAlertToneConfigs()
                .Select(t => string.Equals(t.Id, tone.Id, StringComparison.OrdinalIgnoreCase) ? tone : t));
        }

        private List<AlertToneConfig> MigrateLegacyAlertTones()
        {
            return (AlertToneFilePaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new AlertToneConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    TabName = AlertToneTabs.TryGetValue(path, out string tabName) ? tabName : string.Empty,
                    Position = AlertTonePositions.TryGetValue(path, out ChannelPosition position)
                        ? position
                        : new ChannelPosition { X = 20, Y = 20 }
                })
                .ToList();
        }

        private void SyncLegacyAlertToneState()
        {
            AlertToneFilePaths = AlertTones
                .Where(t => !string.IsNullOrWhiteSpace(t?.FilePath))
                .Select(t => t.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            AlertTonePositions = AlertTones
                .Where(t => !string.IsNullOrWhiteSpace(t?.FilePath))
                .GroupBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Position ?? new ChannelPosition { X = 20, Y = 20 },
                    StringComparer.OrdinalIgnoreCase);

            AlertToneTabs = AlertTones
                .Where(t => !string.IsNullOrWhiteSpace(t?.FilePath))
                .GroupBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().TabName ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a normalized copy of toolbar clock configuration rows.
        /// </summary>
        public List<ToolbarClockConfig> GetToolbarClockConfigs()
        {
            ToolbarClockConfigSlots = NormalizeToolbarClockConfigSlots(ToolbarClockConfigSlots, ToolbarClockConfigs);
            ToolbarClockConfigs = ToolbarClockConfigSlotsToList(ToolbarClockConfigSlots);
            return CopyToolbarClockConfigs(ToolbarClockConfigs);
        }

        /// <summary>
        /// Saves toolbar clock configuration rows.
        /// </summary>
        public void SaveToolbarClockConfigs(IEnumerable<ToolbarClockConfig> configs)
        {
            ToolbarClockConfigs = NormalizeToolbarClockConfigs(configs);
            ToolbarClockConfigSlots = ToolbarClockConfigsToSlots(ToolbarClockConfigs);
            SaveSettings();
        }

        /// <summary>
        /// Saves all toolbar clock display settings.
        /// </summary>
        public void SaveToolbarClockSettings(IEnumerable<ToolbarClockConfig> configs, bool use24HourTime, bool showSeconds)
        {
            ToolbarClockConfigs = NormalizeToolbarClockConfigs(configs);
            ToolbarClockConfigSlots = ToolbarClockConfigsToSlots(ToolbarClockConfigs);
            ClockUse24HourTime = use24HourTime;
            ClockShowSeconds = showSeconds;
            SaveSettings();
        }

        private static List<ToolbarClockConfig> CreateDefaultToolbarClockConfigs()
        {
            return Enumerable.Range(0, MAX_TOOLBAR_CLOCKS)
                .Select(_ => new ToolbarClockConfig())
                .ToList();
        }

        private static Dictionary<string, ToolbarClockConfig> CreateDefaultToolbarClockConfigSlots()
        {
            return ToolbarClockConfigsToSlots(CreateDefaultToolbarClockConfigs());
        }

        private static List<ToolbarClockConfig> NormalizeToolbarClockConfigs(IEnumerable<ToolbarClockConfig> configs)
        {
            List<ToolbarClockConfig> normalized = (configs ?? Enumerable.Empty<ToolbarClockConfig>())
                .Take(MAX_TOOLBAR_CLOCKS)
                .Select(config => new ToolbarClockConfig
                {
                    Enabled = config?.Enabled == true,
                    UtcOffsetHours = NormalizeToolbarClockOffset(config?.UtcOffsetHours ?? 0),
                    ColorHex = NormalizeToolbarClockColor(config?.ColorHex)
                })
                .ToList();

            while (normalized.Count < MAX_TOOLBAR_CLOCKS)
                normalized.Add(new ToolbarClockConfig());

            return normalized;
        }

        private static Dictionary<string, ToolbarClockConfig> NormalizeToolbarClockConfigSlots(
            IDictionary<string, ToolbarClockConfig> slots,
            IEnumerable<ToolbarClockConfig> fallbackConfigs)
        {
            Dictionary<string, ToolbarClockConfig> normalized = new Dictionary<string, ToolbarClockConfig>(StringComparer.OrdinalIgnoreCase);
            List<ToolbarClockConfig> fallback = NormalizeToolbarClockConfigs(fallbackConfigs);

            for (int i = 0; i < MAX_TOOLBAR_CLOCKS; i++)
            {
                string slotKey = (i + 1).ToString();
                ToolbarClockConfig source = null;

                if (slots != null && slots.TryGetValue(slotKey, out ToolbarClockConfig slotConfig))
                    source = slotConfig;
                else if (i < fallback.Count)
                    source = fallback[i];

                normalized[slotKey] = NormalizeToolbarClockConfig(source);
            }

            return normalized;
        }

        private static Dictionary<string, ToolbarClockConfig> ToolbarClockConfigsToSlots(IEnumerable<ToolbarClockConfig> configs)
        {
            List<ToolbarClockConfig> normalized = NormalizeToolbarClockConfigs(configs);
            Dictionary<string, ToolbarClockConfig> slots = new Dictionary<string, ToolbarClockConfig>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < MAX_TOOLBAR_CLOCKS; i++)
                slots[(i + 1).ToString()] = NormalizeToolbarClockConfig(normalized.ElementAtOrDefault(i));

            return slots;
        }

        private static List<ToolbarClockConfig> ToolbarClockConfigSlotsToList(IDictionary<string, ToolbarClockConfig> slots)
        {
            return Enumerable.Range(1, MAX_TOOLBAR_CLOCKS)
                .Select(slot => slots != null && slots.TryGetValue(slot.ToString(), out ToolbarClockConfig config)
                    ? NormalizeToolbarClockConfig(config)
                    : new ToolbarClockConfig())
                .ToList();
        }

        private static List<ToolbarClockConfig> CopyToolbarClockConfigs(IEnumerable<ToolbarClockConfig> configs)
        {
            return NormalizeToolbarClockConfigs(configs)
                .Select(NormalizeToolbarClockConfig)
                .ToList();
        }

        private static ToolbarClockConfig NormalizeToolbarClockConfig(ToolbarClockConfig config)
        {
            return new ToolbarClockConfig
            {
                Enabled = config?.Enabled == true,
                UtcOffsetHours = NormalizeToolbarClockOffset(config?.UtcOffsetHours ?? 0),
                ColorHex = NormalizeToolbarClockColor(config?.ColorHex)
            };
        }

        private static int NormalizeToolbarClockOffset(int offsetHours)
        {
            return Math.Max(-12, Math.Min(14, offsetHours));
        }

        private static string NormalizeToolbarClockColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
                return DEFAULT_TOOLBAR_CLOCK_COLOR;

            string trimmed = colorHex.Trim();
            if (!trimmed.StartsWith("#") || (trimmed.Length != 7 && trimmed.Length != 9))
                return DEFAULT_TOOLBAR_CLOCK_COLOR;

            return trimmed.ToUpperInvariant();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void UpdateChannelPosition(string channelName, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            ChannelPositions[channelName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        /// <summary>
        /// Gets a saved channel position by stable resource key with legacy channel-name fallback.
        /// </summary>
        public bool TryGetChannelPosition(string resourceKey, string legacyChannelName, out ChannelPosition position)
        {
            position = null;

            if (ChannelPositions == null)
                return false;

            if (!string.IsNullOrWhiteSpace(resourceKey) &&
                ChannelPositions.TryGetValue(resourceKey, out position))
                return true;

            if (!string.IsNullOrWhiteSpace(legacyChannelName) &&
                ChannelPositions.TryGetValue(legacyChannelName, out position))
                return true;

            return false;
        }

        /// <summary>
        /// Saves a web stream chip position.
        /// </summary>
        public void UpdateWebStreamPosition(string streamName, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(streamName))
                return;

            WebStreamPositions[streamName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        /// <summary>
        /// Saves a web stream chip volume.
        /// </summary>
        public void UpdateWebStreamVolume(string streamName, double volume)
        {
            if (string.IsNullOrWhiteSpace(streamName))
                return;

            WebStreamVolumes[streamName] = Math.Max(0.0, Math.Min(4.0, volume));
            SaveSettings();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="systemName"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void UpdateSystemStatusPosition(string systemName, double x, double y)
        {
            SystemStatusPositions[systemName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="deviceIndex"></param>
        public void UpdateChannelOutputDevice(string channelName, int deviceIndex, string deviceKey = null)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            ChannelOutputDevices[channelName] = NormalizeAudioDeviceIndex(deviceIndex);
            ChannelOutputDeviceKeys[channelName] = NormalizeAudioDeviceKey(
                string.IsNullOrWhiteSpace(deviceKey)
                    ? AudioDeviceResolver.GetOutputDeviceKey(deviceIndex)
                    : deviceKey);
            SaveSettings();
        }

        public void RemoveChannelOutputDevice(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            bool removed = ChannelOutputDevices.Remove(channelName);
            removed |= ChannelOutputDeviceKeys.Remove(channelName);
            if (removed)
                SaveSettings();
        }

        public void UpdateAudioInputDevice(int deviceIndex, string deviceKey = null)
        {
            AudioInputDevice = NormalizeAudioDeviceIndex(deviceIndex);
            AudioInputDeviceKey = NormalizeAudioDeviceKey(
                string.IsNullOrWhiteSpace(deviceKey)
                    ? AudioDeviceResolver.GetInputDeviceKey(deviceIndex)
                    : deviceKey);
            SaveSettings();
        }

        public void UpdateMasterOutputDevice(int deviceIndex, string deviceKey = null)
        {
            MasterOutputDevice = NormalizeAudioDeviceIndex(deviceIndex);
            MasterOutputDeviceKey = NormalizeAudioDeviceKey(
                string.IsNullOrWhiteSpace(deviceKey)
                    ? AudioDeviceResolver.GetOutputDeviceKey(deviceIndex)
                    : deviceKey);
            SaveSettings();
        }

        public static int NormalizeAudioDeviceIndex(int deviceIndex)
        {
            return deviceIndex < WINDOWS_DEFAULT_AUDIO_DEVICE ? WINDOWS_DEFAULT_AUDIO_DEVICE : deviceIndex;
        }

        public static string NormalizeAudioDeviceKey(string deviceKey)
        {
            return string.IsNullOrWhiteSpace(deviceKey)
                ? AudioDeviceResolver.WINDOWS_DEFAULT_DEVICE_KEY
                : deviceKey.Trim();
        }

        private void MigrateLegacyAudioSettings()
        {
            if (ChannelOutputDevices == null)
                ChannelOutputDevices = new Dictionary<string, int>();
            if (ChannelOutputDeviceKeys == null)
                ChannelOutputDeviceKeys = new Dictionary<string, string>();

            if (AudioDeviceResolver.IsWindowsDefault(AudioInputDeviceKey) && AudioInputDevice != WINDOWS_DEFAULT_AUDIO_DEVICE)
                AudioInputDeviceKey = NormalizeAudioDeviceKey(AudioDeviceResolver.GetInputDeviceKey(AudioInputDevice));

            if (AudioDeviceResolver.IsWindowsDefault(MasterOutputDeviceKey) && MasterOutputDevice != WINDOWS_DEFAULT_AUDIO_DEVICE)
                MasterOutputDeviceKey = NormalizeAudioDeviceKey(AudioDeviceResolver.GetOutputDeviceKey(MasterOutputDevice));

            foreach (KeyValuePair<string, int> kvp in ChannelOutputDevices.ToList())
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || ChannelOutputDeviceKeys.ContainsKey(kvp.Key))
                    continue;

                string deviceKey = AudioDeviceResolver.GetOutputDeviceKey(kvp.Value);
                if (!string.IsNullOrWhiteSpace(deviceKey))
                    ChannelOutputDeviceKeys[kvp.Key] = NormalizeAudioDeviceKey(deviceKey);
            }

            if (ChannelOutputDevices.TryGetValue(LEGACY_GLOBAL_INPUT_DEVICE_KEY, out int legacyInputDevice))
            {
                AudioInputDevice = NormalizeAudioDeviceIndex(legacyInputDevice);
                AudioInputDeviceKey = NormalizeAudioDeviceKey(AudioDeviceResolver.GetInputDeviceKey(AudioInputDevice));
                ChannelOutputDevices.Remove(LEGACY_GLOBAL_INPUT_DEVICE_KEY);
            }
        }

        /// <summary>
        /// Saves a per-channel volume level.
        /// </summary>
        public void UpdateChannelVolume(string channelName, double volume)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            ChannelVolumes[channelName] = volume;
            SaveSettings();
        }

        /// <summary>
        /// Gets a saved channel volume by stable resource key with legacy channel-name fallback.
        /// </summary>
        public bool TryGetChannelVolume(string resourceKey, string legacyChannelName, out double volume)
        {
            volume = 0;

            if (ChannelVolumes == null)
                return false;

            if (!string.IsNullOrWhiteSpace(resourceKey) &&
                ChannelVolumes.TryGetValue(resourceKey, out volume))
                return true;

            if (!string.IsNullOrWhiteSpace(legacyChannelName) &&
                ChannelVolumes.TryGetValue(legacyChannelName, out volume))
                return true;

            return false;
        }

        /// <summary>
        /// Returns the saved selectable encryption state for a system/talkgroup pair.
        /// </summary>
        public bool GetSelectableEncryptionState(string stateKey, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(stateKey) || SelectableEncryptionStates == null)
                return defaultValue;

            return SelectableEncryptionStates.TryGetValue(stateKey, out bool enabled)
                ? enabled
                : defaultValue;
        }

        /// <summary>
        /// Saves the selectable encryption state for a system/talkgroup pair.
        /// </summary>
        public void UpdateSelectableEncryptionState(string stateKey, bool encrypted)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
                return;

            SelectableEncryptionStates ??= new Dictionary<string, bool>();
            SelectableEncryptionStates[stateKey] = encrypted;
            SaveSettings();
        }

        /// <summary>
        /// Returns a normalized copy of the TAR channel configuration map.
        /// </summary>
        public Dictionary<string, TarChannelConfig> GetTarChannelConfigs()
        {
            Dictionary<string, TarChannelConfig> copy = new Dictionary<string, TarChannelConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, TarChannelConfig> kvp in TarChannelConfigs ?? new Dictionary<string, TarChannelConfig>())
            {
                string configKey = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(configKey))
                    continue;

                copy[configKey] = NormalizeTarChannelConfig(kvp.Value);
            }

            return copy;
        }

        /// <summary>
        /// Gets TAR settings for a resource key, returning defaults if no saved entry exists.
        /// </summary>
        public TarChannelConfig GetTarChannelConfig(string configKey, string legacyChannelName = null, string legacyTalkgroupId = null)
        {
            if (string.IsNullOrWhiteSpace(configKey) && string.IsNullOrWhiteSpace(legacyChannelName) && string.IsNullOrWhiteSpace(legacyTalkgroupId))
                return NormalizeTarChannelConfig(null);

            if (TarChannelConfigs != null &&
                !string.IsNullOrWhiteSpace(configKey) &&
                TarChannelConfigs.TryGetValue(configKey, out TarChannelConfig config))
                return NormalizeTarChannelConfig(config);

            if (TarChannelConfigs != null &&
                !string.IsNullOrWhiteSpace(legacyTalkgroupId) &&
                TarChannelConfigs.TryGetValue(legacyTalkgroupId, out TarChannelConfig legacyTalkgroupConfig))
                return NormalizeTarChannelConfig(legacyTalkgroupConfig);

            if (TarChannelConfigs != null &&
                !string.IsNullOrWhiteSpace(legacyChannelName) &&
                TarChannelConfigs.TryGetValue(legacyChannelName, out TarChannelConfig legacyConfig))
                return NormalizeTarChannelConfig(legacyConfig);

            return NormalizeTarChannelConfig(null);
        }

        /// <summary>
        /// Saves TAR root folder and per-channel configuration.
        /// </summary>
        public void SaveTarSettings(string rootPath, IDictionary<string, TarChannelConfig> configs)
        {
            TarRecordingsRootPath = string.IsNullOrWhiteSpace(rootPath)
                ? DefaultTarRecordingsPath
                : rootPath.Trim();

            Dictionary<string, TarChannelConfig> normalized = new Dictionary<string, TarChannelConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, TarChannelConfig> kvp in configs ?? new Dictionary<string, TarChannelConfig>())
            {
                string configKey = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(configKey))
                    continue;

                normalized[configKey] = NormalizeTarChannelConfig(kvp.Value);
            }

            TarChannelConfigs = normalized;
            SaveSettings();
        }

        public void MigrateResourceScopedSettings(IEnumerable<Codeplug.Channel> channels)
        {
            if (channels == null)
                return;

            ChannelOutputDevices ??= new Dictionary<string, int>();
            ChannelOutputDeviceKeys ??= new Dictionary<string, string>();
            ChannelPositions ??= new Dictionary<string, ChannelPosition>();
            ChannelVolumes ??= new Dictionary<string, double>();
            TarChannelConfigs ??= new Dictionary<string, TarChannelConfig>();
            SelectedChannels ??= new List<string>();

            bool changed = false;
            HashSet<string> selectedChannelKeys = new HashSet<string>(SelectedChannels, StringComparer.OrdinalIgnoreCase);
            foreach (Codeplug.Channel channel in channels)
            {
                if (channel == null || string.IsNullOrWhiteSpace(channel.Tgid))
                    continue;

                string resourceKey = ResourceIdentity.Build(channel.System, channel.Tgid);
                if (string.IsNullOrWhiteSpace(resourceKey))
                    continue;

                if (!ChannelOutputDeviceKeys.ContainsKey(resourceKey) &&
                    ChannelOutputDeviceKeys.TryGetValue(channel.Tgid, out string legacyDeviceKey))
                {
                    ChannelOutputDeviceKeys[resourceKey] = NormalizeAudioDeviceKey(legacyDeviceKey);
                    changed = true;
                }

                if (!ChannelOutputDevices.ContainsKey(resourceKey) &&
                    ChannelOutputDevices.TryGetValue(channel.Tgid, out int legacyDevice))
                {
                    ChannelOutputDevices[resourceKey] = NormalizeAudioDeviceIndex(legacyDevice);
                    changed = true;
                }

                if (!TarChannelConfigs.ContainsKey(resourceKey))
                {
                    TarChannelConfig legacyConfig = null;
                    if (!string.IsNullOrWhiteSpace(channel.Tgid))
                        TarChannelConfigs.TryGetValue(channel.Tgid, out legacyConfig);
                    if (legacyConfig == null && !string.IsNullOrWhiteSpace(channel.Name))
                        TarChannelConfigs.TryGetValue(channel.Name, out legacyConfig);

                    if (legacyConfig != null)
                    {
                        TarChannelConfigs[resourceKey] = NormalizeTarChannelConfig(legacyConfig);
                        changed = true;
                    }
                }

                if (!ChannelPositions.ContainsKey(resourceKey) &&
                    !string.IsNullOrWhiteSpace(channel.Name) &&
                    ChannelPositions.TryGetValue(channel.Name, out ChannelPosition legacyPosition))
                {
                    ChannelPositions[resourceKey] = legacyPosition;
                    changed = true;
                }

                if (!ChannelVolumes.ContainsKey(resourceKey) &&
                    !string.IsNullOrWhiteSpace(channel.Name) &&
                    ChannelVolumes.TryGetValue(channel.Name, out double legacyVolume))
                {
                    ChannelVolumes[resourceKey] = legacyVolume;
                    changed = true;
                }

                if (!selectedChannelKeys.Contains(resourceKey) &&
                    ((!string.IsNullOrWhiteSpace(channel.Name) && selectedChannelKeys.Contains(channel.Name)) ||
                     (!string.IsNullOrWhiteSpace(channel.Tgid) && selectedChannelKeys.Contains(channel.Tgid))))
                {
                    SelectedChannels.Add(resourceKey);
                    selectedChannelKeys.Add(resourceKey);
                    changed = true;
                }
            }

            if (changed)
                SaveSettings();
        }

        public void PruneResourceSettings(IEnumerable<string> validChannelNames, IEnumerable<string> validTalkgroupIds, IEnumerable<string> validSystemNames, IEnumerable<string> validWebStreamNames = null, IEnumerable<string> validSelectableEncryptionKeys = null, IEnumerable<string> validResourceKeys = null)
        {
            HashSet<string> channelNames = new HashSet<string>(
                validChannelNames?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> talkgroupIds = new HashSet<string>(
                validTalkgroupIds?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> systemNames = new HashSet<string>(
                validSystemNames?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> webStreamNames = new HashSet<string>(
                validWebStreamNames?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> resourceKeys = new HashSet<string>(
                validResourceKeys?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            channelNames.Add(MainWindow.PLAYBACKCHNAME);
            talkgroupIds.Add(MainWindow.PLAYBACKTG);
            HashSet<string> validAudioOutputKeys = new HashSet<string>(resourceKeys, StringComparer.OrdinalIgnoreCase);
            validAudioOutputKeys.Add(MainWindow.PLAYBACKTG);
            foreach (string webStreamName in webStreamNames)
                validAudioOutputKeys.Add(webStreamName);
            HashSet<string> validChannelResourceKeys = new HashSet<string>(resourceKeys, StringComparer.OrdinalIgnoreCase);
            validChannelResourceKeys.Add(MainWindow.PLAYBACKCHNAME);

            bool changed = false;
            changed |= PruneDictionary(ChannelPositions, validChannelResourceKeys);
            changed |= PruneDictionary(ChannelVolumes, validChannelResourceKeys);
            changed |= PruneList(SelectedChannels, resourceKeys);
            HashSet<string> validTarKeys = new HashSet<string>(resourceKeys, StringComparer.OrdinalIgnoreCase);

            changed |= PruneDictionary(TarChannelConfigs, validTarKeys);
            changed |= PruneDictionary(ChannelOutputDevices, validAudioOutputKeys);
            changed |= PruneDictionary(ChannelOutputDeviceKeys, validAudioOutputKeys);
            changed |= PruneDictionary(SystemStatusPositions, systemNames);
            changed |= PruneDictionary(WebStreamPositions, webStreamNames);
            changed |= PruneDictionary(WebStreamVolumes, webStreamNames);
            changed |= PruneList(SelectedWebStreams, webStreamNames);

            HashSet<string> selectableEncryptionKeys = new HashSet<string>(
                validSelectableEncryptionKeys?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            changed |= PruneDictionary(SelectableEncryptionStates, selectableEncryptionKeys);

            if (changed)
                SaveSettings();
        }

        private static bool PruneList(List<string> list, HashSet<string> validKeys)
        {
            if (list == null || validKeys == null)
                return false;

            int originalCount = list.Count;
            List<string> normalized = list
                .Where(item => !string.IsNullOrWhiteSpace(item) && validKeys.Contains(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == originalCount && list.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
                return false;

            list.Clear();
            list.AddRange(normalized);
            return true;
        }

        private static bool PruneDictionary<TValue>(Dictionary<string, TValue> dictionary, HashSet<string> validKeys)
        {
            if (dictionary == null || validKeys == null)
                return false;

            List<string> staleKeys = dictionary.Keys
                .Where(key => !validKeys.Contains(key))
                .ToList();
            foreach (string key in staleKeys)
                dictionary.Remove(key);

            return staleKeys.Count > 0;
        }

        /// <summary>
        /// Gets a copy of patch group memberships for a codeplug context.
        /// </summary>
        /// <param name="contextKey"></param>
        /// <returns></returns>
        public Dictionary<string, List<PatchTalkgroupMember>> GetPatchGroupMemberships(string contextKey)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            if (!PatchGroupMemberships.TryGetValue(key, out Dictionary<string, List<PatchTalkgroupMember>> memberships))
                return new Dictionary<string, List<PatchTalkgroupMember>>();

            Dictionary<string, List<PatchTalkgroupMember>> copy = new Dictionary<string, List<PatchTalkgroupMember>>();
            foreach (KeyValuePair<string, List<PatchTalkgroupMember>> kvp in memberships)
                copy[kvp.Key] = NormalizePatchMembers(kvp.Value);

            return copy;
        }

        /// <summary>
        /// Saves patch group memberships for a codeplug context.
        /// </summary>
        /// <param name="contextKey"></param>
        /// <param name="memberships"></param>
        public void SavePatchGroupMemberships(string contextKey, Dictionary<string, List<PatchTalkgroupMember>> memberships)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            Dictionary<string, List<PatchTalkgroupMember>> normalized = new Dictionary<string, List<PatchTalkgroupMember>>();
            foreach (KeyValuePair<string, List<PatchTalkgroupMember>> kvp in memberships ?? new Dictionary<string, List<PatchTalkgroupMember>>())
                normalized[kvp.Key] = NormalizePatchMembers(kvp.Value);

            PatchGroupMemberships[key] = normalized;
            SaveSettings();
        }

        /// <summary>
        /// Gets a copy of patch group one-way modes for a codeplug context.
        /// </summary>
        /// <param name="contextKey"></param>
        /// <returns></returns>
        public Dictionary<string, bool> GetPatchGroupModes(string contextKey)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            if (!PatchGroupModes.TryGetValue(key, out Dictionary<string, bool> modes))
                return new Dictionary<string, bool>();

            Dictionary<string, bool> copy = new Dictionary<string, bool>();
            foreach (KeyValuePair<string, bool> kvp in modes)
            {
                string groupName = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(groupName))
                    continue;
                copy[groupName] = kvp.Value;
            }

            return copy;
        }

        /// <summary>
        /// Saves patch group one-way modes for a codeplug context.
        /// </summary>
        /// <param name="contextKey"></param>
        /// <param name="modes"></param>
        public void SavePatchGroupModes(string contextKey, Dictionary<string, bool> modes)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            Dictionary<string, bool> normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, bool> kvp in modes ?? new Dictionary<string, bool>())
            {
                string groupName = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(groupName))
                    continue;
                normalized[groupName] = kvp.Value;
            }

            PatchGroupModes[key] = new Dictionary<string, bool>(normalized);
            SaveSettings();
        }

        /// <summary>
        /// Gets a copy of patch enabled state for a codeplug context.
        /// </summary>
        public Dictionary<string, bool> GetPatchGroupEnabledStates(string contextKey)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            if (!PatchGroupEnabledStates.TryGetValue(key, out Dictionary<string, bool> states))
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, bool> copy = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, bool> kvp in states)
            {
                string groupName = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(groupName))
                    continue;
                copy[groupName] = kvp.Value;
            }

            return copy;
        }

        /// <summary>
        /// Saves patch enabled state for a codeplug context.
        /// </summary>
        public void SavePatchGroupEnabledStates(string contextKey, Dictionary<string, bool> states)
        {
            string key = NormalizePatchMembershipKey(contextKey);
            Dictionary<string, bool> normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, bool> kvp in states ?? new Dictionary<string, bool>())
            {
                string groupName = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(groupName))
                    continue;
                normalized[groupName] = kvp.Value;
            }

            PatchGroupEnabledStates[key] = new Dictionary<string, bool>(normalized);
            SaveSettings();
        }

        /// <summary>
        /// Normalizes and de-duplicates patch membership entries.
        /// </summary>
        /// <param name="members"></param>
        /// <returns></returns>
        private static List<PatchTalkgroupMember> NormalizePatchMembers(IEnumerable<PatchTalkgroupMember> members)
        {
            return (members ?? Enumerable.Empty<PatchTalkgroupMember>())
                .Where(m => !string.IsNullOrWhiteSpace(m?.SystemName) && !string.IsNullOrWhiteSpace(m?.Tgid))
                .GroupBy(m => $"{m.SystemName.Trim().ToLowerInvariant()}|{m.Tgid.Trim()}")
                .Select(g =>
                {
                    PatchTalkgroupMember first = g.First();
                    return new PatchTalkgroupMember
                    {
                        SystemName = first.SystemName.Trim(),
                        Tgid = first.Tgid.Trim()
                    };
                })
                .ToList();
        }

        /// <summary>
        /// Normalizes the settings key used to scope patch memberships.
        /// </summary>
        /// <param name="contextKey"></param>
        /// <returns></returns>
        private static string NormalizePatchMembershipKey(string contextKey)
        {
            if (string.IsNullOrWhiteSpace(contextKey))
                return "__default__";

            return contextKey.Trim().ToLowerInvariant();
        }

        private static TarChannelConfig NormalizeTarChannelConfig(TarChannelConfig config)
        {
            bool looksLikeLegacyDefault = config != null &&
                !config.Enabled &&
                config.RetentionDays == 30 &&
                (config.IgnoredSubscriberIds == null || config.IgnoredSubscriberIds.Count == 0);

            TarChannelConfig normalized = new TarChannelConfig
            {
                Enabled = config?.Enabled ?? false,
                RetentionDays = looksLikeLegacyDefault
                    ? 7
                    : (config?.RetentionDays ?? 7),
                IgnoredSubscriberIds = (config?.IgnoredSubscriberIds ?? new List<uint>())
                    .Where(id => id > 0)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
            };

            if (normalized.RetentionDays < 0)
                normalized.RetentionDays = 0;

            return normalized;
        }
    } // public class SettingsManager
} // namespace dvmconsole
