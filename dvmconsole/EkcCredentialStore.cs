// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 DVMProject Authors
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace dvmconsole
{
    /// <summary>
    /// Passwords protected by Windows for the current user, separate from user settings.
    /// </summary>
    internal sealed class EkcCredentialStore
    {
        private readonly string directory;

        public EkcCredentialStore(string profilePath)
        {
            directory = Path.Combine(profilePath, "EkcCredentials");
        }

        private static byte[] Identity(string path) =>
            SHA256.HashData(Encoding.UTF8.GetBytes("DVMConsole.EKC.v1:" + Path.GetFullPath(path).ToUpperInvariant()));

        private string CredentialPath(byte[] identity) => Path.Combine(directory, Convert.ToHexString(identity) + ".bin");

        public string Load(string containerPath)
        {
            byte[] identity = Identity(containerPath);
            string path = CredentialPath(identity);
            if (!File.Exists(path))
                return null;
            if (new FileInfo(path).Length > 65536)
                throw new InvalidDataException("Invalid saved EKC credential.");
            byte[] plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), identity, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }

        public void Save(string containerPath, string password)
        {
            byte[] identity = Identity(containerPath);
            byte[] plaintext = Encoding.UTF8.GetBytes(password);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plaintext, identity, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }

            Directory.CreateDirectory(directory);
            string path = CredentialPath(identity);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}

