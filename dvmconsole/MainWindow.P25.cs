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

using NWaves.Signals;

using dvmconsole.Controls;

using Constants = fnecore.Constants;
using fnecore;
using fnecore.P25;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {

        /*
        ** Methods
        */

        /// <summary>
        /// Helper to encode and transmit PCM audio as P25 IMBE frames.
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="fne"></param>
        /// <param name="channel"></param>
        /// <param name="cpgChannel"></param>
        /// <param name="system"></param>
        private void P25EncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel, Codeplug.Channel cpgChannel, Codeplug.System system)
        {
            bool encryptCall = true; // TODO: make this dynamic somewhere?

            if (channel.p25N > 17)
                channel.p25N = 0;
            if (channel.p25N == 0)
                FneUtils.Memset(channel.netLDU1, 0, 9 * 25);
            if (channel.p25N == 9)
                FneUtils.Memset(channel.netLDU2, 0, 9 * 25);

            // Log.Logger.Debug($"BYTE BUFFER {FneUtils.HexDump(pcm)}");

            //// pre-process: apply gain to PCM audio frames
            //if (Program.Configuration.TxAudioGain != 1.0f)
            //{
            //    BufferedWaveProvider buffer = new BufferedWaveProvider(waveFormat);
            //    buffer.AddSamples(pcm, 0, pcm.Length);

            //    VolumeWaveProvider16 gainControl = new VolumeWaveProvider16(buffer);
            //    gainControl.Volume = Program.Configuration.TxAudioGain;
            //    gainControl.Read(pcm, 0, pcm.Length);
            //}

            int smpIdx = 0;
            short[] samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];
            for (int pcmIdx = 0; pcmIdx < pcm.Length; pcmIdx += 2)
            {
                samples[smpIdx] = (short)((pcm[pcmIdx + 1] << 8) + pcm[pcmIdx + 0]);
                smpIdx++;
            }

            channel.VolumeMeterLevel = 0;

            float max = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                short sample = samples[index];

                // to floating point
                float sample32 = sample / 32768f;

                if (sample32 < 0)
                    sample32 = -sample32;

                // is this the max value?
                if (sample32 > max)
                    max = sample32;
            }

            channel.VolumeMeterLevel = max;

            // Convert to floats
            float[] fSamples = AudioConverter.PcmToFloat(samples);

            // Convert to signal
            DiscreteSignal signal = new DiscreteSignal(8000, fSamples, true);

            // encode PCM samples into IMBE codewords
            byte[] imbe = new byte[FneSystemBase.IMBE_BUF_LEN];

            int tone = 0;

            if (true) // TODO: Disable/enable detection
            {
                tone = channel.ToneDetector.Detect(signal);
            }

            if (tone > 0)
            {
                MBEToneGenerator.IMBEEncodeSingleTone((ushort)tone, imbe);
                Log.WriteLine($"({system.Name}) P25D: {tone} HZ TONE DETECT");
            }
            else
            {
                // do we have the external vocoder library?
                if (channel.ExternalVocoderEnabled)
                {
                    if (channel.ExtFullRateVocoder == null)
                        channel.ExtFullRateVocoder = new AmbeVocoder(true);

                    channel.ExtFullRateVocoder.encode(samples, out imbe);
                }
                else
                {
                    if (channel.Encoder == null)
                        channel.Encoder = new MBEEncoder(MBE_MODE.IMBE_88BIT);

                    channel.Encoder.encode(samples, imbe);
                }
            }
            // Log.Logger.Debug($"IMBE {FneUtils.HexDump(imbe)}");

            if (encryptCall && cpgChannel.GetAlgoId() != 0 && cpgChannel.GetKeyId() != 0)
            {
                // initial HDU MI
                if (channel.p25N == 0)
                {
                    if (channel.mi.All(b => b == 0))
                    {
                        Random random = new Random();

                        for (int i = 0; i < P25Defines.P25_MI_LENGTH; i++)
                            channel.mi[i] = (byte)random.Next(0x00, 0x100);
                    }

                    channel.Crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                }

                // crypto time
                channel.Crypter.Process(imbe, channel.p25N < 9U ? P25DUID.LDU1 : P25DUID.LDU2);

                // last block of LDU2, prepare a new MI
                if (channel.p25N == 17U)
                {
                    P25Crypto.CycleP25Lfsr(channel.mi);
                    channel.Crypter.Prepare(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId(), channel.mi);
                }
            }

            // fill the LDU buffers appropriately
            switch (channel.p25N)
            {
                // LDU1
                case 0:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 10, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 1:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 26, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 2:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 55, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 3:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 80, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 4:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 105, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 5:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 130, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 6:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 155, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 7:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 180, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 8:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU1, 204, FneSystemBase.IMBE_BUF_LEN);
                    break;

                // LDU2
                case 9:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 10, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 10:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 26, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 11:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 55, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 12:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 80, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 13:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 105, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 14:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 130, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 15:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 155, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 16:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 180, FneSystemBase.IMBE_BUF_LEN);
                    break;
                case 17:
                    Buffer.BlockCopy(imbe, 0, channel.netLDU2, 204, FneSystemBase.IMBE_BUF_LEN);
                    break;
            }

            uint srcId = uint.Parse(system.Rid);
            uint dstId = uint.Parse(cpgChannel.Tgid);

            FnePeer peer = fne.peer;
            RemoteCallData callData = new RemoteCallData()
            {
                SrcId = srcId,
                DstId = dstId,
                LCO = P25Defines.LC_GROUP
            };

            // make sure we have a valid stream ID
            if (channel.TxStreamId == 0)
                Log.WriteWarning($"({channel.SystemName}) P25D: Traffic *VOICE FRAME    * Stream ID not set for traffic? Shouldn't happen.");

            CryptoParams cryptoParams = new CryptoParams();
            if (cpgChannel.GetAlgoId() != P25Defines.P25_ALGO_UNENCRYPT && cpgChannel.GetKeyId() > 0)
            {
                cryptoParams.AlgoId = cpgChannel.GetAlgoId();
                cryptoParams.KeyId = cpgChannel.GetKeyId();
                Array.Copy(channel.mi, cryptoParams.MI, P25Defines.P25_MI_LENGTH);
            }

            // send P25 LDU1
            if (channel.p25N == 8U)
            {
                // bryanb: in multi-TG architecture we cannot use the pktSeq helper singleton in the FNE peer class otherwise we won't
                //  maintain outgoing RTP packet sequences properly
                if (channel.p25SeqNo == 0U)
                    channel.pktSeq = 0;
                else
                {
                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;
                }

                Log.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME LDU1* PEER {fne.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.TxStreamId} SEQ {channel.p25SeqNo}]");

                byte[] payload = new byte[200];
                fne.CreateP25MessageHdr((byte)P25DUID.LDU1, callData, ref payload, cryptoParams);
                fne.CreateP25LDU1Message(channel.netLDU1, ref payload, srcId, dstId);

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, channel.pktSeq, channel.TxStreamId);
            }

            // send P25 LDU2
            if (channel.p25N == 17U)
            {
                // bryanb: in multi-TG architecture we cannot use the pktSeq helper singleton in the FNE peer class otherwise we won't
                //  maintain outgoing RTP packet sequences properly
                if (channel.p25SeqNo == 0U)
                    channel.pktSeq = 0;
                else
                {
                    ushort curr = channel.pktSeq;
                    ++channel.pktSeq;
                    if (channel.pktSeq > (Constants.RtpCallEndSeq - 1))
                        channel.pktSeq = 0;
                }

                Log.WriteLine($"({channel.SystemName}) P25D: Traffic *VOICE FRAME LDU2* PEER {fne.PeerId} SRC_ID {srcId} TGID {dstId} [STREAM ID {channel.TxStreamId} SEQ {channel.p25SeqNo}]");

                byte[] payload = new byte[200];


                fne.CreateP25MessageHdr((byte)P25DUID.LDU2, callData, ref payload, cryptoParams);
                fne.CreateP25LDU2Message(channel.netLDU2, ref payload, new CryptoParams { AlgoId = cpgChannel.GetAlgoId(), KeyId = cpgChannel.GetKeyId(), MI = channel.mi });

                peer.SendMaster(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), payload, channel.pktSeq, channel.TxStreamId);
            }

            channel.p25SeqNo++;
            channel.p25N++;
        }

        /// <summary>
        /// Helper to decode and playback P25 IMBE frames as PCM audio.
        /// </summary>
        /// <param name="ldu"></param>
        /// <param name="e"></param>
        /// <param name="system"></param>
        /// <param name="channel"></param>
        /// <param name="duid"></param>
        private void P25DecodeAudioFrame(byte[] ldu, P25DataReceivedEvent e, PeerSystem system, ChannelBox channel, P25DUID duid = P25DUID.LDU1)
        {
            try
            {
                // decode 9 IMBE codewords into PCM samples
                for (int n = 0; n < 9; n++)
                {
                    byte[] imbe = new byte[FneSystemBase.IMBE_BUF_LEN];
                    switch (n)
                    {
                        case 0:
                            Buffer.BlockCopy(ldu, 10, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 1:
                            Buffer.BlockCopy(ldu, 26, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 2:
                            Buffer.BlockCopy(ldu, 55, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 3:
                            Buffer.BlockCopy(ldu, 80, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 4:
                            Buffer.BlockCopy(ldu, 105, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 5:
                            Buffer.BlockCopy(ldu, 130, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 6:
                            Buffer.BlockCopy(ldu, 155, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 7:
                            Buffer.BlockCopy(ldu, 180, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                        case 8:
                            Buffer.BlockCopy(ldu, 204, imbe, 0, FneSystemBase.IMBE_BUF_LEN);
                            break;
                    }

                    //Log.Logger.Debug($"Decoding IMBE buffer: {FneUtils.HexDump(imbe)}");

                    short[] samples = new short[FneSystemBase.MBE_SAMPLES_LENGTH];

                    channel.Crypter.Process(imbe, duid);

                    // do we have the external vocoder library?
                    if (channel.ExternalVocoderEnabled)
                    {
                        if (channel.ExtFullRateVocoder == null)
                            channel.ExtFullRateVocoder = new AmbeVocoder(true);

                        channel.p25Errs = channel.ExtFullRateVocoder.decode(imbe, out samples);
                    }
                    else
                        channel.p25Errs = channel.Decoder.decode(imbe, samples);

                    if (samples != null)
                    {
                        Log.WriteLine($"P25D: Traffic *VOICE FRAME    * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} VC{n} [STREAM ID {e.StreamId}]");

                        channel.VolumeMeterLevel = 0;

                        float max = 0;
                        for (int index = 0; index < samples.Length; index++)
                        {
                            short sample = samples[index];

                            // to floating point
                            float sample32 = sample / 32768f;

                            if (sample32 < 0)
                                sample32 = -sample32;

                            // is this the max value?
                            if (sample32 > max)
                                max = sample32;
                        }

                        channel.VolumeMeterLevel = max;

                        int pcmIdx = 0;
                        byte[] pcmData = new byte[samples.Length * 2];
                        for (int i = 0; i < samples.Length; i++)
                        {
                            pcmData[pcmIdx] = (byte)(samples[i] & 0xFF);
                            pcmData[pcmIdx + 1] = (byte)((samples[i] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        audioManager.AddTalkgroupStream(e.DstId.ToString(), pcmData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Audio Decode Exception: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }

        /// <summary>
        /// Event handler used to process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void P25DataReceived(P25DataReceivedEvent e, DateTime pktTime)
        {
            uint sysId = (uint)((e.Data[11U] << 8) | (e.Data[12U] << 0));
            uint netId = FneUtils.Bytes3ToUInt32(e.Data, 16);
            byte control = e.Data[14U];

            byte len = e.Data[23];
            byte[] data = new byte[len];
            for (int i = 24; i < len; i++)
                data[i - 24] = e.Data[i];

            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    if (cpgChannel.GetChannelMode() != Codeplug.ChannelMode.P25)
                        continue;

                    bool encrypted = false;

                    PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled || channel.Name == PLAYBACKCHNAME)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    if (channel.PttState)
                        continue;

                    if (e.DUID == P25DUID.TSDU || e.DUID == P25DUID.PDU)
                        continue;

                    if (!systemStatuses.ContainsKey(cpgChannel.Name))
                        systemStatuses[cpgChannel.Name] = new SlotStatus();

                    if (channel.Decoder == null)
                        channel.Decoder = new MBEDecoder(MBE_MODE.IMBE_88BIT);

                    SlotStatus slot = systemStatuses[cpgChannel.Name];
                    channel.LastPktTime = pktTime;

                    // if this is an LDU1 see if this is the first LDU with HDU encryption data
                    if (e.DUID == P25DUID.LDU1)
                    {
                        byte frameType = e.Data[180];

                        // get the initial MI and other enc info (bug found by the screeeeeeeeech on initial tx...)
                        if (frameType == P25Defines.P25_FT_HDU_VALID)
                        {
                            channel.algId = e.Data[181];
                            channel.kId = (ushort)((e.Data[182] << 8) | e.Data[183]);
                            Array.Copy(e.Data, 184, channel.mi, 0, P25Defines.P25_MI_LENGTH);

                            channel.Crypter.Prepare(channel.algId, channel.kId, channel.mi);

                            if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                                encrypted = true;
                        }
                    }

                    // is the Rx stream ID being Rx'ed on any of our other channels?
                    bool duplicateRx = false;
                    foreach (ChannelBox other in selectedChannelsManager.GetSelectedChannels())
                    {
                        if (other.InternalID == channel.InternalID)
                            continue;
                        if ((other.RxStreamId > 0 && other.RxStreamId == e.StreamId) && other.InternalID != channel.InternalID)
                        {
                            duplicateRx = true;
                            break;
                        }
                    }

                    // is this duplicate traffic?
                    if (((channel.PeerId > 0 && channel.RxStreamId > 0) && (e.PeerId != channel.PeerId && e.StreamId == channel.RxStreamId)) || duplicateRx)
                    {
                        Log.WriteLine($"({system.Name}) P25D: Traffic *IGNORE DUP TRAF* PEER {e.PeerId} CALL_START PEER ID {channel.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                        continue;
                    }

                    // is the Rx stream ID any of our Tx stream IDs?
                    List<bool> txChannels = new List<bool>();
                    foreach (ChannelBox other in selectedChannelsManager.GetSelectedChannels())
                        if (other.TxStreamId > 0 && other.TxStreamId == channel.RxStreamId)
                            txChannels.Add(true);

                    // if we have a count of Tx channels this means we're sourcing traffic for the incoming stream ID
                    if (txChannels.Count() > 0)
                    {
                        Log.WriteLine($"({system.Name}) P25D: Traffic *IGNORE TX TRAF * PEER {e.PeerId} CALL_START PEER ID {channel.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                        continue;
                    }

                    // is this a new call stream?
                    if (e.StreamId != slot.RxStreamId && ((e.DUID != P25DUID.TDU) && (e.DUID != P25DUID.TDULC)))
                    {
                        channel.IsReceiving = true;
                        channel.PeerId = e.PeerId;
                        channel.RxStreamId = e.StreamId;

                        slot.RxStart = pktTime;
                        Log.WriteLine($"({system.Name}) P25D: Traffic *CALL START     * PEER {e.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");

                        FneUtils.Memset(channel.mi, 0x00, P25Defines.P25_MI_LENGTH);

                        // make channel enc parameters sane
                        if (channel.algId == 0 && channel.kId == 0)
                            channel.algId = P25Defines.P25_ALGO_UNENCRYPT;

                        callHistoryWindow.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId, DateTime.Now.ToString());
                        channel.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId, DateTime.Now.ToString());
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, encrypted);

                        if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                            Log.WriteLine($"({system.Name}) P25D: Traffic *CALL ENC PARMS * PEER {e.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                    }

                    // reset the channel state if we're not Rx
                    if (!channel.IsReceiving)
                    {
                        channel.Background = ChannelBox.BLUE_GRADIENT;
                        channel.VolumeMeterLevel = 0;
                        continue;
                    }

                    // is the call over?
                    if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (slot.RxType != fnecore.FrameType.TERMINATOR))
                    {
                        channel.IsReceiving = false;
                        channel.PeerId = 0;
                        channel.RxStreamId = 0;

                        TimeSpan callDuration = pktTime - slot.RxStart;
                        Log.WriteLine($"({system.Name}) P25D: Traffic *CALL END       * PEER {e.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        channel.Background = ChannelBox.BLUE_GRADIENT;
                        channel.VolumeMeterLevel = 0;
                        callHistoryWindow.ChannelUnkeyed(cpgChannel.Name, (int)e.SrcId);
                        continue;
                    }

                    // do background updates here -- this catches late entry
                    if (channel.algId != P25Defines.P25_ALGO_UNENCRYPT)
                        channel.Background = ChannelBox.ORANGE_GRADIENT;
                    else
                        channel.Background = ChannelBox.GREEN_GRADIENT;

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

                    byte[] newMI = new byte[P25Defines.P25_MI_LENGTH];
                    int count = 0;

                    switch (e.DUID)
                    {
                        case P25DUID.LDU1:
                            {
                                // The '62', '63', '64', '65', '66', '67', '68', '69', '6A' records are LDU1
                                if ((data[0U] == 0x62U) && (data[22U] == 0x63U) &&
                                    (data[36U] == 0x64U) && (data[53U] == 0x65U) &&
                                    (data[70U] == 0x66U) && (data[87U] == 0x67U) &&
                                    (data[104U] == 0x68U) && (data[121U] == 0x69U) &&
                                    (data[138U] == 0x6AU))
                                {
                                    // The '62' record - IMBE Voice 1
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 0, 22);
                                    count += 22;

                                    // The '63' record - IMBE Voice 2
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 25, 14);
                                    count += 14;

                                    // The '64' record - IMBE Voice 3 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 50, 17);
                                    byte serviceOptions = data[count + 3];
                                    count += 17;

                                    // The '65' record - IMBE Voice 4 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 75, 17);
                                    count += 17;

                                    // The '66' record - IMBE Voice 5 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 100, 17);
                                    count += 17;

                                    // The '67' record - IMBE Voice 6 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 125, 17);
                                    count += 17;

                                    // The '68' record - IMBE Voice 7 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 150, 17);
                                    count += 17;

                                    // The '69' record - IMBE Voice 8 + Link Control
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 175, 17);
                                    count += 17;

                                    // The '6A' record - IMBE Voice 9 + Low Speed Data
                                    Buffer.BlockCopy(data, count, channel.netLDU1, 200, 16);
                                    count += 16;

                                    // decode 9 IMBE codewords into PCM samples
                                    P25DecodeAudioFrame(channel.netLDU1, e, handler, channel);
                                }
                            }
                            break;
                        case P25DUID.LDU2:
                            {
                                // The '6B', '6C', '6D', '6E', '6F', '70', '71', '72', '73' records are LDU2
                                if ((data[0U] == 0x6BU) && (data[22U] == 0x6CU) &&
                                    (data[36U] == 0x6DU) && (data[53U] == 0x6EU) &&
                                    (data[70U] == 0x6FU) && (data[87U] == 0x70U) &&
                                    (data[104U] == 0x71U) && (data[121U] == 0x72U) &&
                                    (data[138U] == 0x73U))
                                {
                                    // The '6B' record - IMBE Voice 10
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 0, 22);
                                    count += 22;

                                    // The '6C' record - IMBE Voice 11
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 25, 14);
                                    count += 14;

                                    // The '6D' record - IMBE Voice 12 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 50, 17);
                                    newMI[0] = data[count + 1];
                                    newMI[1] = data[count + 2];
                                    newMI[2] = data[count + 3];
                                    count += 17;

                                    // The '6E' record - IMBE Voice 13 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 75, 17);
                                    newMI[3] = data[count + 1];
                                    newMI[4] = data[count + 2];
                                    newMI[5] = data[count + 3];
                                    count += 17;

                                    // The '6F' record - IMBE Voice 14 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 100, 17);
                                    newMI[6] = data[count + 1];
                                    newMI[7] = data[count + 2];
                                    newMI[8] = data[count + 3];
                                    count += 17;

                                    // The '70' record - IMBE Voice 15 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 125, 17);
                                    channel.algId = data[count + 1];                                    // Algorithm ID
                                    channel.kId = (ushort)((data[count + 2] << 8) | data[count + 3]);   // Key ID
                                    count += 17;

                                    // The '71' record - IMBE Voice 16 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 150, 17);
                                    count += 17;

                                    // The '72' record - IMBE Voice 17 + Encryption Sync
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 175, 17);
                                    count += 17;

                                    // The '73' record - IMBE Voice 18 + Low Speed Data
                                    Buffer.BlockCopy(data, count, channel.netLDU2, 200, 16);
                                    count += 16;

                                    if (channel.p25Errs > 0) // temp, need to actually get errors I guess
                                        P25Crypto.CycleP25Lfsr(channel.mi);
                                    else
                                        Array.Copy(newMI, channel.mi, P25Defines.P25_MI_LENGTH);

                                    // decode 9 IMBE codewords into PCM samples
                                    P25DecodeAudioFrame(channel.netLDU2, e, handler, channel, P25DUID.LDU2);
                                }
                            }
                            break;
                    }

                    if (channel.mi != null)
                        channel.Crypter.Prepare(channel.algId, channel.kId, channel.mi);

                    slot.RxRFS = e.SrcId;
                    slot.RxType = e.FrameType;
                    slot.RxTGId = e.DstId;
                    slot.RxTime = pktTime;
                    slot.RxStreamId = e.StreamId;

                }
            });
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole
