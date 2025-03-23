// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using System.Windows;

using dvmconsole.Controls;

using Constants = fnecore.Constants;
using fnecore;
using fnecore.DMR;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Helper to encode and transmit PCM audio as DMR AMBE frames.
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="fne"></param>
        /// <param name="channel"></param>
        /// <param name="cpgChannel"></param>
        /// <param name="system"></param>
        private void DMREncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel, Codeplug.Channel cpgChannel, Codeplug.System system)
        {
            // make sure we have a valid stream ID
            if (channel.TxStreamId == 0)
                Log.WriteWarning($"({channel.SystemName}) DMRD: Traffic *VOICE FRAME    * Stream ID not set for traffic? Shouldn't happen.");

            try
            {
                byte slot = (byte)(cpgChannel.Slot - 1);

                byte[] data = null, dmrpkt = null;
                channel.dmrN = (byte)(channel.dmrSeqNo % 6);
                if (channel.ambeCount == FneSystemBase.AMBE_PER_SLOT)
                {
                    // is this the intitial sequence?
                    if (channel.dmrSeqNo == 0)
                    {
                        channel.pktSeq = 0;

                        // send DMR voice header
                        data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];

                        // generate DMR LC
                        LC dmrLC = new LC();
                        dmrLC.FLCO = (byte)DMRFLCO.FLCO_GROUP;
                        dmrLC.SrcId = uint.Parse(system.Rid);
                        dmrLC.DstId = uint.Parse(cpgChannel.Tgid);
                        channel.embeddedData.SetLC(dmrLC);

                        // generate the Slot Type
                        SlotType slotType = new SlotType();
                        slotType.DataType = (byte)DMRDataType.VOICE_LC_HEADER;
                        slotType.GetData(ref data);

                        FullLC.Encode(dmrLC, ref data, DMRDataType.VOICE_LC_HEADER);

                        // generate DMR network frame
                        dmrpkt = new byte[FneSystemBase.DMR_PACKET_SIZE];
                        fne.CreateDMRMessage(ref dmrpkt, uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), slot, FrameType.VOICE_SYNC, (byte)channel.dmrSeqNo, 0);
                        Buffer.BlockCopy(data, 0, dmrpkt, 20, FneSystemBase.DMR_FRAME_LENGTH_BYTES);

                        fne.peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR), dmrpkt, channel.pktSeq, channel.TxStreamId);

                        channel.dmrSeqNo++;
                    }

                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;

                    // send DMR voice
                    data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];

                    Buffer.BlockCopy(channel.ambeBuffer, 0, data, 0, 13);
                    data[13U] = (byte)(channel.ambeBuffer[13U] & 0xF0);
                    data[19U] = (byte)(channel.ambeBuffer[13U] & 0x0F);
                    Buffer.BlockCopy(channel.ambeBuffer, 14, data, 20, 13);

                    FrameType frameType = FrameType.VOICE_SYNC;
                    if (channel.dmrN == 0)
                        frameType = FrameType.VOICE_SYNC;
                    else
                    {
                        frameType = FrameType.VOICE;

                        byte lcss = channel.embeddedData.GetData(ref data, channel.dmrN);

                        // generated embedded signalling
                        EMB emb = new EMB();
                        emb.ColorCode = 0;
                        emb.LCSS = lcss;
                        emb.Encode(ref data);
                    }

                    // generate DMR network frame
                    dmrpkt = new byte[FneSystemBase.DMR_PACKET_SIZE];
                    fne.CreateDMRMessage(ref dmrpkt, uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), 1, frameType, (byte)channel.dmrSeqNo, channel.dmrN);
                    Buffer.BlockCopy(data, 0, dmrpkt, 20, FneSystemBase.DMR_FRAME_LENGTH_BYTES);

                    fne.peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR), dmrpkt, channel.pktSeq, channel.TxStreamId);

                    channel.dmrSeqNo++;

                    FneUtils.Memset(channel.ambeBuffer, 0, 27);
                    channel.ambeCount = 0;
                }

                int smpIdx = 0;
                short[] samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];
                for (int pcmIdx = 0; pcmIdx < pcm.Length; pcmIdx += 2)
                {
                    samples[smpIdx] = (short)((pcm[pcmIdx + 1] << 8) + pcm[pcmIdx + 0]);
                    smpIdx++;
                }

                // encode PCM samples into AMBE codewords
                byte[] ambe = null;

                if (channel.ExternalVocoderEnabled)
                {
                    if (channel.ExtHalfRateVocoder == null)
                        channel.ExtHalfRateVocoder = new AmbeVocoder(false);

                    channel.ExtHalfRateVocoder.encode(samples, out ambe, true);
                }
                else
                {
                    if (channel.Encoder == null)
                        channel.Encoder = new MBEEncoder(MBE_MODE.DMR_AMBE);

                    ambe = new byte[FneSystemBase.AMBE_BUF_LEN];

                    channel.Encoder.encode(samples, ambe);
                }

                Buffer.BlockCopy(ambe, 0, channel.ambeBuffer, channel.ambeCount * 9, FneSystemBase.AMBE_BUF_LEN);

                channel.ambeCount++;
            }
            catch (Exception ex)
            {
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Helper to decode and playback DMR AMBE frames as PCM audio.
        /// </summary>
        /// <param name="ambe"></param>
        /// <param name="e"></param>
        /// <param name="system"></param>
        /// <param name="channel"></param>
        private void DMRDecodeAudioFrame(byte[] ambe, DMRDataReceivedEvent e, PeerSystem system, ChannelBox channel)
        {
            try
            {
                // Log.Logger.Debug($"FULL AMBE {FneUtils.HexDump(ambe)}");
                for (int n = 0; n < FneSystemBase.AMBE_PER_SLOT; n++)
                {
                    byte[] ambePartial = new byte[FneSystemBase.AMBE_BUF_LEN];
                    for (int i = 0; i < FneSystemBase.AMBE_BUF_LEN; i++)
                        ambePartial[i] = ambe[i + (n * 9)];

                    short[] samples = null;
                    int errs = 0;

                    // do we have the external vocoder library?
                    if (channel.ExternalVocoderEnabled)
                    {
                        if (channel.ExtHalfRateVocoder == null)
                            channel.ExtHalfRateVocoder = new AmbeVocoder(false);

                        errs = channel.ExtHalfRateVocoder.decode(ambePartial, out samples);
                    }
                    else
                    {
                        samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];
                        errs = channel.Decoder.decode(ambePartial, samples);
                    }

                    if (samples != null)
                    {
                        Log.WriteLine($"({system.SystemName}) DMRD: Traffic *VOICE FRAME    * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot + 1} VC{e.n}.{n} ERRS {errs} [STREAM ID {e.StreamId}]");
                        // Log.Logger.Debug($"PARTIAL AMBE {FneUtils.HexDump(ambePartial)}");
                        // Log.Logger.Debug($"SAMPLE BUFFER {FneUtils.HexDump(samples)}");

                        int pcmIdx = 0;
                        byte[] pcm = new byte[samples.Length * 2];
                        for (int smpIdx = 0; smpIdx < samples.Length; smpIdx++)
                        {
                            pcm[pcmIdx + 0] = (byte)(samples[smpIdx] & 0xFF);
                            pcm[pcmIdx + 1] = (byte)((samples[smpIdx] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        //Log.WriteLine($"PCM BYTE BUFFER {FneUtils.HexDump(pcm)}");
                        audioManager.AddTalkgroupStream(e.DstId.ToString(), pcm);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteError($"Audio Decode Exception: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Event handler used to process incoming DMR data.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="pktTime"></param>
        public void DMRDataReceived(DMRDataReceivedEvent e, DateTime pktTime)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    if (cpgChannel.GetChannelMode() != Codeplug.ChannelMode.DMR)
                        continue;

                    PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled || channel.Name == PLAYBACKCHNAME)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    if (!systemStatuses.ContainsKey(cpgChannel.Name + e.Slot))
                        systemStatuses[cpgChannel.Name + e.Slot] = new SlotStatus();

                    if (channel.Decoder == null)
                        channel.Decoder = new MBEDecoder(MBE_MODE.DMR_AMBE);

                    byte[] data = new byte[FneSystemBase.DMR_FRAME_LENGTH_BYTES];
                    Buffer.BlockCopy(e.Data, 20, data, 0, FneSystemBase.DMR_FRAME_LENGTH_BYTES);
                    byte bits = e.Data[15];

                    // is this a new call stream?
                    if (e.StreamId != systemStatuses[cpgChannel.Name + e.Slot].RxStreamId)
                    {
                        channel.IsReceiving = true;
                        systemStatuses[cpgChannel.Name + e.Slot].RxStart = pktTime;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL START     * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot} [STREAM ID {e.StreamId}]");

                        // if we can, use the LC from the voice header as to keep all options intact
                        if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_LC_HEADER))
                        {
                            LC lc = FullLC.Decode(data, DMRDataType.VOICE_LC_HEADER);
                            systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC = lc;
                        }
                        else // if we don't have a voice header; don't wait to decode it, just make a dummy header
                            systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC = new LC()
                            {
                                SrcId = e.SrcId,
                                DstId = e.DstId
                            };

                        systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC = new PrivacyLC();
                        Log.WriteLine($"({system.Name}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_LC {FneUtils.HexDump(systemStatuses[cpgChannel.Name + e.Slot].DMR_RxLC.GetBytes())}");

                        callHistoryWindow.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId, DateTime.Now.ToString());
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, false); // TODO: Encrypted state

                        string alias = string.Empty;

                        try
                        {
                            alias = AliasTools.GetAliasByRid(system.RidAlias, (int)e.SrcId);
                        }
                        catch (Exception) { }

                        if (string.IsNullOrEmpty(alias))
                            channel.LastSrcId = "Last ID: " + e.SrcId;
                        else
                            channel.LastSrcId = "Last: " + alias;

                        channel.Background = ChannelBox.GREEN_GRADIENT;
                    }

                    // if we can, use the PI LC from the PI voice header as to keep all options intact
                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_PI_HEADER))
                    {
                        PrivacyLC lc = FullLC.DecodePI(data);
                        systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC = lc;
                        //Log.WriteLine($"({SystemName}) DMRD: Traffic *CALL PI PARAMS  * PEER {e.PeerId} DST_ID {e.DstId} TS {e.Slot + 1} ALGID {lc.AlgId} KID {lc.KId} [STREAM ID {e.StreamId}]");
                        //Log.WriteLine($"({SystemName}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_PI_LC {FneUtils.HexDump(systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC.GetBytes())}");
                    }

                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.TERMINATOR_WITH_LC) && (systemStatuses[cpgChannel.Name + e.Slot].RxType != FrameType.TERMINATOR))
                    {
                        channel.IsReceiving = false;
                        TimeSpan callDuration = pktTime - systemStatuses[cpgChannel.Name + e.Slot].RxStart;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL END       * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        channel.Background = ChannelBox.BLUE_GRADIENT;
                        channel.VolumeMeterLevel = 0;
                        callHistoryWindow.ChannelUnkeyed(cpgChannel.Name, (int)e.SrcId);
                    }

                    if (e.FrameType == FrameType.VOICE_SYNC || e.FrameType == FrameType.VOICE)
                    {
                        byte[] ambe = new byte[FneSystemBase.DMR_AMBE_LENGTH_BYTES];
                        Buffer.BlockCopy(data, 0, ambe, 0, 14);
                        ambe[13] &= 0xF0;
                        ambe[13] |= (byte)(data[19] & 0x0F);
                        Buffer.BlockCopy(data, 20, ambe, 14, 13);
                        DMRDecodeAudioFrame(ambe, e, handler, channel);
                    }

                    systemStatuses[cpgChannel.Name + e.Slot].RxRFS = e.SrcId;
                    systemStatuses[cpgChannel.Name + e.Slot].RxType = e.FrameType;
                    systemStatuses[cpgChannel.Name + e.Slot].RxTGId = e.DstId;
                    systemStatuses[cpgChannel.Name + e.Slot].RxTime = pktTime;
                    systemStatuses[cpgChannel.Name + e.Slot].RxStreamId = e.StreamId;
                }
            });
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole
