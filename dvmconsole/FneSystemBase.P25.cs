// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Audio Bridge
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Audio Bridge
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2022-2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System.Diagnostics;
using fnecore;
using fnecore.P25;
using fnecore.P25.LC.TSBK;

namespace dvmconsole
{
    /// <summary>
    /// Implements a FNE system base.
    /// </summary>
    public abstract partial class FneSystemBase : fnecore.FneSystemBase
    {
        public const int IMBE_BUF_LEN = 11;

        /*
        ** Methods
        */

        /// <summary>
        /// Callback used to validate incoming P25 data.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="srcId">Source Address</param>
        /// <param name="dstId">Destination Address</param>
        /// <param name="callType">Call Type (Group or Private)</param>
        /// <param name="duid">P25 DUID</param>
        /// <param name="frameType">Frame Type</param>
        /// <param name="streamId">Stream ID</param>
        /// <param name="message">Raw message data</param>
        /// <returns>True, if data stream is valid, otherwise false.</returns>
        protected override bool P25DataValidate(uint peerId, uint srcId, uint dstId, CallType callType, P25DUID duid, FrameType frameType, uint streamId, byte[] message)
        {
            return true;
        }

        /// <summary>
        /// Event handler used to pre-process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void P25DataPreprocess(object sender, P25DataReceivedEvent e)
        {
            return;
        }

        /// <summary>
        /// Helper to send a P25 TDU message.
        /// </summary>
        /// <param name="grantDemand"></param>
        public void SendP25TDU(uint srcId, uint dstId, bool grantDemand = false)
        {
            RemoteCallData callData = new RemoteCallData()
            {
                SrcId = srcId,
                DstId = dstId,
                LCO = P25Defines.LC_GROUP
            };

            SendP25TDU(callData, grantDemand);
        }

        /// <summary>
        /// Encode a logical link data unit 1.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="imbe"></param>
        /// <param name="frameType"></param>
        /// <param name="srcIdOverride"></param>
        private void EncodeLDU1(ref byte[] data, int offset, byte[] imbe, byte frameType, uint srcId, uint dstId)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (imbe == null)
                throw new ArgumentNullException("imbe");

            // determine the LDU1 DFSI frame length, its variable
            uint frameLength = P25DFSI.P25_DFSI_LDU1_VOICE1_FRAME_LENGTH_BYTES;
            switch (frameType)
            {
                case P25DFSI.P25_DFSI_LDU1_VOICE1:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE1_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE2:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE2_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE3:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE3_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE4:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE4_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE5:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE5_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE6:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE6_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE7:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE7_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE8:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE8_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE9:
                    frameLength = P25DFSI.P25_DFSI_LDU1_VOICE9_FRAME_LENGTH_BYTES;
                    break;
                default:
                    return;
            }

            byte[] dfsiFrame = new byte[frameLength];

            dfsiFrame[0U] = frameType;                                                      // Frame Type

