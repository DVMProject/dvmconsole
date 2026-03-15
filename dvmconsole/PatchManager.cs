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

namespace dvmconsole
{
    /// <summary>
    /// Runtime manager for patch group forwarding state.
    /// </summary>
    public class PatchManager
    {
        private sealed class PatchMember
        {
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;
            public string Key => BuildKey(SystemName, Tgid);
        }

        private sealed class ActiveSource
        {
            public string SourceKey { get; set; } = string.Empty;
            public uint StreamId { get; set; }
            public uint SourceId { get; set; }
        }

        private sealed class ForwardTarget
        {
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;
            public string MemberKey { get; set; } = string.Empty;
            public uint StreamId { get; set; }
            public uint OutboundSourceId { get; set; }
        }

        private sealed class GroupRuntime
        {
            public string GroupName { get; set; } = string.Empty;
            public List<PatchMember> Members { get; set; } = new List<PatchMember>();
            public ActiveSource Source { get; set; }
            public Dictionary<string, ForwardTarget> ActiveTargets { get; set; } = new Dictionary<string, ForwardTarget>();
        }

        private readonly struct StartWorkItem
        {
            public StartWorkItem(string groupName, int generation, string sourceKey, uint sourceStreamId, PatchMember member, uint sourceId, bool passthrough)
            {
                GroupName = groupName;
                Generation = generation;
                SourceKey = sourceKey;
                SourceStreamId = sourceStreamId;
                Member = member;
                SourceId = sourceId;
                Passthrough = passthrough;
            }

            public string GroupName { get; }
            public int Generation { get; }
            public string SourceKey { get; }
            public uint SourceStreamId { get; }
            public PatchMember Member { get; }
            public uint SourceId { get; }
            public bool Passthrough { get; }
        }

        private readonly struct StopWorkItem
        {
            public StopWorkItem(string systemName, string tgid, uint streamId, uint sourceId)
            {
                SystemName = systemName;
                Tgid = tgid;
                StreamId = streamId;
                SourceId = sourceId;
            }

            public string SystemName { get; }
            public string Tgid { get; }
            public uint StreamId { get; }
            public uint SourceId { get; }
        }

        private readonly struct AudioWorkItem
        {
            public AudioWorkItem(string systemName, string tgid, uint sourceId)
            {
                SystemName = systemName;
                Tgid = tgid;
                SourceId = sourceId;
            }

            public string SystemName { get; }
            public string Tgid { get; }
            public uint SourceId { get; }
        }

        private readonly Func<string, string, uint, uint> beginForward;
        private readonly Action<string, string, uint, uint> endForward;
        private readonly Action<string, string, byte[], uint> sendForwardAudio;
        private readonly Func<string, string, uint> getFallbackSourceId;

        private readonly object sync = new object();
        private readonly Dictionary<string, GroupRuntime> groups = new Dictionary<string, GroupRuntime>();
        private readonly HashSet<string> outboundStreams = new HashSet<string>();
        private readonly Dictionary<string, DateTime> recentlyEndedOutboundStreams = new Dictionary<string, DateTime>();
        private readonly HashSet<string> activeTargetKeys = new HashSet<string>();

        private bool sourceIdPassthrough = false;
        private int membershipGeneration = 0;
        private static readonly TimeSpan LatePacketSuppressWindow = TimeSpan.FromMilliseconds(2000);

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchManager"/> class.
        /// </summary>
        /// <param name="beginForward"></param>
        /// <param name="endForward"></param>
        /// <param name="sendForwardAudio"></param>
        /// <param name="getFallbackSourceId"></param>
        public PatchManager(
            Func<string, string, uint, uint> beginForward,
            Action<string, string, uint, uint> endForward,
            Action<string, string, byte[], uint> sendForwardAudio,
            Func<string, string, uint> getFallbackSourceId)
        {
            this.beginForward = beginForward;
            this.endForward = endForward;
            this.sendForwardAudio = sendForwardAudio;
            this.getFallbackSourceId = getFallbackSourceId;
        }

