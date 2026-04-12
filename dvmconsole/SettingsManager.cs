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
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using System.Windows.Forms;
using Newtonsoft.Json;

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

        public static readonly string UserAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        public static readonly string RootAppDataPath = "DVMProject" + Path.DirectorySeparatorChar + "dvmconsole";
        public static string UserAppDataPath = UserAppData + Path.DirectorySeparatorChar + RootAppDataPath;

        private static string SettingsFilePath = UserAppDataPath + Path.DirectorySeparatorChar + "UserSettings.json";

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
        /// Global input device override. -1 follows the current Windows default device.
        /// </summary>
        public int AudioInputDevice { get; set; } = WINDOWS_DEFAULT_AUDIO_DEVICE;
        /// <summary>
        /// Master output device used by resources without a per-TG override. -1 follows Windows default.
        /// </summary>
        public int MasterOutputDevice { get; set; } = WINDOWS_DEFAULT_AUDIO_DEVICE;
        /// <summary>
        /// Enables simple console microphone automatic gain control.
        /// </summary>
        public bool AudioInputAgcEnabled { get; set; } = false;
        /// <summary>
        /// Saved per-channel volume levels.
        /// </summary>
        public Dictionary<string, double> ChannelVolumes { get; set; } = new Dictionary<string, double>();
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
        /*
        ** Methods
        */

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
                    AlertToneTabs = loadedSettings.AlertToneTabs ?? new Dictionary<string, string>();
                    AlertTones = loadedSettings.AlertTones ?? new List<AlertToneConfig>();
                    ChannelOutputDevices = loadedSettings.ChannelOutputDevices ?? new Dictionary<string, int>();
                    AudioInputDevice = NormalizeAudioDeviceIndex(loadedSettings.AudioInputDevice);
                    MasterOutputDevice = NormalizeAudioDeviceIndex(loadedSettings.MasterOutputDevice);
                    AudioInputAgcEnabled = loadedSettings.AudioInputAgcEnabled;
                    MigrateLegacyAudioSettings();
                    ChannelVolumes = loadedSettings.ChannelVolumes ?? new Dictionary<string, double>();
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

            return AlertTones
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
        /// 
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void UpdateChannelPosition(string channelName, double x, double y)
        {
            ChannelPositions[channelName] = new ChannelPosition { X = x, Y = y };
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
        public void UpdateChannelOutputDevice(string channelName, int deviceIndex)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            ChannelOutputDevices[channelName] = NormalizeAudioDeviceIndex(deviceIndex);
            SaveSettings();
        }

        public void RemoveChannelOutputDevice(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            if (ChannelOutputDevices.Remove(channelName))
                SaveSettings();
        }

        public void UpdateAudioInputDevice(int deviceIndex)
        {
            AudioInputDevice = NormalizeAudioDeviceIndex(deviceIndex);
            SaveSettings();
        }

        public void UpdateMasterOutputDevice(int deviceIndex)
        {
            MasterOutputDevice = NormalizeAudioDeviceIndex(deviceIndex);
            SaveSettings();
        }

        public static int NormalizeAudioDeviceIndex(int deviceIndex)
        {
            return deviceIndex < WINDOWS_DEFAULT_AUDIO_DEVICE ? WINDOWS_DEFAULT_AUDIO_DEVICE : deviceIndex;
        }

        private void MigrateLegacyAudioSettings()
        {
            if (ChannelOutputDevices == null)
                ChannelOutputDevices = new Dictionary<string, int>();

            if (ChannelOutputDevices.TryGetValue(LEGACY_GLOBAL_INPUT_DEVICE_KEY, out int legacyInputDevice))
            {
                AudioInputDevice = NormalizeAudioDeviceIndex(legacyInputDevice);
                ChannelOutputDevices.Remove(LEGACY_GLOBAL_INPUT_DEVICE_KEY);
            }
        }

        /// <summary>
        /// Saves a per-channel volume level.
        /// </summary>
        public void UpdateChannelVolume(string channelName, double volume)
        {
            ChannelVolumes[channelName] = volume;
            SaveSettings();
        }

        public void PruneResourceSettings(IEnumerable<string> validChannelNames, IEnumerable<string> validTalkgroupIds, IEnumerable<string> validSystemNames)
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

            channelNames.Add(MainWindow.PLAYBACKCHNAME);
            talkgroupIds.Add(MainWindow.PLAYBACKTG);

            bool changed = false;
            changed |= PruneDictionary(ChannelPositions, channelNames);
            changed |= PruneDictionary(ChannelVolumes, channelNames);
            changed |= PruneDictionary(ChannelOutputDevices, talkgroupIds);
            changed |= PruneDictionary(SystemStatusPositions, systemNames);

            if (changed)
                SaveSettings();
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
    } // public class SettingsManager
} // namespace dvmconsole