            // different frame types mean different things
            switch (frameType)
            {
                case P25DFSI.P25_DFSI_LDU1_VOICE2:
                    {
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 1, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE3:
                    {
                        dfsiFrame[1U] = P25Defines.LC_GROUP;                                // LCO
                        dfsiFrame[2U] = 0;                                                  // MFId
                        dfsiFrame[3U] = 0;                                                  // Service Options
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE4:
                    {
                        dfsiFrame[1U] = (byte)((dstId >> 16) & 0xFFU);                      // Talkgroup Address
                        dfsiFrame[2U] = (byte)((dstId >> 8) & 0xFFU);
                        dfsiFrame[3U] = (byte)((dstId >> 0) & 0xFFU);
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE5:
                    {
                        dfsiFrame[1U] = (byte)((srcId >> 16) & 0xFFU);                      // Source Address
                        dfsiFrame[2U] = (byte)((srcId >> 8) & 0xFFU);
                        dfsiFrame[3U] = (byte)((srcId >> 0) & 0xFFU);
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE6:
                    {
                        dfsiFrame[1U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[2U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[3U] = 0;                                                  // RS (24,12,13)
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE7:
                    {
                        dfsiFrame[1U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[2U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[3U] = 0;                                                  // RS (24,12,13)
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE8:
                    {
                        dfsiFrame[1U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[2U] = 0;                                                  // RS (24,12,13)
                        dfsiFrame[3U] = 0;                                                  // RS (24,12,13)
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU1_VOICE9:
                    {
                        dfsiFrame[1U] = 0;                                                  // LSD MSB
                        dfsiFrame[2U] = 0;                                                  // LSD LSB
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 4, IMBE_BUF_LEN);              // IMBE
                    }
                    break;

                case P25DFSI.P25_DFSI_LDU1_VOICE1:
                default:
                    {
                        dfsiFrame[6U] = 0;                                                  // RSSI
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 10, IMBE_BUF_LEN);             // IMBE
                    }
                    break;
            }

            Buffer.BlockCopy(dfsiFrame, 0, data, offset, (int)frameLength);
        }

        /// <summary>
        /// Creates an P25 LDU1 frame message.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="srcId"></param>
        public void CreateP25LDU1Message(in byte[] netLDU1, ref byte[] data, uint srcId, uint dstId)
        {
            // pack DFSI data
            int count = P25_MSG_HDR_SIZE;
            byte[] imbe = new byte[IMBE_BUF_LEN];

            Buffer.BlockCopy(netLDU1, 10, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 24, imbe, P25DFSI.P25_DFSI_LDU1_VOICE1, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE1_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 26, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 46, imbe, P25DFSI.P25_DFSI_LDU1_VOICE2, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE2_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 55, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 60, imbe, P25DFSI.P25_DFSI_LDU1_VOICE3, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE3_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 80, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 77, imbe, P25DFSI.P25_DFSI_LDU1_VOICE4, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE4_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 105, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 94, imbe, P25DFSI.P25_DFSI_LDU1_VOICE5, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE5_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 130, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 111, imbe, P25DFSI.P25_DFSI_LDU1_VOICE6, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE6_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 155, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 128, imbe, P25DFSI.P25_DFSI_LDU1_VOICE7, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE7_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 180, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 145, imbe, P25DFSI.P25_DFSI_LDU1_VOICE8, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE8_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU1, 204, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU1(ref data, 162, imbe, P25DFSI.P25_DFSI_LDU1_VOICE9, srcId, dstId);
            count += (int)P25DFSI.P25_DFSI_LDU1_VOICE9_FRAME_LENGTH_BYTES;

            data[23U] = (byte)count;
        }

        /// <summary>
        /// Encode a logical link data unit 2.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="imbe"></param>
        /// <param name="frameType"></param>
        /// <param name="cryptoParams"></param>
        private void EncodeLDU2(ref byte[] data, int offset, byte[] imbe, byte frameType, CryptoParams cryptoParams)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (imbe == null)
                throw new ArgumentNullException("imbe");

            // determine the LDU2 DFSI frame length, its variable
            uint frameLength = P25DFSI.P25_DFSI_LDU2_VOICE10_FRAME_LENGTH_BYTES;
            switch (frameType)
            {
                case P25DFSI.P25_DFSI_LDU2_VOICE10:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE10_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE11:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE11_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE12:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE12_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE13:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE13_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE14:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE14_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE15:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE15_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE16:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE16_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE17:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE17_FRAME_LENGTH_BYTES;
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE18:
                    frameLength = P25DFSI.P25_DFSI_LDU2_VOICE18_FRAME_LENGTH_BYTES;
                    break;
                default:
                    return;
            }

            byte[] dfsiFrame = new byte[frameLength];

            dfsiFrame[0U] = frameType;                                                      // Frame Type

            // different frame types mean different things
            switch (frameType)
            {
                case P25DFSI.P25_DFSI_LDU2_VOICE11:
                    {
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 1, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE12:
                    {
                        dfsiFrame[1U] = cryptoParams.MI[0];                                 // Message Indicator
                        dfsiFrame[2U] = cryptoParams.MI[1];
                        dfsiFrame[3U] = cryptoParams.MI[2];
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE13:
                    {
                        dfsiFrame[1U] = cryptoParams.MI[3];                                 // Message Indicator
                        dfsiFrame[2U] = cryptoParams.MI[4];
                        dfsiFrame[3U] = cryptoParams.MI[5];
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE14:
                    {
                        dfsiFrame[1U] = cryptoParams.MI[6];                                 // Message Indicator
                        dfsiFrame[2U] = cryptoParams.MI[7];
                        dfsiFrame[3U] = cryptoParams.MI[8];
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE15:
                    {
                        dfsiFrame[1U] = cryptoParams.AlgoId;                                // Algorithm ID
                        FneUtils.WriteBytes(cryptoParams.KeyId, ref dfsiFrame, 2);          // Key ID
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE16:
                    {
                        // first 3 bytes of frame are supposed to be
                        // part of the RS(24, 16, 9) of the VOICE12, 13, 14, 15
                        // control bytes
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE17:
                    {
                        // first 3 bytes of frame are supposed to be
                        // part of the RS(24, 16, 9) of the VOICE12, 13, 14, 15
                        // control bytes
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 5, IMBE_BUF_LEN);              // IMBE
                    }
                    break;
                case P25DFSI.P25_DFSI_LDU2_VOICE18:
                    {
                        dfsiFrame[1U] = 0;                                                  // LSD MSB
                        dfsiFrame[2U] = 0;                                                  // LSD LSB
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 4, IMBE_BUF_LEN);              // IMBE
                    }
                    break;

                case P25DFSI.P25_DFSI_LDU2_VOICE10:
                default:
                    {
                        dfsiFrame[6U] = 0;                                                  // RSSI
                        Buffer.BlockCopy(imbe, 0, dfsiFrame, 10, IMBE_BUF_LEN);             // IMBE
                    }
                    break;
            }

            Buffer.BlockCopy(dfsiFrame, 0, data, offset, (int)frameLength);
        }

        /// <summary>
        /// Creates an P25 LDU2 frame message.
        /// </summary>
        /// <param name="netLDU2">Input LDU data array</param>
        /// <param name="data">Output data array</param>
        /// <param name="cryptoParams"></param>
        public void CreateP25LDU2Message(in byte[] netLDU2, ref byte[] data, CryptoParams cryptoParams = null)
        {
            if (cryptoParams == null)
                cryptoParams = new CryptoParams();

            // pack DFSI data
            int count = P25_MSG_HDR_SIZE;
            byte[] imbe = new byte[IMBE_BUF_LEN];

            Buffer.BlockCopy(netLDU2, 10, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 24, imbe, P25DFSI.P25_DFSI_LDU2_VOICE10, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE10_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 26, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 46, imbe, P25DFSI.P25_DFSI_LDU2_VOICE11, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE11_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 55, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 60, imbe, P25DFSI.P25_DFSI_LDU2_VOICE12, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE12_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 80, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 77, imbe, P25DFSI.P25_DFSI_LDU2_VOICE13, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE13_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 105, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 94, imbe, P25DFSI.P25_DFSI_LDU2_VOICE14, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE14_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 130, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 111, imbe, P25DFSI.P25_DFSI_LDU2_VOICE15, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE15_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 155, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 128, imbe, P25DFSI.P25_DFSI_LDU2_VOICE16, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE16_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 180, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 145, imbe, P25DFSI.P25_DFSI_LDU2_VOICE17, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE17_FRAME_LENGTH_BYTES;

            Buffer.BlockCopy(netLDU2, 204, imbe, 0, IMBE_BUF_LEN);
            EncodeLDU2(ref data, 162, imbe, P25DFSI.P25_DFSI_LDU2_VOICE18, cryptoParams);
            count += (int)P25DFSI.P25_DFSI_LDU2_VOICE18_FRAME_LENGTH_BYTES;

            data[23U] = (byte)count;
        }

        /// <summary>
        /// Event handler used to process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void P25DataReceived(object sender, P25DataReceivedEvent e)
        {
            DateTime pktTime = DateTime.Now;

            // TODO: Handle TSBKs
            if (e.DUID == P25DUID.TSDU)
            {
                byte lco = e.Data[4];

                switch (lco)
                {
                    case P25Defines.TSBK_IOSP_ACK_RSP:
                        break;

                    case P25Defines.TSBK_IOSP_CALL_ALRT:
                        break;
                }
            }

            if (e.DUID == P25DUID.HDU || e.DUID == P25DUID.TSDU || e.DUID == P25DUID.PDU)
                return;

            if (e.CallType == CallType.GROUP)
            {
                if (e.SrcId == 0)
                    return;

                mainWindow.P25DataReceived(e, pktTime);
            }
        }
    } // public abstract partial class FneSystemBase : fnecore.FneSystemBase
} // namespace dvmconsole