        /// <summary>
        /// Sets source ID passthrough behavior.
        /// </summary>
        /// <param name="enabled"></param>
        public void SetSourceIdPassthrough(bool enabled)
        {
            lock (sync)
                sourceIdPassthrough = enabled;
        }

        /// <summary>
        /// Applies committed patch memberships transactionally.
        /// </summary>
        /// <param name="memberships"></param>
        public void ApplyMemberships(Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships)
        {
            Dictionary<string, List<PatchMember>> incoming = NormalizeMemberships(memberships);
            List<StopWorkItem> stops = new List<StopWorkItem>();

            lock (sync)
            {
                if (MembershipsEqual(incoming))
                    return;

                membershipGeneration++;

                List<string> keysToRemove = groups.Keys
                    .Where(k => !incoming.ContainsKey(k) || !MembersEqual(groups[k].Members, incoming[k]))
                    .ToList();

                foreach (string key in keysToRemove)
                {
                    GroupRuntime group = groups[key];
                    CollectAndClearStops(group, stops);
                    groups.Remove(key);
                }

                foreach (KeyValuePair<string, List<PatchMember>> kvp in incoming)
                {
                    if (groups.ContainsKey(kvp.Key))
                        continue;

                    groups[kvp.Key] = new GroupRuntime
                    {
                        GroupName = kvp.Key,
                        Members = kvp.Value
                    };
                }

            }

            ExecuteStops(stops);
        }

        /// <summary>
        /// Handles call start from an inbound member.
        /// </summary>
        public void HandleCallStart(string systemName, string tgid, uint streamId, uint sourceId)
        {
            string sourceKey = BuildKey(systemName, tgid);
            if (IsPatchedTransmitStream(systemName, tgid, streamId))
                return;

            List<StartWorkItem> starts = new List<StartWorkItem>();
            lock (sync)
            {
                bool passthrough = sourceIdPassthrough;
                int generation = membershipGeneration;

                foreach (GroupRuntime group in groups.Values.Where(g => g.Members.Any(m => m.Key == sourceKey)))
                {
                    if (group.Source != null)
                        continue;

                    group.Source = new ActiveSource
                    {
                        SourceKey = sourceKey,
                        StreamId = streamId,
                        SourceId = sourceId
                    };

                    foreach (PatchMember member in group.Members.Where(m => m.Key != sourceKey))
                        starts.Add(new StartWorkItem(group.GroupName, generation, sourceKey, streamId, member, sourceId, passthrough));
                }
            }

            ExecuteStarts(starts);
        }

        /// <summary>
        /// Handles inbound audio from a potential patch source.
        /// </summary>
        public void HandleAudio(string systemName, string tgid, uint streamId, uint sourceId, byte[] pcm)
        {
            string sourceKey = BuildKey(systemName, tgid);
            if (IsPatchedTransmitStream(systemName, tgid, streamId))
                return;

            List<StartWorkItem> starts = new List<StartWorkItem>();
            List<AudioWorkItem> sends = new List<AudioWorkItem>();
            lock (sync)
            {
                bool passthrough = sourceIdPassthrough;
                int generation = membershipGeneration;

                foreach (GroupRuntime group in groups.Values.Where(g => g.Members.Any(m => m.Key == sourceKey)))
                {
                    if (group.Source == null)
                    {
                        group.Source = new ActiveSource
                        {
                            SourceKey = sourceKey,
                            StreamId = streamId,
                            SourceId = sourceId
                        };

                        foreach (PatchMember member in group.Members.Where(m => m.Key != sourceKey))
                            starts.Add(new StartWorkItem(group.GroupName, generation, sourceKey, streamId, member, sourceId, passthrough));
                    }

                    if (group.Source.SourceKey != sourceKey || group.Source.StreamId != streamId)
                        continue;

                    sends.AddRange(group.ActiveTargets.Values.Select(t => new AudioWorkItem(t.SystemName, t.Tgid, t.OutboundSourceId)));
                }
            }

            ExecuteStarts(starts);

            foreach (AudioWorkItem send in sends)
                sendForwardAudio(send.SystemName, send.Tgid, pcm, send.SourceId);
        }

