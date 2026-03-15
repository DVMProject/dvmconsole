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
        /// 
        /// </summary>
        public Dictionary<string, int> ChannelOutputDevices { get; set; } = new Dictionary<string, int>();
        /// <summary>
        /// Saved patch group memberships scoped by codeplug context key.
        /// </summary>
        public Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>> PatchGroupMemberships { get; set; } = new Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>>();

        /// <summary>
        /// Stored member identity for a patch talkgroup.
        /// </summary>
        public class PatchTalkgroupMember
        {
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;
        }

        /// <summary>
        /// Flag indicating the PTT mode, Toggle PTT or Regular PTT.
        /// </summary>
        public bool TogglePTTMode { get; set; } = true;

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
                    ChannelOutputDevices = loadedSettings.ChannelOutputDevices ?? new Dictionary<string, int>();
                    PatchGroupMemberships = loadedSettings.PatchGroupMemberships ?? new Dictionary<string, Dictionary<string, List<PatchTalkgroupMember>>>();
                    TogglePTTMode = loadedSettings.TogglePTTMode;
                    LockWidgets = loadedSettings.LockWidgets;
                    SnapCallHistoryToWindow = loadedSettings.SnapCallHistoryToWindow;
                    KeepWindowOnTop = loadedSettings.KeepWindowOnTop;
                    TalkPermitTone = loadedSettings.TalkPermitTone;
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
            ChannelOutputDevices[channelName] = deviceIndex;
            SaveSettings();
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
