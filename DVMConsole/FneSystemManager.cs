// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

namespace DVMConsole
{
    /// <summary>
    /// WhackerLink peer/client websocket manager for having multiple systems
    /// </summary>
    public class FneSystemManager
    {
        private readonly Dictionary<string, PeerSystem> _webSocketHandlers;

        /// <summary>
        /// Creates an instance of <see cref="PeerSystem"/>
        /// </summary>
        public FneSystemManager()
        {
            _webSocketHandlers = new Dictionary<string, PeerSystem>();
        }

        /// <summary>
        /// Create a new <see cref="PeerSystem"/> for a new system
        /// </summary>
        /// <param name="systemId"></param>
        public void AddFneSystem(string systemId, Codeplug.System system, MainWindow mainWindow)
        {
            if (!_webSocketHandlers.ContainsKey(systemId))
            {
                _webSocketHandlers[systemId] = new PeerSystem(mainWindow, system);
            }
        }

        /// <summary>
        /// Return a <see cref="PeerSystem"/> by looking up a systemid
        /// </summary>
        /// <param name="systemId"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public PeerSystem GetFneSystem(string systemId)
        {
            if (_webSocketHandlers.TryGetValue(systemId, out var handler))
            {
                return handler;
            }
            throw new KeyNotFoundException($"WebSocketHandler for system '{systemId}' not found.");
        }

        /// <summary>
        /// Delete a <see cref="Peer"/> by system id
        /// </summary>
        /// <param name="systemId"></param>
        public void RemoveFneSystem(string systemId)
        {
            if (_webSocketHandlers.TryGetValue(systemId, out var handler))
            {
                handler.peer.Stop();
                _webSocketHandlers.Remove(systemId);
            }
        }

        /// <summary>
        /// Check if the manager has a handler
        /// </summary>
        /// <param name="systemId"></param>
        /// <returns></returns>
        public bool HasFneSystem(string systemId)
        {
            return _webSocketHandlers.ContainsKey(systemId);
        }

        /// <summary>
        /// Cleanup
        /// </summary>
        public void ClearAll()
        {
            foreach (var handler in _webSocketHandlers.Values)
            {
                handler.peer.Stop();
            }
            _webSocketHandlers.Clear();
        }
    }
}
