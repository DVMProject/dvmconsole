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

namespace dvmconsole.Controls
{
    public partial class ChannelBox
    {
        private readonly HashSet<object> pttInputs = new();
        private readonly object mousePttInput = new();
        private readonly object legacyPttInput = new();

        /// <summary>
        /// Each input owns its request; the final release ends the call.
        /// </summary>
        internal bool SetPttInput(object source, bool active)
        {
            if (active)
            {
                if (pttInputs.Contains(source))
                    return PttState && !PttTimedOut;
                if (IsLocalPlayback || IsRxOnly || !IsSelected || (CanStartPtt != null && !CanStartPtt(this)))
                    return false;
                ApplyPttState(true);
                if (!PttState || PttTimedOut)
                    return false;
                pttInputs.Add(source);
                return true;
            }

            if (!pttInputs.Remove(source))
                return false;
            if (pttInputs.Count == 0)
                ApplyPttState(false);
            return true;
        }

        /// <summary>
        /// Releases operator inputs immediately for deselection, reload, or shutdown.
        /// </summary>
        internal void ReleaseAllPttInputs()
        {
            pttInputs.Clear();
            CancelPendingPttRelease();
            if (!PttState)
                return;
            PttState = false;
            PTTButtonClicked?.Invoke(null, this);
        }
    }
}
