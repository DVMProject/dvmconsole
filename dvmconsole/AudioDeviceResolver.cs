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

using System.Text;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace dvmconsole
{
    public enum AudioBackendKind
    {
        Wasapi,
        Mme
    }

    /// <summary>
    /// Resolves persisted audio device identities back to the current Windows audio endpoint.
    /// </summary>
    public static class AudioDeviceResolver
    {
        public const string WINDOWS_DEFAULT_DEVICE_KEY = "windows-default";
        public const string INHERIT_MASTER_OUTPUT_KEY = "inherit-master-output";
        public const string WASAPI_INPUT_DEVICE_KEY_PREFIX = "wasapi|input|";
        public const string WASAPI_OUTPUT_DEVICE_KEY_PREFIX = "wasapi|output|";

        public sealed class AudioDeviceOption
        {
            public string DisplayName { get; set; } = string.Empty;
            public int DeviceNumber { get; set; } = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            public string DeviceKey { get; set; } = string.Empty;
            public AudioBackendKind Backend { get; set; } = AudioBackendKind.Wasapi;
        }

        public sealed class AudioDeviceSelection
        {
            public AudioBackendKind Backend { get; set; }
            public MMDevice WasapiDevice { get; set; }
            public int DeviceNumber { get; set; } = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            public string DisplayName { get; set; } = string.Empty;
            public string DeviceKey { get; set; } = WINDOWS_DEFAULT_DEVICE_KEY;
        }

        /// <summary>
        /// Creates a stable legacy MME key for an input device index.
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
        /// Creates a stable legacy MME key for an output device index.
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

        public static List<AudioDeviceOption> GetInputDeviceOptions(AudioBackendKind? backendFilter = null)
        {
            List<AudioDeviceOption> devices = new List<AudioDeviceOption>
            {
                new AudioDeviceOption
                {
                    DisplayName = "Windows Default Input",
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    DeviceKey = WINDOWS_DEFAULT_DEVICE_KEY,
                    Backend = AudioBackendKind.Wasapi
                }
            };

            if (backendFilter == null || backendFilter == AudioBackendKind.Wasapi)
                devices.AddRange(GetWasapiDeviceOptions(DataFlow.Capture, "WASAPI", WASAPI_INPUT_DEVICE_KEY_PREFIX));

            if (backendFilter == null || backendFilter == AudioBackendKind.Mme)
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    WaveInCapabilities deviceInfo = WaveIn.GetCapabilities(i);
                    devices.Add(new AudioDeviceOption
                    {
                        DisplayName = $"Legacy MME: {deviceInfo.ProductName}",
                        DeviceNumber = i,
                        DeviceKey = BuildInputDeviceKey(deviceInfo),
                        Backend = AudioBackendKind.Mme
                    });
                }
            }

            return devices;
        }

        public static List<AudioDeviceOption> GetOutputDeviceOptions(bool includeInheritOption, AudioBackendKind? backendFilter = null)
        {
            List<AudioDeviceOption> devices = new List<AudioDeviceOption>();
            if (includeInheritOption)
            {
                devices.Add(new AudioDeviceOption
                {
                    DisplayName = "Default (Master Output)",
                    DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                    DeviceKey = INHERIT_MASTER_OUTPUT_KEY,
                    Backend = AudioBackendKind.Wasapi
                });
            }

            devices.Add(new AudioDeviceOption
            {
                DisplayName = "Windows Default Output",
                DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                DeviceKey = WINDOWS_DEFAULT_DEVICE_KEY,
                Backend = AudioBackendKind.Wasapi
            });

            if (backendFilter == null || backendFilter == AudioBackendKind.Wasapi)
                devices.AddRange(GetWasapiDeviceOptions(DataFlow.Render, "WASAPI", WASAPI_OUTPUT_DEVICE_KEY_PREFIX));

            if (backendFilter == null || backendFilter == AudioBackendKind.Mme)
            {
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    WaveOutCapabilities deviceInfo = WaveOut.GetCapabilities(i);
                    devices.Add(new AudioDeviceOption
                    {
                        DisplayName = $"Legacy MME: {deviceInfo.ProductName}",
                        DeviceNumber = i,
                        DeviceKey = BuildOutputDeviceKey(deviceInfo),
                        Backend = AudioBackendKind.Mme
                    });
                }
            }

            return devices;
        }

        /// <summary>
        /// Resolves an input device key to the current runtime index.
        /// </summary>
        public static int ResolveInputDeviceNumber(string deviceKey, int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
        {
            if (IsWindowsDefault(deviceKey))
                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            if (IsWasapiDeviceKey(deviceKey))
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

            if (IsWasapiDeviceKey(deviceKey))
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

        public static AudioDeviceSelection ResolveInputDevice(string deviceKey, int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
        {
            if ((IsWindowsDefault(deviceKey) || IsWasapiInputDeviceKey(deviceKey)) &&
                TryResolveWasapiDevice(DataFlow.Capture, deviceKey, out MMDevice wasapiDevice))
            {
                int legacyFallbackDeviceNumber = ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveIn.DeviceCount);
                if (!IsWindowsDefault(deviceKey) && legacyFallbackDeviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
                    legacyFallbackDeviceNumber = FindLegacyInputDeviceNumberByName(wasapiDevice.FriendlyName);

                return new AudioDeviceSelection
                {
                    Backend = AudioBackendKind.Wasapi,
                    WasapiDevice = wasapiDevice,
                    DeviceNumber = legacyFallbackDeviceNumber,
                    DisplayName = IsWindowsDefault(deviceKey) ? "Windows Default Input" : wasapiDevice.FriendlyName,
                    DeviceKey = NormalizeResolvedDeviceKey(deviceKey)
                };
            }

            return ResolveMmeInputFallback(legacyDeviceNumber, deviceKey);
        }

        public static AudioDeviceSelection ResolveOutputDevice(string deviceKey, int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
        {
            if ((IsWindowsDefault(deviceKey) || IsWasapiOutputDeviceKey(deviceKey)) &&
                TryResolveWasapiDevice(DataFlow.Render, deviceKey, out MMDevice wasapiDevice))
            {
                int legacyFallbackDeviceNumber = ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveOut.DeviceCount);
                if (!IsWindowsDefault(deviceKey) && legacyFallbackDeviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE)
                    legacyFallbackDeviceNumber = FindLegacyOutputDeviceNumberByName(wasapiDevice.FriendlyName);

                return new AudioDeviceSelection
                {
                    Backend = AudioBackendKind.Wasapi,
                    WasapiDevice = wasapiDevice,
                    DeviceNumber = legacyFallbackDeviceNumber,
                    DisplayName = IsWindowsDefault(deviceKey) ? "Windows Default Output" : wasapiDevice.FriendlyName,
                    DeviceKey = NormalizeResolvedDeviceKey(deviceKey)
                };
            }

            return ResolveMmeOutputFallback(legacyDeviceNumber, deviceKey);
        }

        public static AudioDeviceSelection ResolveMmeOutputFallback(int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE, string deviceKey = null)
        {
            int deviceNumber = IsWindowsDefault(deviceKey) || IsWasapiDeviceKey(deviceKey)
                ? ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveOut.DeviceCount)
                : ResolveOutputDeviceNumber(deviceKey, legacyDeviceNumber);
            return new AudioDeviceSelection
            {
                Backend = AudioBackendKind.Mme,
                DeviceNumber = deviceNumber,
                DisplayName = deviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE
                    ? "Windows Default Output (Legacy MME)"
                    : WaveOut.GetCapabilities(deviceNumber).ProductName,
                DeviceKey = string.IsNullOrWhiteSpace(deviceKey) || IsWasapiDeviceKey(deviceKey)
                    ? WINDOWS_DEFAULT_DEVICE_KEY
                    : deviceKey.Trim()
            };
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
            if (IsWasapiInputDeviceKey(deviceKey))
            {
                bool exists = TryResolveWasapiDevice(DataFlow.Capture, deviceKey, out MMDevice device);
                device?.Dispose();
                return exists;
            }

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
            if (IsWasapiOutputDeviceKey(deviceKey))
            {
                bool exists = TryResolveWasapiDevice(DataFlow.Render, deviceKey, out MMDevice device);
                device?.Dispose();
                return exists;
            }

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

        public static bool IsWasapiDeviceKey(string deviceKey)
        {
            return IsWasapiInputDeviceKey(deviceKey) || IsWasapiOutputDeviceKey(deviceKey);
        }

        public static bool IsWasapiInputDeviceKey(string deviceKey)
        {
            return !string.IsNullOrWhiteSpace(deviceKey) &&
                deviceKey.StartsWith(WASAPI_INPUT_DEVICE_KEY_PREFIX, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWasapiOutputDeviceKey(string deviceKey)
        {
            return !string.IsNullOrWhiteSpace(deviceKey) &&
                deviceKey.StartsWith(WASAPI_OUTPUT_DEVICE_KEY_PREFIX, StringComparison.OrdinalIgnoreCase);
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

        private static IEnumerable<AudioDeviceOption> GetWasapiDeviceOptions(DataFlow dataFlow, string displayPrefix, string keyPrefix)
        {
            List<AudioDeviceOption> devices = new List<AudioDeviceOption>();

            try
            {
                using MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
                {
                    devices.Add(new AudioDeviceOption
                    {
                        DisplayName = $"{displayPrefix}: {device.FriendlyName}",
                        DeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE,
                        DeviceKey = BuildWasapiDeviceKey(keyPrefix, device.ID),
                        Backend = AudioBackendKind.Wasapi
                    });
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Unable to enumerate WASAPI {dataFlow} devices: {ex.Message}");
            }

            return devices;
        }

        public static AudioDeviceSelection ResolveMmeInputFallback(int legacyDeviceNumber = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE, string deviceKey = null, string displayName = null)
        {
            int deviceNumber = IsWindowsDefault(deviceKey) || IsWasapiDeviceKey(deviceKey)
                ? ResolveLegacyDeviceNumber(legacyDeviceNumber, WaveIn.DeviceCount)
                : ResolveInputDeviceNumber(deviceKey, legacyDeviceNumber);
            if (deviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE && !string.IsNullOrWhiteSpace(displayName))
                deviceNumber = FindLegacyInputDeviceNumberByName(displayName);

            return new AudioDeviceSelection
            {
                Backend = AudioBackendKind.Mme,
                DeviceNumber = deviceNumber,
                DisplayName = deviceNumber == SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE
                    ? "Windows Default Input (Legacy MME)"
                    : WaveIn.GetCapabilities(deviceNumber).ProductName,
                DeviceKey = string.IsNullOrWhiteSpace(deviceKey) || IsWasapiDeviceKey(deviceKey)
                    ? WINDOWS_DEFAULT_DEVICE_KEY
                    : deviceKey.Trim()
            };
        }

        private static bool TryResolveWasapiDevice(DataFlow dataFlow, string deviceKey, out MMDevice device)
        {
            device = null;

            try
            {
                MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                if (IsWindowsDefault(deviceKey))
                {
                    if (!enumerator.HasDefaultAudioEndpoint(dataFlow, Role.Multimedia))
                        return false;

                    device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
                    return device != null;
                }

                string expectedPrefix = dataFlow == DataFlow.Capture
                    ? WASAPI_INPUT_DEVICE_KEY_PREFIX
                    : WASAPI_OUTPUT_DEVICE_KEY_PREFIX;
                if (!TryDecodeWasapiDeviceId(deviceKey, expectedPrefix, out string deviceId))
                    return false;

                device = enumerator.GetDevice(deviceId);
                return device != null && device.State == DeviceState.Active;
            }
            catch
            {
                device = null;
                return false;
            }
        }

        private static int FindLegacyInputDeviceNumberByName(string friendlyName)
        {
            return FindLegacyDeviceNumberByName(
                friendlyName,
                WaveIn.DeviceCount,
                deviceNumber => WaveIn.GetCapabilities(deviceNumber).ProductName);
        }

        private static int FindLegacyOutputDeviceNumberByName(string friendlyName)
        {
            return FindLegacyDeviceNumberByName(
                friendlyName,
                WaveOut.DeviceCount,
                deviceNumber => WaveOut.GetCapabilities(deviceNumber).ProductName);
        }

        private static int FindLegacyDeviceNumberByName(string friendlyName, int deviceCount, Func<int, string> getDeviceName)
        {
            string normalizedFriendlyName = NormalizeDeviceMatchName(friendlyName);
            if (string.IsNullOrWhiteSpace(normalizedFriendlyName))
                return SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;

            int bestMatch = SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
            int bestScore = 0;
            for (int i = 0; i < deviceCount; i++)
            {
                string normalizedDeviceName = NormalizeDeviceMatchName(getDeviceName(i));
                if (string.IsNullOrWhiteSpace(normalizedDeviceName))
                    continue;

                int score = GetDeviceNameMatchScore(normalizedFriendlyName, normalizedDeviceName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = i;
                }
            }

            return bestScore >= 3 ? bestMatch : SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE;
        }

        private static int GetDeviceNameMatchScore(string friendlyName, string deviceName)
        {
            if (string.Equals(friendlyName, deviceName, StringComparison.OrdinalIgnoreCase))
                return 100;

            if (friendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
                deviceName.Contains(friendlyName, StringComparison.OrdinalIgnoreCase))
                return Math.Min(friendlyName.Length, deviceName.Length);

            HashSet<string> friendlyWords = new HashSet<string>(friendlyName.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            return deviceName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(friendlyWords.Contains);
        }

        private static string NormalizeDeviceMatchName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value.Trim().ToLowerInvariant())
                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');

            return string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string BuildWasapiDeviceKey(string keyPrefix, string deviceId)
        {
            string encodedId = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceId ?? string.Empty));
            return keyPrefix + encodedId;
        }

        private static bool TryDecodeWasapiDeviceId(string deviceKey, string expectedPrefix, out string deviceId)
        {
            deviceId = string.Empty;
            if (string.IsNullOrWhiteSpace(deviceKey) ||
                !deviceKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                string encodedId = deviceKey.Substring(expectedPrefix.Length);
                deviceId = Encoding.UTF8.GetString(Convert.FromBase64String(encodedId));
                return !string.IsNullOrWhiteSpace(deviceId);
            }
            catch
            {
                deviceId = string.Empty;
                return false;
            }
        }

        private static string NormalizeResolvedDeviceKey(string deviceKey)
        {
            return string.IsNullOrWhiteSpace(deviceKey)
                ? WINDOWS_DEFAULT_DEVICE_KEY
                : deviceKey.Trim();
        }
    }
}
