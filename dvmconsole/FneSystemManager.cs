// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class FneSystemManager
    {
        private readonly Dictionary<string, PeerSystem> peerHandlers;

        /*
        ** Methods
        */

        /// <summary>
        /// Creates an instance of <see cref="PeerSystem"/> class.
        /// </summary>
        public FneSystemManager()
        {
            peerHandlers = new Dictionary<string, PeerSystem>();
        }

        /// <summary>
        /// Create a new <see cref="PeerSystem"/> for a new system
        /// </summary>
        /// <param name="systemId"></param>
        public void AddFneSystem(string systemId, Codeplug.System system, MainWindow mainWindow)
        {
            if (!peerHandlers.ContainsKey(systemId))
                peerHandlers[systemId] = new PeerSystem(mainWindow, system);
        }

        /// <summary>
        /// Replaces any existing system handler with a new instance.
        /// </summary>
        public PeerSystem AddOrReplaceFneSystem(string systemId, Codeplug.System system, MainWindow mainWindow)
        {
            RemoveFneSystem(systemId);
            PeerSystem handler = new PeerSystem(mainWindow, system);
            peerHandlers[systemId] = handler;
            return handler;
        }

        /// <summary>
        /// Return a <see cref="PeerSystem"/> by looking up a systemid
        /// </summary>
        /// <param name="systemId"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public PeerSystem GetFneSystem(string systemId)
        {
            if (peerHandlers.TryGetValue(systemId, out var handler))
                return handler;

            return null;
            //throw new KeyNotFoundException($"WebSocketHandler for system '{systemId}' not found.");
        }

        /// <summary>
        /// Delete a <see cref="Peer"/> by system id
        /// </summary>
        /// <param name="systemId"></param>
        public void RemoveFneSystem(string systemId)
        {
            if (peerHandlers.TryGetValue(systemId, out var handler))
            {
                handler.Stop();
                peerHandlers.Remove(systemId);
            }
        }

        /// <summary>
        /// Check if the manager has a handler
        /// </summary>
        /// <param name="systemId"></param>
        /// <returns></returns>
        public bool HasFneSystem(string systemId)
        {
            return peerHandlers.ContainsKey(systemId);
        }

        /// <summary>
        /// Cleanup
        /// </summary>
        public void ClearAll()
        {
            foreach (var handler in peerHandlers.Values)
                handler.Stop();

            peerHandlers.Clear();
        }
    } // public class FneSystemManager
} // namespace dvmconsole
