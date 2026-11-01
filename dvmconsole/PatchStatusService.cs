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
*   Copyright (C) 2026 Lorenzo L. Romero, K2LLR
*
*/

using System.Threading;

using fnecore;

namespace dvmconsole
{
    /// <summary>
    /// Publishes the console's active patch and multiselect groups to the FNE console patch status registry.
    /// </summary>
    /// <remarks>
    /// Publishing is heartbeat driven: every tick re-sends the complete active set to every FNE that supports
    /// the registry, and a group that is no longer active is simply absent from the next publish. Callers may
    /// also request an immediate publish for changes that should not wait for the next tick. Each member carries
    /// its own system name, so a patch spanning multiple systems is a single record on every FNE.
    /// </remarks>
    public class PatchStatusService : IDisposable
    {
        /// <summary>
        /// TTL, in seconds, requested for published patch status records.
        /// </summary>
        public const uint TTL_SECONDS = 15;
        /// <summary>
        /// Heartbeat interval; a third of the TTL so a single lost publish does not expire the record.
        /// </summary>
        private const int HEARTBEAT_INTERVAL_MS = (int)(TTL_SECONDS * 1000 / 3);

        private readonly Func<Codeplug> getCodeplug;
        private readonly object sync = new object();

        private readonly Dictionary<string, PeerSystem> peers =
            new Dictionary<string, PeerSystem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventHandler<PatchStatusReceivedEvent>> peerHandlers =
            new Dictionary<string, EventHandler<PatchStatusReceivedEvent>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> supportedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private List<PatchStatusPatch> currentPatches = new List<PatchStatusPatch>();
        private Timer heartbeatTimer;
        private bool disposed;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchStatusService"/> class.
        /// </summary>
        /// <param name="getCodeplug">Accessor for the active codeplug, used to resolve member mode and slot.</param>
        public PatchStatusService(Func<Codeplug> getCodeplug)
        {
            this.getCodeplug = getCodeplug ?? throw new ArgumentNullException(nameof(getCodeplug));
            heartbeatTimer = new Timer(HeartbeatTick, null, HEARTBEAT_INTERVAL_MS, HEARTBEAT_INTERVAL_MS);
        }

        /// <summary>
        /// Registers a peer system for patch status publishing.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        /// <param name="peer">Peer system.</param>
        public void RegisterPeer(string systemName, PeerSystem peer)
        {
            if (string.IsNullOrWhiteSpace(systemName) || peer?.peer == null)
                return;

            UnregisterPeer(systemName);

            EventHandler<PatchStatusReceivedEvent> handler = (sender, e) => OnPatchStatusReceived(systemName, e);

            lock (sync)
            {
                peers[systemName] = peer;
                peerHandlers[systemName] = handler;
            }

            peer.peer.PatchStatusReceived += handler;
        }

        /// <summary>
        /// Unregisters a peer system.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        public void UnregisterPeer(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName))
                return;

            PeerSystem peer;
            EventHandler<PatchStatusReceivedEvent> handler;

            lock (sync)
            {
                peers.Remove(systemName, out peer);
                peerHandlers.Remove(systemName, out handler);
                supportedSystems.Remove(systemName);
            }

            if (peer?.peer != null && handler != null)
                peer.peer.PatchStatusReceived -= handler;
        }

