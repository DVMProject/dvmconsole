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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using fnecore;
using fnecore.P25.KMM;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

        private void LoadConfiguredKeyFileKeys()
        {
            if (Codeplug?.KeyFile == null)
                return;

            if (!File.Exists(Codeplug.KeyFile))
            {
                MessageBox.Show($"Key file {Codeplug.KeyFile} not found. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var keys = deserializer.Deserialize<KeyContainer>(File.ReadAllText(Codeplug.KeyFile));
            var keysetItems = new Dictionary<int, KeysetItem>();

            foreach (var keyEntry in keys.Keys)
            {
                var keyItem = new KeyItem
                {
                    KeyId = keyEntry.KeyId
                };
                var keyBytes = keyEntry.KeyBytes;
                keyItem.SetKey(keyBytes, (uint)keyBytes.Length);
                if (!keysetItems.ContainsKey(keyEntry.AlgId))
                {
                    var asByte = (byte)keyEntry.AlgId;
                    keysetItems.Add(keyEntry.AlgId, new KeysetItem() { AlgId = asByte });
                }

                keysetItems[keyEntry.AlgId].AddKey(keyItem);
            }

            foreach (var eventData in keysetItems.Select(keyValuePair => keyValuePair.Value).Select(keysetItem => new KeyResponseEvent(0, new KmmModifyKey
                     {
                         AlgId = 0,
                         KeyId = 0,
                         MessageId = 0,
                         MessageLength = 0,
                         KeysetItem = keysetItem
                     }, [])))
            {
                KeyResponseReceived(eventData);
            }
        }
    }
}