        /// <summary>
        /// Handles call end from an inbound member.
        /// </summary>
        public void HandleCallEnd(string systemName, string tgid, uint streamId)
        {
            string sourceKey = BuildKey(systemName, tgid);
            List<StopWorkItem> stops = new List<StopWorkItem>();

            lock (sync)
            {
                foreach (GroupRuntime group in groups.Values)
                {
                    if (group.Source == null)
                        continue;
                    if (group.Source.SourceKey != sourceKey || group.Source.StreamId != streamId)
                        continue;

                    CollectAndClearStops(group, stops);
                    group.Source = null;
                }
            }

            ExecuteStops(stops);
        }

        /// <summary>
        /// Determines if a stream belongs to patch-generated traffic.
        /// </summary>
        public bool IsPatchedTransmitStream(string systemName, string tgid, uint streamId)
        {
            lock (sync)
            {
                CleanupExpiredRecentlyEndedUnsafe();
                string streamKey = BuildStreamKey(systemName, tgid, streamId);
                return outboundStreams.Contains(streamKey) || recentlyEndedOutboundStreams.ContainsKey(streamKey);
            }
        }

        /// <summary>
        /// Determines if a member is currently forwarding as a patch destination.
        /// </summary>
        public bool IsForwardTargetActive(string systemName, string tgid)
        {
            lock (sync)
                return activeTargetKeys.Contains(BuildKey(systemName, tgid));
        }

        /// <summary>
        /// Executes deferred start operations and reconciles state safely.
        /// </summary>
        /// <param name="starts"></param>
        private void ExecuteStarts(List<StartWorkItem> starts)
        {
            foreach (StartWorkItem start in starts)
            {
                uint outboundSourceId = start.Passthrough && start.SourceId != 0
                    ? start.SourceId
                    : getFallbackSourceId(start.Member.SystemName, start.Member.Tgid);

                uint streamId = beginForward(start.Member.SystemName, start.Member.Tgid, outboundSourceId);
                if (streamId == 0)
                    continue;

                bool accepted = false;
                lock (sync)
                {
                    if (membershipGeneration == start.Generation &&
                        groups.TryGetValue(start.GroupName, out GroupRuntime group) &&
                        group.Source != null &&
                        group.Source.SourceKey == start.SourceKey &&
                        group.Source.StreamId == start.SourceStreamId &&
                        group.Members.Any(m => m.Key == start.Member.Key) &&
                        !group.ActiveTargets.ContainsKey(start.Member.Key))
                    {
                        group.ActiveTargets[start.Member.Key] = new ForwardTarget
                        {
                            SystemName = start.Member.SystemName,
                            Tgid = start.Member.Tgid,
                            MemberKey = start.Member.Key,
                            StreamId = streamId,
                            OutboundSourceId = outboundSourceId
                        };

                        string streamKey = BuildStreamKey(start.Member.SystemName, start.Member.Tgid, streamId);
                        outboundStreams.Add(streamKey);
                        recentlyEndedOutboundStreams.Remove(streamKey);
                        activeTargetKeys.Add(start.Member.Key);
                        accepted = true;
                    }
                }

                if (!accepted)
                    endForward(start.Member.SystemName, start.Member.Tgid, streamId, outboundSourceId);
            }
        }

        /// <summary>
        /// Executes deferred stop operations.
        /// </summary>
        /// <param name="stops"></param>
        private void ExecuteStops(List<StopWorkItem> stops)
        {
            foreach (StopWorkItem stop in stops)
                endForward(stop.SystemName, stop.Tgid, stop.StreamId, stop.SourceId);
        }

