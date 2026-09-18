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

namespace dvmconsole
{
    public partial class MainWindow
    {
        private static void LoadSystemAliases(Codeplug.System system)
        {
            try
            {
                if (File.Exists(system.AliasPath))
                    system.RidAlias = AliasTools.LoadAliases(system.AliasPath);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"({system.Name}) Local RID aliases could not be loaded: {ex.Message}");
            }

            if (!system.SyncRadioAliases)
                return;
            try
            {
                system.NetworkRidAliases = RadioAliasCache.Load(SettingsManager.UserAppDataPath, system);
                if (system.NetworkRidAliases != null)
                    Log.WriteLine($"({system.Name}) Loaded {system.NetworkRidAliases.Count} cached FNE RID aliases.");
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"({system.Name}) Cached RID aliases could not be loaded; using local aliases: {ex.Message}");
            }
        }

        public async Task SyncFneAliasesAsync(string systemName)
        {
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry?.SystemConfig.SyncRadioAliases != true || !entry.IsConnected || entry.AliasSyncCancellation != null)
                return;

            PeerSystem peer = entry.Peer;
            if (peer?.peer == null)
                return;

            using var cancellation = new CancellationTokenSource();
            entry.AliasSyncCancellation = cancellation;
            string previousStatus = entry.AliasStatus;
            entry.AliasStatus = "Syncing...";
            PublishConnectionState(entry);
            Log.WriteLine($"({systemName}) Requesting FNE RID aliases.");
            try
            {
                byte[] data = await peer.peer.RequestRadioAliasesAsync(cancellation.Token);
                var aliases = await Task.Run(() => AliasTools.ParseNetworkAliases(data), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!entry.IsConnected || entry.Peer != peer)
                    return;

                entry.SystemConfig.NetworkRidAliases = aliases;
                entry.AliasStatus = $"{aliases.Count:N0} aliases";
                Log.WriteLine($"({systemName}) FNE RID alias sync complete: {aliases.Count} aliases, {data.Length} bytes.");
                try
                {
                    await Task.Run(() => RadioAliasCache.Save(SettingsManager.UserAppDataPath, entry.SystemConfig, data));
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"({systemName}) RID aliases loaded, but cache could not be saved: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                if (entry.AliasSyncCancellation == cancellation)
                    entry.AliasStatus = previousStatus;
            }
            catch (Exception ex)
            {
                if (entry.AliasSyncCancellation == cancellation)
                {
                    entry.AliasStatus = "Sync failed; aliases retained";
                    Log.WriteWarning($"({systemName}) RID alias sync failed; keeping existing aliases: {ex.Message}");
                }
            }
            finally
            {
                if (entry.AliasSyncCancellation == cancellation)
                {
                    entry.AliasSyncCancellation = null;
                    PublishConnectionState(entry);
                }
            }
        }

        private static void CancelFneAliasSync(FneConnectionEntry entry)
        {
            if (entry?.AliasSyncCancellation == null)
                return;
            entry.AliasSyncCancellation.Cancel();
            entry.AliasSyncCancellation = null;
            entry.AliasStatus = GetSavedAliasStatus(entry.SystemConfig);
        }

        private static string GetSavedAliasStatus(Codeplug.System system)
        {
            if (!system.SyncRadioAliases)
                return "Local aliases";
            return system.NetworkRidAliases == null ? "Not synced" : $"{system.NetworkRidAliases.Count:N0} aliases (cached)";
        }
    }
}
