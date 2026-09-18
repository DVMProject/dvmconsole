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
*/

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace dvmconsole
{
    public static class RadioAliasCache
    {
        public static string GetPath(string settingsDirectory, Codeplug.System system)
        {
            string identity = $"{system.Name?.Trim().ToUpperInvariant()}\n{system.Address?.Trim().ToUpperInvariant()}\n{system.Port}\n{system.PeerId}";
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            return Path.Combine(settingsDirectory, "RadioAliases", key + ".csv");
        }

        public static IReadOnlyDictionary<int, string> Load(string settingsDirectory, Codeplug.System system)
        {
            string path = GetPath(settingsDirectory, system);
            if (!File.Exists(path))
                return null;
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Cached radio alias file is too large.");
            return AliasTools.ParseNetworkAliases(File.ReadAllBytes(path));
        }

        public static void Save(string settingsDirectory, Codeplug.System system, byte[] data)
        {
            // Validate before replacing a previous successful download.
            AliasTools.ParseNetworkAliases(data);
            string path = GetPath(settingsDirectory, system);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, data);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
