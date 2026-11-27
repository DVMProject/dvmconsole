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

using System.Security.Cryptography;

namespace dvmconsole
{
    /// <summary>
    /// Downloaded traffic keys and request cooldowns, isolated by FNE system.
    /// </summary>
    internal sealed class TrafficKeyCache
    {
        private const long REQUEST_RETRY_MS = 30000;
        private readonly object sync = new();
        private readonly Dictionary<(string System, byte Algorithm, ushort Id), byte[]> keys = new();
        private readonly Dictionary<(string System, byte Algorithm, ushort Id), long> requests = new();

        private static (string, byte, ushort) Index(string system, byte algorithm, ushort id) =>
            ((system ?? string.Empty).Trim().ToUpperInvariant(), algorithm, id);

        public bool Store(string system, byte algorithm, ushort id, byte[] material)
        {
            int length = algorithm switch { 0x01 => 2, 0x81 => 8, 0x84 => 32, 0xAA => 5, _ => 0 };
            if (string.IsNullOrWhiteSpace(system) || id == 0 || length == 0 || material?.Length != length)
                return false;

            lock (sync)
            {
                var index = Index(system, algorithm, id);
                if (keys.Remove(index, out byte[] previous))
                    CryptographicOperations.ZeroMemory(previous);
                keys[index] = material.ToArray();
                requests.Remove(index);
                return true;
            }
        }

        public bool TryGet(string system, byte algorithm, ushort id, out byte[] material)
        {
            lock (sync)
            {
                material = keys.TryGetValue(Index(system, algorithm, id), out byte[] key) ? key.ToArray() : null;
                return material != null;
            }
        }

        public bool TryBeginRequest(string system, byte algorithm, ushort id, long now)
        {
            lock (sync)
            {
                var index = Index(system, algorithm, id);
                if (keys.ContainsKey(index) ||
                    (requests.TryGetValue(index, out long sent) && now - sent < REQUEST_RETRY_MS))
                    return false;
                requests[index] = now;
                return true;
            }
        }

        public void ForgetRequest(string system, byte algorithm, ushort id)
        {
            lock (sync)
                requests.Remove(Index(system, algorithm, id));
        }

        public void ClearRequests(string system)
        {
            string systemKey = Index(system, 0, 0).Item1;
            lock (sync)
            {
                foreach (var index in requests.Keys.Where(key => key.System == systemKey).ToList())
                    requests.Remove(index);
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                foreach (byte[] key in keys.Values)
                    CryptographicOperations.ZeroMemory(key);
                keys.Clear();
                requests.Clear();
            }
        }
    }
}
