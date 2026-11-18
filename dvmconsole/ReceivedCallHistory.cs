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

using System.Windows.Media;

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
        private bool ended;

        public ReceivedCallHistory(uint streamId, CallEntry globalEntry, CallEntry channelEntry)
        {
            this.streamId = streamId;
            this.globalEntry = globalEntry;
            this.channelEntry = channelEntry;
            globalEntry.StreamId = channelEntry.StreamId = streamId;
        }

        /// <summary>
        /// Applies a late source ID, such as MDC, without creating another call.
        /// The initial packet's ID remains in use until the source changes.
        /// </summary>
        public void UpdateSource(uint receivedStreamId, int sourceId, string alias)
        {
            if (ended || receivedStreamId != streamId || sourceId <= 0)
                return;

            UpdateEntry(globalEntry, sourceId, alias);
            UpdateEntry(channelEntry, sourceId, alias);
        }

        public void UpdateEncryption(uint receivedStreamId, bool encrypted, ushort? keyId)
        {
            if (ended || receivedStreamId != streamId)
                return;

            globalEntry.EncryptionKeyId = encrypted ? keyId : null;
            channelEntry.EncryptionKeyId = encrypted ? keyId : null;
        }

        /// <summary>Links a recording enabled after this call's initial history entry.</summary>
        public void AttachRecording(uint receivedStreamId, Task<TarRecordingMetadata> recording)
        {
            if (ended || receivedStreamId != streamId || recording == null)
                return;

            globalEntry.Recording = channelEntry.Recording = recording;
        }

        public void SetActive(bool encrypted)
        {
            if (ended)
                return;
            globalEntry.ForegroundColor = channelEntry.ForegroundColor = Brushes.Black;
            globalEntry.BackgroundColor = channelEntry.BackgroundColor = encrypted ? Brushes.Orange : Brushes.LightGreen;
        }

        public void End(bool darkMode)
        {
            ended = true;
            globalEntry.ForegroundColor = channelEntry.ForegroundColor = darkMode ? Brushes.White : Brushes.Black;
            globalEntry.BackgroundColor = channelEntry.BackgroundColor = Brushes.Transparent;
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
