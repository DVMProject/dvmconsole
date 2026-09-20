// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Audio Bridge
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Audio Bridge
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2022-2024 Bryan Biedenkapp, N2PLL
*
*/

using fnecore;
using fnecore.DMR;

namespace dvmconsole
{
    /// <summary>
    /// Implements a FNE system base.
    /// </summary>
    public abstract partial class FneSystemBase : fnecore.FneSystemBase
    {
        public const int AMBE_BUF_LEN = 9;
        public const int DMR_AMBE_LENGTH_BYTES = 27;
        public const int AMBE_PER_SLOT = 3;

        public const int P25_FIXED_SLOT = 2;

        public const int SAMPLE_RATE = 8000;
        public const int BITS_PER_SECOND = 16;

        public const int MBE_SAMPLES_LENGTH = 160;

        public const int AUDIO_BUFFER_MS = 20;
        public const int AUDIO_NO_BUFFERS = 2;
        public const int AFSK_AUDIO_BUFFER_MS = 60;
        public const int AFSK_AUDIO_NO_BUFFERS = 4;

        public new const int DMR_PACKET_SIZE = 55;
        public new const int DMR_FRAME_LENGTH_BYTES = 33;

        /*
        ** Methods
        */

        /// <summary>
        /// Callback used to validate incoming DMR data.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="srcId">Source Address</param>
        /// <param name="dstId">Destination Address</param>
        /// <param name="slot">Slot Number</param>
        /// <param name="callType">Call Type (Group or Private)</param>
        /// <param name="frameType">Frame Type</param>
        /// <param name="dataType">DMR Data Type</param>
        /// <param name="streamId">Stream ID</param>
        /// <param name="message">Raw message data</param>
        /// <returns>True, if data stream is valid, otherwise false.</returns>
        protected override bool DMRDataValidate(uint peerId, uint srcId, uint dstId, byte slot, fnecore.CallType callType, FrameType frameType, DMRDataType dataType, uint streamId, byte[] message)
        {
            return true;
        }

        /// <summary>
        /// Creates an DMR frame message.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="srcId"></param>
        /// <param name="dstId"></param>
        /// <param name="slot"></param>
        /// <param name="frameType"></param>
        /// <param name="seqNo"></param>
        /// <param name="n"></param>
        /// <param name="dataType"></param>
        public void CreateDMRMessage(ref byte[] data, uint srcId, uint dstId, byte slot, FrameType frameType, byte seqNo, byte n, DMRDataType dataType = DMRDataType.TERMINATOR_WITH_LC)
        {
            RemoteCallData callData = new RemoteCallData()
            {
                SrcId = srcId,
                DstId = dstId,
                FrameType = frameType,
                Slot = slot,
            };

            CreateDMRMessage(ref data, callData, seqNo, n);

            // DATA_SYNC identifies the frame class, not the LC payload type.
            if (frameType == FrameType.DATA_SYNC)
                data[15] = (byte)((data[15] & 0xC0) | 0x20 | (byte)dataType);
        }

        /// <summary>
        /// Helper to send a DMR terminator with LC message.
        /// </summary>
        public void SendDMRTerminator(uint srcId, uint dstId, byte slot, int dmrSeqNo, byte dmrN, EmbeddedData embeddedData, uint streamId, ushort pktSeq)
        {
            // A short PTT may finish before any DMR burst was sent.
            if (streamId == 0 || dmrSeqNo <= 0)
                return;

            // Bound padding to the unfinished superframe. The legacy FNECore
            // helper can underflow its fill count when fewer than three frames were sent.
            byte nextN = (byte)((dmrN + 1) % 6);
            int fill = (6 - nextN) % 6;
            var protocol = new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR);
            for (int i = 0; i < fill; i++)
            {
                byte n = (byte)(nextN + i);
                byte[] silence = new byte[DMR_FRAME_LENGTH_BYTES];
                // The shared silence constant includes two modem tag bytes.
                Buffer.BlockCopy(DMR_SILENCE_DATA, 2, silence, 0, DMR_FRAME_LENGTH_BYTES);

                byte lcss = embeddedData.GetData(ref silence, n);
                EMB emb = new EMB { ColorCode = 0, LCSS = lcss };
                emb.Encode(ref silence);

                byte[] voice = new byte[DMR_PACKET_SIZE];
                CreateDMRMessage(ref voice, srcId, dstId, slot, FrameType.VOICE, (byte)dmrSeqNo, n);
                Buffer.BlockCopy(silence, 0, voice, 20, DMR_FRAME_LENGTH_BYTES);

                pktSeq = (ushort)((pktSeq + 1) % Constants.RtpCallEndSeq);
                fne.SendMasterTraffic(protocol, voice, pktSeq, streamId);
                dmrSeqNo++;
            }

            byte[] data = new byte[DMR_FRAME_LENGTH_BYTES];
            LC lc = new LC { SrcId = srcId, DstId = dstId, FLCO = (byte)DMRFLCO.FLCO_GROUP };
            SlotType slotType = new SlotType { DataType = (byte)DMRDataType.TERMINATOR_WITH_LC };
            slotType.GetData(ref data);
            FullLC.Encode(lc, ref data, DMRDataType.TERMINATOR_WITH_LC);

            byte[] terminator = new byte[DMR_PACKET_SIZE];
            CreateDMRMessage(ref terminator, srcId, dstId, slot, FrameType.DATA_SYNC, (byte)dmrSeqNo, 0);
            Buffer.BlockCopy(data, 0, terminator, 20, DMR_FRAME_LENGTH_BYTES);
            fne.SendMasterTraffic(protocol, terminator, Constants.RtpCallEndSeq, streamId);
        }

        /// <summary>
        /// Event handler used to process incoming DMR data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void DMRDataReceived(object sender, DMRDataReceivedEvent e)
        {
            DateTime pktTime = DateTime.Now;

            byte[] data = new byte[DMR_FRAME_LENGTH_BYTES];
            Buffer.BlockCopy(e.Data, 20, data, 0, DMR_FRAME_LENGTH_BYTES);
            byte bits = e.Data[15];

            if (e.CallType == CallType.GROUP)
                mainWindow.DMRDataReceived((this as PeerSystem)?.ConfiguredSystemName, e, pktTime);

            return;
        }
    } // public abstract partial class FneSystemBase : fnecore.FneSystemBase
} // namespace dvmconsole
