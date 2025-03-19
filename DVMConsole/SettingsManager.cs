// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*
*/

using System.IO;
using Newtonsoft.Json;

namespace DVMConsole
{
    public class SettingsManager
    {
        private const string SettingsFilePath = "UserSettings.json";

        public bool ShowSystemStatus { get; set; } = true;
        public bool ShowChannels { get; set; } = true;
        public bool ShowAlertTones { get; set; } = true;

        public string LastCodeplugPath { get; set; } = null;

        public Dictionary<string, ChannelPosition> ChannelPositions { get; set; } = new Dictionary<string, ChannelPosition>();
        public Dictionary<string, ChannelPosition> SystemStatusPositions { get; set; } = new Dictionary<string, ChannelPosition>();
        public List<string> AlertToneFilePaths { get; set; } = new List<string>();
        public Dictionary<string, ChannelPosition> AlertTonePositions { get; set; } = new Dictionary<string, ChannelPosition>();
        public Dictionary<string, int> ChannelOutputDevices { get; set; } = new Dictionary<string, int>();

        public void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath)) return;

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
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        public void UpdateAlertTonePaths(string newFilePath)
        {
            if (!AlertToneFilePaths.Contains(newFilePath))
            {
                AlertToneFilePaths.Add(newFilePath);
                SaveSettings();
            }
        }

        public void UpdateAlertTonePosition(string alertFileName, double x, double y)
        {
            AlertTonePositions[alertFileName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        public void UpdateChannelPosition(string channelName, double x, double y)
        {
            ChannelPositions[channelName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        public void UpdateSystemStatusPosition(string systemName, double x, double y)
        {
            SystemStatusPositions[systemName] = new ChannelPosition { X = x, Y = y };
            SaveSettings();
        }

        public void UpdateChannelOutputDevice(string channelName, int deviceIndex)
        {
            ChannelOutputDevices[channelName] = deviceIndex;
            SaveSettings();
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
