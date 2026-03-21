// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2023 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.Net;
using System.Reflection;
using System.Linq;

using fnecore;
using fnecore.Utility;
using NAudio.Mixer;
using static dvmconsole.Codeplug;

namespace dvmconsole
{
    /// <summary>
    /// Implements a peer FNE router system.
    /// </summary>
    public class PeerSystem : FneSystemBase
    {
        public FnePeer peer;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="PeerSystem"/> class.
        /// </summary>
        public PeerSystem(MainWindow mainWindow, Codeplug.System system) : base(Create(system), mainWindow)
        {
            peer = (FnePeer)fne;
        }

        /// <summary>
        /// Internal helper to instantiate a new instance of <see cref="FnePeer"/> class.
        /// </summary>
        /// <returns><see cref="FnePeer"/></returns>
        private static FnePeer Create(Codeplug.System system)
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, system.Port);

            if (system.Address == null)
                throw new NullReferenceException("address");
            if (system.Address == string.Empty)
                throw new ArgumentException("address");

            // handle using address as IP or resolving from hostname to IP
            try
            {
                endpoint = new IPEndPoint(IPAddress.Parse(system.Address), system.Port);
            }
            catch (FormatException)
            {
                IPAddress[] addresses = Dns.GetHostAddresses(system.Address);
                if (addresses.Length > 0)
                    endpoint = new IPEndPoint(addresses[0], system.Port);
            }

            string key = system.Encrypted ? system.PresharedKey : null;

            FnePeer peer = new FnePeer("DVMCONSOLE", system.PeerId, endpoint, key);

            Assembly asm = Assembly.GetExecutingAssembly();
            SemVersion _SEM_VERSION = new SemVersion(asm);

            string software = $"CONSOLE_R{_SEM_VERSION.Major.ToString("D2")}A{_SEM_VERSION.Minor.ToString("D2")}";

            if (system.Identity == null)
                system.Identity = system.PeerId.ToString();
            if (system.Identity.Length == 0)
                system.Identity = system.PeerId.ToString();

            // set configuration parameters
            peer.Passphrase = system.Password;
            peer.Information = new PeerInformation
            {
                Details = new PeerDetails
                {
                    ConventionalPeer = true,
                    PeerClass = PeerConnectionClass.PEER_CONN_CLASS_CONSOLE,
                    Software = software,
                    Identity = system.Identity
                }
            };

            peer.PingTime = 5;

            peer.PeerConnected += Peer_PeerConnected;

            return peer;
        }

        /// <summary>
        /// Event action that handles when a peer connects.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Peer_PeerConnected(object sender, PeerConnectedEvent e)
        {
            /* stub */
        }

        /// <summary>
        /// Helper to send a activity transfer message to the master.
        /// </summary>
        /// <param name="message">Message to send</param>
        public void SendActivityTransfer(string message)
        {
            /* stub */
        }

        /// <summary>
        /// Helper to send a diagnostics transfer message to the master.
        /// </summary>
        /// <param name="message">Message to send</param>
        public void SendDiagnosticsTransfer(string message)
        {
            /* stub */
        }

        /// <summary>
        /// Returns whether the configured channel talkgroup is currently available on this FNE.
        /// If the peer has not announced any talkgroups yet, this allows traffic to avoid false blocks.
        /// </summary>
        public bool IsTalkgroupAvailable(Codeplug.Channel channel)
        {
            if (peer == null || channel == null)
                return true;

            if (!uint.TryParse(channel.Tgid, out uint talkgroupId))
                return true;

            TalkgroupEntry[] snapshot;
            try
            {
                snapshot = peer.AnnouncedTGs?.ToArray() ?? Array.Empty<TalkgroupEntry>();
            }
            catch (InvalidOperationException)
            {
                // The peer updates the announced TG list from a network thread.
                // If we race a mutation, fail open for this attempt rather than blocking valid traffic.
                return true;
            }

            if (snapshot.Length == 0)
                return true;

            TalkgroupEntry[] matchingEntries = snapshot
                .Where(entry => entry.ID == talkgroupId)
                .ToArray();

            if (matchingEntries.Length == 0)
                return false;

            bool isDmr = string.Equals(channel.Mode, "dmr", StringComparison.OrdinalIgnoreCase);
            if (!isDmr)
                return matchingEntries.Any(entry => !entry.Invalid);

            byte desiredSlot = NormalizeChannelSlot(channel.Slot);
            if (matchingEntries.Any(entry => !entry.Invalid && NormalizeAnnouncedSlot(entry.Slot) == desiredSlot))
                return true;

            // Some rule pushes may not carry a meaningful DMR slot for every entry.
            // If the matching TG has no standard slot information at all, allow any active entry.
            bool hasStandardSlotInfo = matchingEntries.Any(entry => NormalizeAnnouncedSlot(entry.Slot) <= 1);
            if (!hasStandardSlotInfo)
                return matchingEntries.Any(entry => !entry.Invalid);

            return false;
        }

        public string DescribeTalkgroupAvailability(Codeplug.Channel channel)
        {
            if (peer == null)
                return "peer unavailable";

            if (channel == null)
                return "channel unavailable";

            if (!uint.TryParse(channel.Tgid, out uint talkgroupId))
                return $"unparseable TGID '{channel.Tgid}'";

            TalkgroupEntry[] snapshot;
            try
            {
                snapshot = peer.AnnouncedTGs?.ToArray() ?? Array.Empty<TalkgroupEntry>();
            }
            catch (InvalidOperationException)
            {
                return "announced TG list changed during read";
            }

            if (snapshot.Length == 0)
                return "no announced TG rules received yet";

            TalkgroupEntry[] matchingEntries = snapshot
                .Where(entry => entry.ID == talkgroupId)
                .ToArray();

            if (matchingEntries.Length == 0)
                return $"TG {talkgroupId} not present in announced rules ({snapshot.Length} entries loaded)";

            string matches = string.Join(", ", matchingEntries.Select(entry =>
                $"slot={NormalizeAnnouncedSlot(entry.Slot)} invalid={entry.Invalid} affiliated={entry.Affiliated} nonPreferred={entry.NonPreferred}"));

            if (!string.Equals(channel.Mode, "dmr", StringComparison.OrdinalIgnoreCase))
                return $"TG {talkgroupId} entries: {matches}";

            return $"TG {talkgroupId} requested on DMR slot {NormalizeChannelSlot(channel.Slot) + 1}; entries: {matches}";
        }

        private static byte NormalizeChannelSlot(int slot)
        {
            if (slot <= 1)
                return 0;

            return (byte)(slot - 1);
        }

        private static byte NormalizeAnnouncedSlot(byte slot)
        {
            return (byte)(slot & 0x03);
        }
    } // public class PeerSystem
} // namespace dvmconsole
