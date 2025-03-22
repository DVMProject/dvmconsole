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
*
*/

using System.Diagnostics;
using System.IO;
using System.Reflection;

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
        public static readonly string UserAppDataPath = UserAppData + Path.DirectorySeparatorChar + RootAppDataPath;

        private static readonly string SettingsFilePath = UserAppDataPath + Path.DirectorySeparatorChar + "UserSettings.json";

        /*
        ** Properties
        */

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
        /// Flag indicating the PTT mode, Toggle PTT or Regular PTT.
        /// </summary>
        public bool TogglePTTMode { get; set; } = true;

        /// <summary>
        /// Flag indicating channel and other widgets are locked in place.
        /// </summary>
        public bool LockWidgets { get; set; } = true;

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

        /*
        ** Methods
        */

        /// <summary>
        /// 
        /// </summary>
        public bool LoadSettings()
        {
            if (!Directory.Exists(UserAppDataPath))
                Directory.CreateDirectory(UserAppDataPath);

            if (!File.Exists(SettingsFilePath))
                return false;

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                SettingsManager loadedSettings = JsonConvert.DeserializeObject<SettingsManager>(json);

                if (loadedSettings != null)
                {
                    ShowSystemStatus = loadedSettings.ShowSystemStatus;
                    ShowChannels = loadedSettings.ShowChannels;
                    ShowAlertTones = loadedSettings.ShowAlertTones;
                    LastCodeplugPath = loadedSettings.LastCodeplugPath;
                    ChannelPositions = loadedSettings.ChannelPositions ?? new Dictionary<string, ChannelPosition>();
                    SystemStatusPositions = loadedSettings.SystemStatusPositions ?? new Dictionary<string, ChannelPosition>();
                    AlertToneFilePaths = loadedSettings.AlertToneFilePaths ?? new List<string>();
                    AlertTonePositions = loadedSettings.AlertTonePositions ?? new Dictionary<string, ChannelPosition>();
                    ChannelOutputDevices = loadedSettings.ChannelOutputDevices ?? new Dictionary<string, int>();
                    TogglePTTMode = loadedSettings.TogglePTTMode;
                    LockWidgets = loadedSettings.LockWidgets;
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
        /// 
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
        /// 
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
    } // public class SettingsManager
} // namespace dvmconsole
