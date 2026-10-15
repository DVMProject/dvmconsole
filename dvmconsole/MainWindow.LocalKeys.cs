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
using System.Windows;
using dvmconsole.Controls;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private EkcKeyStore localKeys;

        /// <summary>
        /// Unlock once per codeplug load, before resources or key requests are started.
        /// </summary>
        private void LoadConfiguredEkcKeys(string codeplugPath)
        {
            if (string.IsNullOrWhiteSpace(Codeplug?.KeyFile))
                return;

            try
            {
                string path = Path.GetFullPath(Codeplug.KeyFile, Path.GetDirectoryName(Path.GetFullPath(codeplugPath)));
                if (!string.Equals(Path.GetExtension(path), ".ekc", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Local keys require a KFDEKC .ekc container. Clear key files are no longer supported.");
                if (!File.Exists(path))
                    throw new InvalidDataException("The configured EKC container was not found.");

                var credentials = new EkcCredentialStore(SettingsManager.UserAppDataPath);
                string password = null;
                try { password = credentials.Load(path); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
                {
                    Log.WriteWarning("Saved EKC password could not be opened; asking for the password again.");
                }

                if (password != null)
                {
                    try { localKeys = EkcKeyStore.Open(path, password); }
                    catch (CryptographicException)
                    {
                        Log.WriteWarning("Saved EKC password no longer unlocks the container; asking again.");
                    }
                    finally { password = null; }
                }

                while (localKeys == null)
                {
                    var prompt = new EkcPasswordWindow(path) { Owner = this };
                    try
                    {
                        if (prompt.ShowDialog() != true)
                        {
                            Log.WriteWarning("EKC unlock canceled; local keys were not loaded.");
                            return;
                        }
                        password = prompt.Password;
                        try { localKeys = EkcKeyStore.Open(path, password); }
                        catch (CryptographicException)
                        {
                            MessageBox.Show(this, "Unable to unlock the EKC container. Check the password. If it is correct, verify the file in KFDEKC.",
                                "Local Keys", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }

                        try { credentials.Save(path, password); }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
                        {
                            Log.WriteWarning("EKC keys loaded, but Windows could not save the protected password.");
                            MessageBox.Show(this, "Keys were loaded, but the password could not be remembered securely. You will be prompted again next time.",
                                "Local Keys", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    finally
                    {
                        password = null;
                        prompt.ClearPassword();
                    }
                }

                // EKC uses the same algorithm identifiers as the FNE key service.
                foreach (Codeplug.Channel channel in (Codeplug.Zones ?? []).SelectMany(zone => zone.Channels ?? []))
                {
                    if (channel.GetChannelMode() != Codeplug.ChannelMode.NXDN || !channel.HasEncryptionConfig())
                        continue;
                    try
                    {
                        if (localKeys.TryGetKey(channel.GetKeyRequestAlgoId(), channel.GetKeyId(), out byte[] material))
                            SetNxdnKey(channel.System, channel.GetNxdnCipherType(), channel.GetKeyId(), material, local: true);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is CryptographicException || ex is InvalidOperationException)
                    {
                        Log.WriteWarning($"Local NXDN key {channel.GetKeyId()} is not usable for {channel.Name}.");
                    }
                }
                Log.WriteLine($"Loaded {localKeys.Count} local EKC traffic keys; skipped {localKeys.SkippedKeys} wrapping or unsupported keys.");
            }
            catch (Exception ex)
            {
                localKeys?.Dispose();
                localKeys = null;
                ClearNxdnKeys();
                // Do not include parser/decryption exception details in logs or dialogs.
                Log.WriteWarning("The configured EKC container could not be loaded.");
                string reason = ex is InvalidDataException ? ex.Message : "Check the configured path and verify the container in KFDEKC.";
                MessageBox.Show(this, $"Local keys were not loaded. {reason}",
                    "Local Keys", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ApplyLocalP25Key(ChannelBox channel, Codeplug.Channel config)
        {
            if (config.GetChannelMode() != Codeplug.ChannelMode.P25 || localKeys == null ||
                !localKeys.TryGetKey(config.GetAlgoId(), config.GetKeyId(), out byte[] material))
                return false;
            channel.Crypter.SetKey(config.GetKeyId(), config.GetAlgoId(), material);
            return true;
        }

        private void ClearLocalKeys()
        {
            localKeys?.Dispose();
            localKeys = null;
        }
    }
}