        /// <summary>
        /// Stops publishing to a system until its FNE proves registry support again.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        /// <remarks>
        /// The FNE may come back from a disconnect with the registry disabled, or as a build that predates it,
        /// so support has to be re-established on every connection.
        /// </remarks>
        public void PeerDisconnected(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName))
                return;

            lock (sync)
                supportedSystems.Remove(systemName);
        }

        /// <summary>
        /// Replaces the set of presently active patch and multiselect groups.
        /// </summary>
        /// <param name="activePatchMemberships">Patch-type group memberships, already filtered to active groups.</param>
        /// <param name="oneWayModes">Per group one-way mode. Only consulted for patch-type groups.</param>
        /// <param name="activeMultiSelectMemberships">Multiselect-type group memberships, already filtered to active groups.</param>
        /// <param name="publishImmediately">True to publish now rather than waiting for the next heartbeat.</param>
        /// <param name="reason">Reason recorded in the log.</param>
        public void UpdateActiveGroups(
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> activePatchMemberships,
            Dictionary<string, bool> oneWayModes,
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> activeMultiSelectMemberships,
            bool publishImmediately,
            string reason)
        {
            Codeplug codeplug = getCodeplug();

            List<PatchStatusPatch> patches = new List<PatchStatusPatch>();
            AppendActiveGroups(patches, codeplug, activePatchMemberships, oneWayModes);
            AppendActiveGroups(patches, codeplug, activeMultiSelectMemberships, null);

            lock (sync)
                currentPatches = patches;

            if (publishImmediately)
                PublishAll(reason);
        }

        /// <summary>
        /// Publishes the current patch set to every registered peer whose FNE supports the registry.
        /// </summary>
        /// <param name="reason">Reason recorded in the log.</param>
        /// <remarks>
        /// dvmhost answers a patch status transfer it will not service with a NAK, and fnecore treats an
        /// unsolicited master NAK as a demand to re-login; publishing to an FNE without the registry would drop
        /// the connection. dvmhost pushes a registry snapshot to every console peer once it accepts the login, so
        /// only systems that have sent one are published to.
        /// </remarks>
        private void PublishAll(string reason)
        {
            List<KeyValuePair<string, PeerSystem>> targets;
            List<PatchStatusPatch> patches;

            lock (sync)
            {
                if (disposed)
                    return;

                targets = peers.Where(kvp => supportedSystems.Contains(kvp.Key)).ToList();
                patches = currentPatches;
            }

            foreach (KeyValuePair<string, PeerSystem> target in targets)
            {
                FnePeer peer = target.Value?.peer;
                if (peer == null)
                    continue;

                try
                {
                    // patches is never mutated once published to currentPatches, and is encoded synchronously
                    peer.PublishPatchStatus(new PatchStatusPublish
                    {
                        // left empty so the FNE substitutes the authenticated connection identity
                        PeerName = string.Empty,
                        TtlSeconds = TTL_SECONDS,
                        Patches = patches
                    });
                }
                catch (Exception ex)
                {
                    Log.WriteError($"({target.Key}) Failed to publish patch status ({reason}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles a registry snapshot received from an FNE.
        /// </summary>
        private void OnPatchStatusReceived(string systemName, PatchStatusReceivedEvent e)
        {
            if (e?.Snapshot == null)
                return;

            bool first;
            lock (sync)
                first = peers.ContainsKey(systemName) && supportedSystems.Add(systemName);

            // the first snapshot on a connection is what enables publishing, so publish right away
            if (first)
            {
                Log.WriteLine($"({systemName}) FNE supports the patch status registry, publishing enabled.");
                PublishAll($"registry available ({systemName})");
            }
        }

        /// <summary>
        /// Periodic heartbeat republish.
        /// </summary>
        /// <remarks>
        /// The FNE only refreshes a record's TTL when the publish sequence advances, which
        /// <see cref="FnePeer.PublishPatchStatus"/> guarantees on every call.
        /// </remarks>
        private void HeartbeatTick(object state)
        {
            PublishAll("heartbeat");
        }

        /// <summary>
        /// Appends one set of active groups to a patch list.
        /// </summary>
        /// <param name="patches">Destination list.</param>
        /// <param name="codeplug">Active codeplug, used to resolve member mode and slot.</param>
        /// <param name="memberships">Active group memberships.</param>
        /// <param name="oneWayModes">Per group one-way mode, or null to report every group as two-way.</param>
        private static void AppendActiveGroups(
            List<PatchStatusPatch> patches,
            Codeplug codeplug,
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships,
            Dictionary<string, bool> oneWayModes)
        {
            if (memberships == null)
                return;

            foreach (KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>> kvp in memberships)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                    continue;

                PatchStatusPatch patch = new PatchStatusPatch
                {
                    PatchId = kvp.Key,
                    Active = true,
                    OneWay = oneWayModes != null && oneWayModes.TryGetValue(kvp.Key, out bool oneWay) && oneWay
                };

                foreach (SettingsManager.PatchTalkgroupMember member in kvp.Value)
                {
                    PatchStatusMember resolved = ResolveMember(codeplug, member);
                    if (resolved != null)
                        patch.Members.Add(resolved);
                }

                // a group with fewer than two members forwards nothing, so it is not advertised; this also drops
                // unresolvable members, as the FNE rejects the whole publish if any member carries a zero TGID
                if (patch.Members.Count >= 2)
                    patches.Add(patch);
            }
        }

        /// <summary>
        /// Resolves a stored patch member to a publishable registry member.
        /// </summary>
        private static PatchStatusMember ResolveMember(Codeplug codeplug, SettingsManager.PatchTalkgroupMember member)
        {
            if (member == null || string.IsNullOrWhiteSpace(member.SystemName) || string.IsNullOrWhiteSpace(member.Tgid))
                return null;

            if (!uint.TryParse(member.Tgid.Trim(), out uint tgid) || tgid == 0)
                return null;

            string mode = "unknown";
            byte slot = 0;

            Codeplug.Channel channel = FindChannel(codeplug, member.SystemName, member.Tgid);
            if (channel != null)
            {
                Codeplug.ChannelMode channelMode = channel.GetChannelMode();
                mode = channelMode.ToString().ToLowerInvariant();
                if (channelMode == Codeplug.ChannelMode.DMR)
                    slot = (byte)(channel.Slot >= 2 ? 2 : 1);
            }

            return new PatchStatusMember
            {
                System = member.SystemName.Trim(),
                Mode = mode,
                Tgid = tgid,
                Slot = slot
            };
        }

        /// <summary>
        /// Finds the codeplug channel backing a system and talkgroup pair.
        /// </summary>
        private static Codeplug.Channel FindChannel(Codeplug codeplug, string systemName, string tgid)
        {
            string wantedTgid = (tgid ?? string.Empty).Trim();

            return (codeplug?.Zones ?? new List<Codeplug.Zone>())
                .SelectMany(zone => zone?.Channels ?? new List<Codeplug.Channel>())
                .FirstOrDefault(channel => channel != null &&
                    ResourceIdentity.SystemMatches(channel.System, systemName) &&
                    string.Equals(channel.Tgid?.Trim() ?? string.Empty, wantedTgid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Releases resources held by the service.
        /// </summary>
        public void Dispose()
        {
            List<string> systemNames;
            lock (sync)
            {
                if (disposed)
                    return;

                disposed = true;
                systemNames = peers.Keys.ToList();
            }

            heartbeatTimer?.Dispose();
            heartbeatTimer = null;

            foreach (string systemName in systemNames)
                UnregisterPeer(systemName);
        }
    } // public class PatchStatusService
} // namespace dvmconsole
