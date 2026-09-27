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

namespace dvmconsole
{
    /// <summary>
    /// Tracks the exact global and channel history rows for one received stream.
    /// </summary>
    internal sealed class ReceivedCallHistory
    {
        private readonly uint streamId;
        private readonly CallEntry globalEntry;
        private readonly CallEntry channelEntry;

        public ReceivedCallHistory(uint streamId, CallEntry globalEntry, CallEntry channelEntry)
        {
            this.streamId = streamId;
            this.globalEntry = globalEntry;
            this.channelEntry = channelEntry;
        }

        /// <summary>
        /// Applies a late source ID, such as MDC, without creating another call.
        /// The initial packet's ID remains in use until the source changes.
        /// </summary>
        public void UpdateSource(uint receivedStreamId, int sourceId, string alias)
        {
            if (receivedStreamId != streamId)
                return;

            UpdateEntry(globalEntry, sourceId, alias);
            UpdateEntry(channelEntry, sourceId, alias);
        }

        private static void UpdateEntry(CallEntry entry, int sourceId, string alias)
        {
            entry.Dispatcher.VerifyAccess();
            if (entry.SrcId == sourceId && entry.RidAlias == (alias ?? string.Empty))
                return;

            entry.SrcId = sourceId;
            entry.SrcIdText = sourceId.ToString();
            entry.RidAlias = alias ?? string.Empty;
        }
    }
}
