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
    /// Implements the main window wiring for the FNE console patch status registry.
    /// </summary>
    public partial class MainWindow
    {
        /*
        ** Methods
        */

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
