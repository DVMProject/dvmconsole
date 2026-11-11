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

using System.Text;

using dvmconsole.Controls;

using fnecore;

namespace dvmconsole
{
    /// <summary>
    /// Implements the main window wiring for the FNE console patch status registry.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Patch held in an FNE registry by another console, matched against one of our resources.
        /// </summary>
        private class RegistryPatch
        {
            public string ConsoleName;
            public string SystemName;
            public PatchStatusPatch Patch;
        } // private class RegistryPatch

        /*
        ** Methods
        */

        /// <summary>
        /// Redraws patch indicators when the registry contents received from an FNE change.
        /// </summary>
        private void PatchStatusService_RegistryUpdated(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
                UpdatePatchMemberIndicators(FilterPatchMemberships(currentPatchMemberships), patchGroupEnabledStates)));
        }

        /// <summary>
        /// Finds the patches other consoles hold that include a resource.
        /// </summary>
        /// <param name="channel">Channel resource.</param>
        /// <param name="registries">Registry contents received from each FNE.</param>
        /// <returns>Matching patches; this console's own records are excluded, as local state already covers them.</returns>
        /// <remarks>
        /// A resource is only matched against the registry of the FNE it is configured on.
        /// </remarks>
        private static List<RegistryPatch> FindRegistryPatches(ChannelBox channel, List<PatchStatusService.RegistryView> registries)
        {
            List<RegistryPatch> result = new List<RegistryPatch>();

            Codeplug.Channel cpgChannel = channel?.ConfiguredChannel;
            if (cpgChannel == null || !uint.TryParse(cpgChannel.Tgid?.Trim(), out uint tgid) || tgid == 0)
                return result;

            foreach (PatchStatusService.RegistryView registry in registries)
            {
                if (!ResourceIdentity.SystemMatches(registry.SystemName, channel.ResourceSystemName))
                    continue;

                foreach (PatchStatusPeer peer in registry.Peers)
                {
                    if (peer.PeerId == registry.LocalPeerId)
                        continue;

                    string consoleName = string.IsNullOrWhiteSpace(peer.PeerName)
                        ? $"Peer {peer.PeerId}"
                        : $"{peer.PeerName.Trim()} (peer {peer.PeerId})";

                    foreach (PatchStatusPatch patch in peer.Patches ?? new List<PatchStatusPatch>())
                    {
                        if (!patch.Active || patch.Members == null)
                            continue;

                        if (patch.Members.Any(member => IsRegistryMemberForChannel(member, patch, registry.SystemName, cpgChannel, tgid)))
                            result.Add(new RegistryPatch { ConsoleName = consoleName, SystemName = registry.SystemName, Patch = patch });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Determines whether a registry patch member refers to one of our channels.
        /// </summary>
        /// <remarks>
        /// Member system names are the publishing console's own codeplug labels and need not match ours. A patch
        /// confined to one system can only be on the FNE that reported it, so its labels are ignored; when a patch
        /// spans several systems the label is the only way to tell which members belong to this FNE, so it must
        /// match our system name.
        /// </remarks>
        private static bool IsRegistryMemberForChannel(PatchStatusMember member, PatchStatusPatch patch, string systemName,
            Codeplug.Channel cpgChannel, uint tgid)
        {
            if (member == null || member.Tgid != tgid)
                return false;

            Codeplug.ChannelMode mode = cpgChannel.GetChannelMode();
            if (!string.IsNullOrWhiteSpace(member.Mode) && !string.Equals(member.Mode, "unknown", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Mode, mode.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (mode == Codeplug.ChannelMode.DMR && member.Slot != 0 && member.Slot != (cpgChannel.Slot >= 2 ? 2 : 1))
                return false;

            bool spansSystems = patch.Members
                .Select(m => m?.System?.Trim() ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;

            return !spansSystems || ResourceIdentity.SystemMatches(member.System, systemName);
        }

        /// <summary>
        /// Builds the patch indicator tooltip for a resource, listing every patch it is part of.
        /// </summary>
        /// <param name="channel">Channel resource.</param>
        /// <param name="memberships">This console's patch-type group memberships.</param>
        /// <param name="enabledStates">This console's per group enabled states.</param>
        /// <param name="registryPatches">Patches other consoles hold that include this resource.</param>
        /// <returns>Tooltip text.</returns>
        private string BuildPatchIndicatorToolTip(ChannelBox channel,
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships,
            Dictionary<string, bool> enabledStates, List<RegistryPatch> registryPatches)
        {
            StringBuilder sb = new StringBuilder();
            string channelKey = BuildPatchTargetKey(channel.ResourceSystemName, channel.DstId);

            List<KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>>> localGroups =
                (memberships ?? new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>())
                    .Where(kvp => (kvp.Value ?? new List<SettingsManager.PatchTalkgroupMember>())
                        .Any(member => member != null && BuildPatchTargetKey(member.SystemName, member.Tgid) == channelKey))
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (localGroups.Count > 0)
            {
                sb.AppendLine("This console");
                foreach (KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>> group in localGroups)
                {
                    bool enabled = enabledStates != null && enabledStates.TryGetValue(group.Key, out bool isEnabled) && isEnabled;
                    bool oneWay = patchGroupModes.TryGetValue(group.Key, out bool isOneWay) && isOneWay;
                    sb.AppendLine($"  {group.Key} ({(enabled ? "enabled" : "disabled")}, {(oneWay ? "one-way" : "two-way")})");

                    foreach (SettingsManager.PatchTalkgroupMember member in group.Value.Where(m => m != null))
                    {
                        Codeplug.Channel memberChannel = PatchStatusService.FindChannel(Codeplug, member.SystemName, member.Tgid);
                        string name = string.IsNullOrWhiteSpace(memberChannel?.Name) ? string.Empty : $"{memberChannel.Name.Trim()} - ";
                        string self = BuildPatchTargetKey(member.SystemName, member.Tgid) == channelKey ? "  (this channel)" : string.Empty;
                        sb.AppendLine($"    {name}{member.SystemName?.Trim()} TG {member.Tgid?.Trim()}{self}");
                    }
                }
            }

            foreach (IGrouping<string, RegistryPatch> console in registryPatches
                .GroupBy(p => p.ConsoleName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (sb.Length > 0)
                    sb.AppendLine();

                sb.AppendLine(console.Key);
                foreach (RegistryPatch registryPatch in console.OrderBy(p => p.Patch.PatchId, StringComparer.OrdinalIgnoreCase))
                {
                    PatchStatusPatch patch = registryPatch.Patch;
                    sb.AppendLine($"  {patch.PatchId} ({(patch.OneWay ? "one-way" : "two-way")})");

                    foreach (PatchStatusMember member in patch.Members.Where(m => m != null))
                    {
                        string mode = string.Equals(member.Mode, "unknown", StringComparison.OrdinalIgnoreCase)
                            ? string.Empty : $" {member.Mode?.ToUpperInvariant()}";
                        string slot = member.Slot != 0 ? $" TS{member.Slot}" : string.Empty;
                        string self = uint.TryParse(channel.ConfiguredChannel?.Tgid?.Trim(), out uint tgid) &&
                            IsRegistryMemberForChannel(member, patch, registryPatch.SystemName, channel.ConfiguredChannel, tgid)
                                ? "  (this channel)" : string.Empty;
                        sb.AppendLine($"    {member.System?.Trim()}{mode} TG {member.Tgid}{slot}{self}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Recomputes the active patch and multiselect groups and hands them to the patch status service.
        /// </summary>
        /// <param name="publishImmediately">True to publish now rather than waiting for the next heartbeat.</param>
        /// <param name="reason">Reason recorded in the log.</param>
        /// <remarks>
        /// This mirrors exactly what is applied to the patch manager, so it must be called wherever
        /// <see cref="PatchManager.ApplyMemberships"/> is.
        /// </remarks>
        private void PublishActiveGroups(bool publishImmediately, string reason)
        {
            if (patchStatusService == null)
                return;

            try
            {
                patchStatusService.UpdateActiveGroups(
                    FilterPatchMemberships(currentPatchMemberships, patchGroupEnabledStates),
                    patchGroupModes,
                    FilterMultiSelectMemberships(currentPatchMemberships),
                    publishImmediately, reason);
            }
            catch (Exception ex)
            {
                Log.WriteError($"Failed to update patch status: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a peer system with the patch status service.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        /// <param name="peer">Peer system.</param>
        private void RegisterPatchStatusPeer(string systemName, PeerSystem peer)
        {
            patchStatusService?.RegisterPeer(systemName, peer);
        }

        /// <summary>
        /// Unregisters a peer system from the patch status service.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        private void UnregisterPatchStatusPeer(string systemName)
        {
            patchStatusService?.UnregisterPeer(systemName);
        }

        /// <summary>
        /// Notifies the patch status service that a peer system lost its FNE connection.
        /// </summary>
        /// <param name="systemName">Codeplug system name.</param>
        private void OnPatchStatusPeerDisconnected(string systemName)
        {
            patchStatusService?.PeerDisconnected(systemName);
        }
    } // public partial class MainWindow
} // namespace dvmconsole