        /// <summary>
        /// Collects and removes active targets for a group into deferred stop work.
        /// </summary>
        /// <param name="group"></param>
        /// <param name="stops"></param>
        private void CollectAndClearStops(GroupRuntime group, List<StopWorkItem> stops)
        {
            foreach (ForwardTarget target in group.ActiveTargets.Values)
            {
                stops.Add(new StopWorkItem(target.SystemName, target.Tgid, target.StreamId, target.OutboundSourceId));
                string streamKey = BuildStreamKey(target.SystemName, target.Tgid, target.StreamId);
                outboundStreams.Remove(streamKey);
                recentlyEndedOutboundStreams[streamKey] = DateTime.UtcNow + LatePacketSuppressWindow;
                activeTargetKeys.Remove(target.MemberKey);
            }

            group.ActiveTargets.Clear();
        }

        /// <summary>
        /// Normalizes an incoming membership document.
        /// </summary>
        /// <param name="memberships"></param>
        /// <returns></returns>
        private static Dictionary<string, List<PatchMember>> NormalizeMemberships(Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships)
        {
            Dictionary<string, List<PatchMember>> normalized = new Dictionary<string, List<PatchMember>>();
            foreach (KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>> kvp in memberships ?? new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>())
            {
                List<PatchMember> members = (kvp.Value ?? new List<SettingsManager.PatchTalkgroupMember>())
                    .Where(m => !string.IsNullOrWhiteSpace(m?.SystemName) && !string.IsNullOrWhiteSpace(m?.Tgid))
                    .GroupBy(m => BuildKey(m.SystemName, m.Tgid))
                    .Select(g => new PatchMember
                    {
                        SystemName = g.First().SystemName.Trim(),
                        Tgid = g.First().Tgid.Trim()
                    })
                    .ToList();

                if (members.Count > 0)
                    normalized[kvp.Key] = members;
            }

            return normalized;
        }

        /// <summary>
        /// Determines whether current runtime memberships equal incoming memberships.
        /// </summary>
        /// <param name="incoming"></param>
        /// <returns></returns>
        private bool MembershipsEqual(Dictionary<string, List<PatchMember>> incoming)
        {
            if (groups.Count != incoming.Count)
                return false;

            foreach (KeyValuePair<string, List<PatchMember>> kvp in incoming)
            {
                if (!groups.TryGetValue(kvp.Key, out GroupRuntime group))
                    return false;
                if (!MembersEqual(group.Members, kvp.Value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether two member lists are equal by normalized identity.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        private static bool MembersEqual(List<PatchMember> left, List<PatchMember> right)
        {
            HashSet<string> leftKeys = new HashSet<string>((left ?? new List<PatchMember>()).Select(m => m.Key));
            HashSet<string> rightKeys = new HashSet<string>((right ?? new List<PatchMember>()).Select(m => m.Key));
            return leftKeys.SetEquals(rightKeys);
        }

        /// <summary>
        /// Builds normalized member key.
        /// </summary>
        private static string BuildKey(string systemName, string tgid)
        {
            return $"{(systemName ?? string.Empty).Trim().ToLowerInvariant()}|{(tgid ?? string.Empty).Trim()}";
        }

        /// <summary>
        /// Builds normalized stream key.
        /// </summary>
        private static string BuildStreamKey(string systemName, string tgid, uint streamId)
        {
            return $"{BuildKey(systemName, tgid)}|{streamId}";
        }

        /// <summary>
        /// Removes expired entries from recently ended outbound stream suppression.
        /// </summary>
        private void CleanupExpiredRecentlyEndedUnsafe()
        {
            DateTime now = DateTime.UtcNow;
            List<string> expired = recentlyEndedOutboundStreams
                .Where(kvp => kvp.Value <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expired)
                recentlyEndedOutboundStreams.Remove(key);
        }

    }
}
