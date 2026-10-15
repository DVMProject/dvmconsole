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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using fnecore;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private const int STARTUP_KEY_REQUEST_DELAY_MS = 5000;
        private const int KEY_REQUEST_SPACING_MS = 100;

        private readonly object deferredKeyRequestSync = new();
        private readonly Dictionary<string, HashSet<string>> deferredStartupKeyRequestsBySystem = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> deferredStartupKeyRequestTimers = new(StringComparer.OrdinalIgnoreCase);
        private bool isRestoringSelectedChannelsOnStartup = false;

        private void QueueMissingChannelKeys(string systemName)
        {
            foreach (var channel in selectedChannelsManager.GetSelectedChannels())
            {
                Codeplug.Channel config = Codeplug?.GetChannelByName(channel.ChannelName);
                if (config == null || !ResourceIdentity.SystemMatches(config.System, systemName) ||
                    !config.HasEncryptionConfig() || channel.HasLoadedTransmitKey())
                    continue;
                QueueStartupKeyRequest(systemName, config.GetKeyRequestAlgoId(), config.GetKeyId());
            }
        }

        private void QueueStartupKeyRequest(string systemName, byte algId, ushort keyId)
        {
            if (string.IsNullOrWhiteSpace(systemName) || algId == 0 || keyId == 0)
                return;

            lock (deferredKeyRequestSync)
            {
                if (!deferredStartupKeyRequestsBySystem.TryGetValue(systemName, out HashSet<string> requests))
                {
                    requests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    deferredStartupKeyRequestsBySystem[systemName] = requests;
                }

                requests.Add(BuildDeferredKeyRequestId(algId, keyId));
            }

            ScheduleDeferredStartupKeyRequests(systemName);
        }

        private void ScheduleDeferredStartupKeyRequests(string systemName)
        {
            FneConnectionEntry entry = GetFneConnectionEntry(systemName);
            if (entry?.IsConnected != true)
                return;

            CancellationTokenSource cts;
            lock (deferredKeyRequestSync)
            {
                if (!deferredStartupKeyRequestsBySystem.TryGetValue(systemName, out HashSet<string> requests) || requests.Count == 0)
                    return;

                if (deferredStartupKeyRequestTimers.TryGetValue(systemName, out CancellationTokenSource existing))
                {
                    existing.Cancel();
                    deferredStartupKeyRequestTimers.Remove(systemName);
                }

                cts = new CancellationTokenSource();
                deferredStartupKeyRequestTimers[systemName] = cts;
            }

            _ = SendDeferredStartupKeyRequestsAsync(systemName, cts);
        }

        private async Task SendDeferredStartupKeyRequestsAsync(string systemName, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(STARTUP_KEY_REQUEST_DELAY_MS, cts.Token);

                FneConnectionEntry entry = GetFneConnectionEntry(systemName);
                PeerSystem fne = fneSystemManager.GetFneSystem(systemName);
                if (entry?.IsConnected != true || fne?.peer == null)
                    return;

                List<(byte AlgId, ushort KeyId)> requestsToSend = new();
                lock (deferredKeyRequestSync)
                {
                    if (!deferredStartupKeyRequestsBySystem.TryGetValue(systemName, out HashSet<string> pending) || pending.Count == 0)
                        return;

                    foreach (string request in pending)
                    {
                        if (TryParseDeferredKeyRequestId(request, out byte algId, out ushort keyId))
                            requestsToSend.Add((algId, keyId));
                    }

                    deferredStartupKeyRequestsBySystem.Remove(systemName);
                    deferredStartupKeyRequestTimers.Remove(systemName);
                }

                foreach ((byte algId, ushort keyId) in requestsToSend)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    if (GetFneConnectionEntry(systemName)?.IsConnected != true)
                        return;

                    if (!TrySendMasterKeyRequest(fne, systemName, algId, keyId))
                        continue;

                    await Task.Delay(KEY_REQUEST_SPACING_MS, cts.Token);
                }
            }
            catch (TaskCanceledException)
            {
                /* stub */
            }
            finally
            {
                lock (deferredKeyRequestSync)
                {
                    if (deferredStartupKeyRequestTimers.TryGetValue(systemName, out CancellationTokenSource current) && current == cts)
                        deferredStartupKeyRequestTimers.Remove(systemName);
                }

                cts.Dispose();
            }
        }

        private void CancelDeferredStartupKeyRequests(string systemName)
        {
            lock (deferredKeyRequestSync)
            {
                if (deferredStartupKeyRequestTimers.TryGetValue(systemName, out CancellationTokenSource existing))
                {
                    existing.Cancel();
                    deferredStartupKeyRequestTimers.Remove(systemName);
                }
            }
        }

        private static string BuildDeferredKeyRequestId(byte algId, ushort keyId)
        {
            return $"{algId}:{keyId}";
        }

        private static bool TryParseDeferredKeyRequestId(string requestId, out byte algId, out ushort keyId)
        {
            algId = 0;
            keyId = 0;

            if (string.IsNullOrWhiteSpace(requestId))
                return false;

            string[] parts = requestId.Split(':');
            if (parts.Length != 2)
                return false;

            return byte.TryParse(parts[0], out algId) && ushort.TryParse(parts[1], out keyId);
        }

        private bool TrySendMasterKeyRequest(PeerSystem fne, Codeplug.System system, byte algId, ushort keyId)
        {
            if (system == null)
                return false;

            return TrySendMasterKeyRequest(fne, system.Name, algId, keyId);
        }

        private bool TrySendMasterKeyRequest(PeerSystem fne, string systemName, byte algId, ushort keyId)
        {
            if (fne?.peer == null || string.IsNullOrWhiteSpace(systemName))
                return false;

            Codeplug.System system = Codeplug?.Systems?.FirstOrDefault(s =>
                string.Equals(s.Name, systemName, StringComparison.OrdinalIgnoreCase));
            if (system == null)
                return false;

            if (!uint.TryParse(system.Rid, out uint consoleRid) || consoleRid == 0)
            {
                Log.WriteWarning($"({system.Name}) Cannot request key ALGID {algId} KID {keyId}; console RID is missing or invalid.");
                return false;
            }

            fne.peer.SendMasterKeyRequest(algId, keyId, consoleRid);
            Log.WriteLine($"({system.Name}) Sent key request RID {consoleRid} ALGID {algId} KID {keyId}");
            return true;
        }

    }
}
