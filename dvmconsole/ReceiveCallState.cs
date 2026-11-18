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
*
*/

namespace dvmconsole
{
    /// <summary>
    /// Tracks one resource's received call independently of display names. Evaluate
    /// before changing call state; accept only after the mode's packet validation.
    /// </summary>
    internal sealed class ReceiveCallState
    {
        internal enum Transition { Ignore, Continue, Start, End }

        public bool Active { get; private set; }
        public uint PeerId { get; private set; }
        public uint StreamId { get; private set; }
        public uint SourceId { get; private set; }
        /// <summary>
        /// Digital source changes split logical calls even when an upstream source
        /// reuses its stream ID. Analog permits late source updates such as MDC.
        /// </summary>
        public Transition Evaluate(uint peerId, uint streamId, uint sourceId,
            bool terminator, bool allowSourceUpdate)
        {
            if (streamId == 0)
                return Transition.Ignore;

            bool sameStream = Active && StreamId == streamId && PeerId == peerId;
            if (terminator)
                return sameStream && (allowSourceUpdate || sourceId == 0 || SourceId == sourceId)
                    ? Transition.End : Transition.Ignore;

            if (sourceId == 0 || (Active && StreamId == streamId && PeerId != peerId))
                return Transition.Ignore;

            return !sameStream || (!allowSourceUpdate && SourceId != 0 && SourceId != sourceId)
                ? Transition.Start : Transition.Continue;
        }

        /// <summary>Records the identity after the caller accepts a packet.</summary>
        public void Accept(uint peerId, uint streamId, uint sourceId)
        {
            Active = true;
            PeerId = peerId;
            StreamId = streamId;
            if (sourceId != 0)
                SourceId = sourceId;
        }

        public void End()
        {
            Active = false;
            PeerId = StreamId = SourceId = 0;
        }
    }
}
