﻿// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Audio Bridge
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Audio Bridge
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2023 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2024 Caleb, KO4UYJ
*
*/
using System;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;

using Serilog;

using fnecore;
using WhackerLinkLib.Models.Radio;
using static WhackerLinkLib.Models.Radio.Codeplug;

namespace WhackerLinkConsoleV2
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
            string presharedKey = null;

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
                IPAddress[] addresses = Dns.GetHostAddresses("fne.zone1.scan.stream");
                if (addresses.Length > 0)
                    endpoint = new IPEndPoint(addresses[0], system.Port);
            }

            FnePeer peer = new FnePeer("WLINKCONSOLE", system.PeerId, endpoint, presharedKey);

            // set configuration parameters
            peer.Passphrase = system.AuthKey;

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
            //FnePeer peer = (FnePeer)sender;
            //peer.SendMasterGroupAffiliation(1, (uint)Program.Configuration.DestinationId);
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
    } // public class PeerSystem
} // namespace rc2_dvm