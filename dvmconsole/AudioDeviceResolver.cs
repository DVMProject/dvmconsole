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

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// Resolves persisted audio device identities back to the current WinMM device index.
    /// </summary>
    public static class AudioDeviceResolver
    {
        public const string WINDOWS_DEFAULT_DEVICE_KEY = "windows-default";
        public const string INHERIT_MASTER_OUTPUT_KEY = "inherit-master-output";

        /// <summary>
        /// Creates a stable key for an input device index.
        /// </summary>
        public static string GetInputDeviceKey(int deviceNumber)
        {
            if (deviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
                return WINDOWS_DEFAULT_DEVICE_KEY;
            if (deviceNumber < 0 || deviceNumber >= WaveIn.DeviceCount)
                return string.Empty;

            WaveInCapabilities capabilities = WaveIn.GetCapabilities(deviceNumber);
            return BuildInputDeviceKey(capabilities);
        }

        /// <summary>
        /// Creates a stable key for an output device index.
        /// </summary>
        public static string GetOutputDeviceKey(int deviceNumber)
        {
            if (deviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
                return WINDOWS_DEFAULT_DEVICE_KEY;
            if (deviceNumber < 0 || deviceNumber >= WaveOut.DeviceCount)
                return string.Empty;

            WaveOutCapabilities capabilities = WaveOut.GetCapabilities(deviceNumber);
            return BuildOutputDeviceKey(capabilities);
        }

        /// <summary>
        /// Resolves an input device key to the current runtime index.
        /// </summary>
        public static int ResolveInputDeviceNumber(string deviceKey, int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
        {
            if (IsWindowsDefault(deviceKey))
                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    if (string.Equals(GetInputDeviceKey(i), deviceKey, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            }

            return ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveIn.DeviceCount);
        }

        /// <summary>
        /// Resolves an output device key to the current runtime index.
        /// </summary>
        public static int ResolveOutputDeviceNumber(string deviceKey, int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
        {
            if (IsWindowsDefault(deviceKey))
                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    if (string.Equals(GetOutputDeviceKey(i), deviceKey, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            }

            return ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveOut.DeviceCount);
        }

        /// <summary>
        /// Returns true if the saved key is available in the current input list.
        /// </summary>
        public static bool InputDeviceKeyExists(string deviceKey)
        {
            if (IsWindowsDefault(deviceKey))
                return true;
            if (string.IsNullOrWhiteSpace(deviceKey))
                return false;

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                if (string.Equals(GetInputDeviceKey(i), deviceKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the saved key is available in the current output list.
        /// </summary>
        public static bool OutputDeviceKeyExists(string deviceKey)
        {
            if (IsWindowsDefault(deviceKey))
                return true;
            if (string.IsNullOrWhiteSpace(deviceKey))
                return false;

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                if (string.Equals(GetOutputDeviceKey(i), deviceKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool IsWindowsDefault(string deviceKey)
        {
            return string.IsNullOrWhiteSpace(deviceKey) ||
                string.Equals(deviceKey, WINDOWS_DEFAULT_DEVICE_KEY, StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveLegacyDeviceNumber(int legacyDeviceNumber, int deviceCount)
        {
            int normalized = SettingsManager.NormalizeAudioDeviceIndex(legacyDeviceNumber);
            return normalized >= deviceCount ? SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE : normalized;
        }

        private static string BuildInputDeviceKey(WaveInCapabilities capabilities)
        {
            return BuildDeviceKey(
                "input",
                capabilities.ProductName,
                capabilities.Channels,
                capabilities.ProductGuid,
                capabilities.NameGuid,
                capabilities.ManufacturerGuid);
        }

        private static string BuildOutputDeviceKey(WaveOutCapabilities capabilities)
        {
            return BuildDeviceKey(
                "output",
                capabilities.ProductName,
                capabilities.Channels,
                capabilities.ProductGuid,
                capabilities.NameGuid,
                capabilities.ManufacturerGuid);
        }

        private static string BuildDeviceKey(string direction, string productName, int channels, Guid productGuid, Guid nameGuid, Guid manufacturerGuid)
        {
            string normalizedName = NormalizeDeviceName(productName);
            return $"{direction}|pg={NormalizeGuid(productGuid)}|ng={NormalizeGuid(nameGuid)}|mg={NormalizeGuid(manufacturerGuid)}|ch={channels}|name={normalizedName}";
        }

        private static string NormalizeGuid(Guid value)
        {
            return value == Guid.Empty ? "none" : value.ToString("D").ToLowerInvariant();
        }

        private static string NormalizeDeviceName(string productName)
        {
            return (productName ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
