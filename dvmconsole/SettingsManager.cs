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

using Newtonsoft.Json;

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
        /// 
        /// </summary>
        public bool ShowSystemStatus { get; set; } = true;
        /// <summary>
        /// 
        /// </summary>
        public bool ShowChannels { get; set; } = true;
        /// <summary>
        /// 
        /// </summary>
        public bool ShowAlertTones { get; set; } = true;

        /// <summary>
        /// 
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
        /// 
        /// </summary>
        public bool Maximized { get; set; } = false;
        /// <summary>
        /// 
        /// </summary>
        public bool DarkMode { get; set; } = false;
        /// <summary>
        /// 
        /// </summary>
        public double WindowWidth { get; set; } = MainWindow.MIN_WIDTH;
        /// <summary>
        /// 
        /// </summary>
        public double WindowHeight { get; set; } = MainWindow.MIN_HEIGHT;
        /// <summary>
        /// 
        /// </summary>
        public double CanvasWidth { get; set; } = MainWindow.MIN_WIDTH;
        /// <summary>
        /// 
        /// </summary>
        public double CanvasHeight { get; set; } = MainWindow.MIN_HEIGHT;

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
                var json = File.ReadAllText(SettingsFilePath);
                var loadedSettings = JsonConvert.DeserializeObject<SettingsManager>(json);

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

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error loading settings: {ex.Message}");
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
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error saving settings: {ex.Message}");
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
