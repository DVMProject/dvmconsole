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
*
*/

using System.Net;
using System.Reflection;

using fnecore;
using fnecore.Utility;
using NAudio.Mixer;

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
    } // public class PeerSystem
} // namespace dvmconsole
