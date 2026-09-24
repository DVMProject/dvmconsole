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
*   Copyright (C) 2025 Lorenzo L Romero, K2LLR
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.Windows;

using dvmconsole.Controls;
using dvmconsole.DMR;

using fnecore;
using fnecore.DMR;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool ValidateDmrTransmit(Codeplug.Channel config, ChannelBox channel = null, bool showWarning = false)
        {
            if (config.GetChannelMode() != Codeplug.ChannelMode.DMR)
                return true;
            try
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(config);
                if (!uint.TryParse(config.GetSourceRid(system), out uint rid) || rid is 0 or > 0xFFFFFF ||
                    !uint.TryParse(config.Tgid, out uint tgid) || tgid is 0 or > 0xFFFFFF)
                    throw new InvalidOperationException("DMR RID and TGID must be between 1 and 16777215.");
                if (config.Slot is < 1 or > 2)
                    throw new InvalidOperationException("DMR timeslot must be 1 or 2.");
                channel ??= FindChannelBySystemAndTgid(config.System, config.Tgid);
                if (channel != null && !channel.DmrEndTask.IsCompleted)
                    throw new InvalidOperationException("The previous DMR call is still finishing. Try PTT again.");
                if (config.GetDmrAlgorithmId() != 0)
                    throw new InvalidOperationException("Encrypted DMR is unavailable in this build. Use a channel explicitly configured with algo: none.");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"DMR TX blocked on {config.Name}: {ex.Message}");
                if (showWarning)
                    Dispatcher.Invoke(() => MessageBox.Show(ex.Message, "DMR Transmit Unavailable",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                return false;
            }
        }

        /// <summary>
        /// Helper to encode and transmit PCM audio as DMR AMBE frames.
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="fne"></param>
        /// <param name="channel"></param>
        /// <param name="cpgChannel"></param>
        /// <param name="system"></param>
        private void DMREncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel,
            Codeplug.Channel config, Codeplug.System system, uint? sourceIdOverride = null)
        {
            lock (channel.DmrSync)
            {
                uint stream = channel.TxStreamId;
                if (stream == 0 || channel.DmrFailedStreamId == stream || !channel.DmrEndTask.IsCompleted)
                    return;
                try
                {
                    if (pcm.Length != PCM_SAMPLES_LENGTH)
                        throw new ArgumentException("DMR requires 20 ms PCM chunks.");
                    if (channel.DmrTx == null)
                    {
                        uint source = sourceIdOverride ?? uint.Parse(config.GetSourceRid(system));
                        if (config.GetDmrAlgorithmId() != 0 || channel.IsTxEncrypted)
                            throw new InvalidOperationException("Encrypted DMR is unavailable; refusing clear fallback.");
                        var call = new DMRCallData
                        {
                            SrcId = source, DstId = uint.Parse(config.Tgid),
                            Slot = checked((byte)config.Slot), TxStreamID = stream
                        };
                        channel.DmrTx = new DmrTxCall(fne, call,
                            samples => EncodeDmrCodeword(channel, samples),
                            (packet, sequence) =>
                            {
                                if (!IsFneSystemConnected(system.Name))
                                    throw new InvalidOperationException("The FNE disconnected during DMR transmit.");
                                fne.SendDMRFrame(call, packet, sequence);
                            });
                    }
                    short[] samples = new short[160];
                    Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
                    channel.DmrTx.Process(samples);
                    Dispatcher.BeginInvoke(new Action(() =>
                        UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.ConsoleTx)));
                }
                catch (Exception ex)
                {
                    Log.WriteError($"({system.Name}) DMR TX stopped: {ex.Message}");
                    channel.DmrFailedStreamId = stream;
                    EndDmrTransmission(channel, discardPending: true);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (channel.TxStreamId != stream)
                            return;
                        EndTarTxRecording(channel, system, config);
                        channel.PttState = false;
                        ResetChannel(channel);
                    }));
                }
            }
        }

        private byte[] EncodeDmrCodeword(ChannelBox channel, short[] samples)
        {
            if (channel.ExternalVocoderEnabled)
            {
                channel.ExtHalfRateVocoder ??= new AmbeVocoder(false);
                channel.ExtHalfRateVocoder.EncoderAgcEnabled = IsAudioInputAgcEnabled();
                channel.ExtHalfRateVocoder.encode(samples, out byte[] encoded, true);
                return encoded;
            }
            channel.Encoder ??= new MBEEncoder(MBE_MODE.DMR_AMBE);
            byte[] ambe = new byte[DMRFrame.CodewordBytes];
            channel.Encoder.encode(samples, ambe);
            return ambe;
        }

        private void EndDmrTransmission(ChannelBox channel, bool discardPending = false)
        {
            lock (channel.DmrSync)
            {
                DmrTxCall call = channel.DmrTx;
                if (call == null)
                    return;
                channel.DmrTx = null;
                try
                {
                    if (discardPending)
                        call.Abort();
                    else
                        call.End();
                }
                catch (Exception ex) { Log.WriteWarning($"DMR release: {ex.Message}"); }
                channel.DmrEndTask = FinishDmrTransmissionAsync(call);
            }
        }

        private static async Task FinishDmrTransmissionAsync(DmrTxCall call)
        {
            try { await call.Completion.ConfigureAwait(false); }
            catch (Exception ex) { Log.WriteWarning($"DMR transmit worker: {ex.Message}"); }
            finally { call.Dispose(); }
        }

        private void StopDmrChannels()
        {
            foreach (ChannelBox channel in GetAllCanvases().SelectMany(canvas => canvas.Children.OfType<ChannelBox>()))
            {
                Codeplug.Channel config = Codeplug?.GetChannelByName(channel.ChannelName);
                if (config?.GetChannelMode() != Codeplug.ChannelMode.DMR)
                    continue;
                Codeplug.System system = Codeplug.GetSystemForChannel(config);
                SlotStatus status = FindActiveReceiveStatus(channel, config);
                EndTarRxRecordingFromChannelState(system, config, channel, status, DateTime.Now);
                EndTarTxRecording(channel, system, config);
                EndDmrTransmission(channel, discardPending: true);
                ClearReceiveState(channel, status);
                ResetChannel(channel);
            }
        }

        /// <summary>
        /// Helper to decode and playback DMR AMBE frames as PCM audio.
        /// </summary>
        /// <param name="ambe"></param>
        /// <param name="e"></param>
        /// <param name="system"></param>
        /// <param name="channel"></param>
        private void DMRDecodeAudioFrame(byte[] ambe, DMRDataReceivedEvent e, PeerSystem system, ChannelBox channel, string sourceSystemName)
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
                        UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.RadioRx);

                        int pcmIdx = 0;
                        byte[] pcm = new byte[samples.Length * 2];
                        for (int smpIdx = 0; smpIdx < samples.Length; smpIdx++)
                        {
                            pcm[pcmIdx + 0] = (byte)(samples[smpIdx] & 0xFF);
                            pcm[pcmIdx + 1] = (byte)((samples[smpIdx] >> 8) & 0xFF);
                            pcmIdx += 2;
                        }

                        //Log.WriteLine($"PCM BYTE BUFFER {FneUtils.HexDump(pcm)}");
                        if (!ShouldMuteRxPlayback())
                            audioManager.AddTalkgroupStream(channel.AudioOutputKey, pcm);
                        AppendTarRxAudio(sourceSystemName, channel.DstId, e.StreamId, pcm);
                        patchManager.HandleAudio(sourceSystemName, e.DstId.ToString(), e.StreamId, e.SrcId, pcm);
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
        public void DMRDataReceived(string sourceSystemName, DMRDataReceivedEvent e, DateTime pktTime)
        {
            if (!DMRFrame.IsVoicePacket(e.Data) || e.CallType != CallType.GROUP || e.StreamId == 0)
                return;
            try
            {
            Dispatcher.Invoke(() =>
            {
                try
                {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);

                    if (system == null || cpgChannel == null)
                    {
                        Log.WriteWarning($"{channel.ChannelName} could not be resolved while processing DMR RX traffic. {ERR_INVALID_CODEPLUG}.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(sourceSystemName) &&
                        !ResourceIdentity.SystemMatches(system?.Name, sourceSystemName))
                        continue;

                    if (cpgChannel.GetChannelMode() != Codeplug.ChannelMode.DMR)
                        continue;

                    PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                    if (!channel.IsEnabled || channel.Name == PLAYBACKCHNAME)
                        continue;

                    if (cpgChannel.Tgid != e.DstId.ToString())
                        continue;

                    // FNECore receive events use zero-based slots; codeplugs use TS1/TS2.
                    if (Math.Max(1, cpgChannel.Slot) != e.Slot + 1)
                        continue;

                    if (patchManager.IsPatchedTransmitStream(system.Name, cpgChannel.Tgid, e.StreamId))
                        continue;

                    if (channel.PttState)
                        continue;

                    string statusKey = ResourceIdentity.Build(system.Name, cpgChannel.Tgid) + $"|slot:{e.Slot}";
                    if (!systemStatuses.ContainsKey(statusKey))
                        systemStatuses[statusKey] = new SlotStatus();

                    if (channel.Decoder == null)
                        channel.Decoder = new MBEDecoder(MBE_MODE.DMR_AMBE);

                    // is the Rx stream ID being Rx'ed on another copy of this same resource?
                    bool duplicateRx = selectedChannelsManager.GetSelectedChannels().Any(other =>
                        other.InternalID != channel.InternalID &&
                        other.RxStreamId > 0 &&
                        other.RxStreamId == e.StreamId &&
                        ChannelMatchesResource(other, system.Name, cpgChannel.Tgid));

                    // is this duplicate traffic?
                    if (((channel.PeerId > 0 && channel.RxStreamId > 0) && (e.PeerId != channel.PeerId && e.StreamId == channel.RxStreamId)) || duplicateRx)
                    {
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *IGNORE DUP TRAF* PEER {e.PeerId} CALL_START PEER ID {channel.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                        continue;
                    }

                    // is the Rx stream ID any of our Tx stream IDs?
                    bool hasMatchingTxStream = selectedChannelsManager.GetSelectedChannels().Any(other =>
                        other.TxStreamId > 0 &&
                        other.TxStreamId == e.StreamId &&
                        ChannelMatchesResource(other, system.Name, cpgChannel.Tgid));

                    // if we have a count of Tx channels this means we're sourcing traffic for the incoming stream ID
                    if (hasMatchingTxStream)
                    {
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *IGNORE TX TRAF * PEER {e.PeerId} CALL_START PEER ID {channel.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} ALGID {channel.algId} KID {channel.kId} [STREAM ID {e.StreamId}]");
                        continue;
                    }

                    string alias = TryResolveSubscriberAlias(system, (int)e.SrcId);
                    bool isConsoleRid = IsConsoleSourceRid(system, e.SrcId);

                    // is this a new call stream?
                    SlotStatus slotStatus = systemStatuses[statusKey];
                    bool isNewCallStream = !channel.IsReceiving || e.StreamId != slotStatus.RxStreamId;
                    // A late or duplicate terminator must not start a phantom call.
                    if (isNewCallStream && e.FrameType == FrameType.DATA_SYNC && e.DataType == DMRDataType.TERMINATOR_WITH_LC)
                        continue;

                    if (isNewCallStream)
                    {
                        channel.DmrRx?.Dispose();
                        channel.DmrRx = new DMRCallData
                        {
                            SrcId = e.SrcId, DstId = e.DstId, TxStreamID = e.StreamId,
                            Slot = checked((byte)(e.Slot + 1))
                        };
                    }
                    DMRCallData dmrCall = channel.DmrRx;
                    byte[] clearAmbe = new byte[DMRFrame.VoiceBytes];
                    int voiceLength = DMRFrame.Decode(dmrCall, e.Data, clearAmbe);
                    if (!dmrCall.LastPacketAccepted)
                        continue;
                    channel.LastPktTime = pktTime;
                    channel.IsReceivingEncrypted = dmrCall.IsEncrypted;

                    if (isNewCallStream)
                    {
                        patchManager.HandleCallStart(system.Name, cpgChannel.Tgid, e.StreamId, e.SrcId);

                        channel.IsReceiving = true;
                        channel.IsReceivingEncrypted = dmrCall.IsEncrypted;
                        channel.PeerId = e.PeerId;
                        channel.RxStreamId = e.StreamId;
                        
                        // Update tab audio indicator
                        Dispatcher.Invoke(() => UpdateTabAudioIndicatorForChannel(channel));

                        slotStatus.RxStart = pktTime;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL START     * PEER {e.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot + 1} [STREAM ID {e.StreamId}]");

                        slotStatus.DMR_RxLC = dmrCall.LinkControl ?? new LC
                        {
                            SrcId = e.SrcId, DstId = e.DstId
                        };

                        slotStatus.DMR_RxPILC = new PrivacyLC();
                        Log.WriteLine($"({system.Name}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_LC {FneUtils.HexDump(slotStatus.DMR_RxLC.GetBytes())}");

                        Task<TarRecordingMetadata> recording = BeginTarRxRecording(
                            system,
                            cpgChannel,
                            e.StreamId,
                            e.SrcId,
                            alias,
                            dmrCall.IsEncrypted,
                            DescribeDmrEncryptionAlgorithm(dmrCall.AlgorithmId),
                            NormalizeEncryptionKeyId(dmrCall.KeyId),
                            pktTime);

                        if (!isConsoleRid)
                        {
                            callHistoryWindow.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId, alias, DateTime.Now.ToString("HH:mm:ss"), recording);
                            channel.AddCall(cpgChannel.Name, (int)e.SrcId, (int)e.DstId, alias, DateTime.Now.ToString("HH:mm:ss"), recording);
                        }
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, dmrCall.IsEncrypted);

                    }

                    // reset the channel state if we're not Rx
                    if (!channel.IsReceiving)
                    {
                        channel.VolumeMeterLevel = 0;
                        continue;
                    }

                    if (dmrCall.LinkControl is LC receivedControl)
                        slotStatus.DMR_RxLC = receivedControl;
                    if (dmrCall.IsEncrypted && dmrCall.FrameType == FrameType.DATA_SYNC &&
                        dmrCall.DataType == DMRDataType.VOICE_PI_HEADER)
                        Log.WriteWarning($"({system.Name}) Encrypted DMR receive is unavailable; audio is muted.");

                    if (dmrCall.IsReleased)
                    {
                        patchManager.HandleCallEnd(system.Name, cpgChannel.Tgid, e.StreamId);
                        bool isEncrypted = dmrCall.IsEncrypted;
                        EndTarRxRecording(
                            system,
                            cpgChannel,
                            e.StreamId,
                            e.SrcId,
                            alias,
                            isEncrypted,
                            DescribeDmrEncryptionAlgorithm(dmrCall.AlgorithmId),
                            NormalizeEncryptionKeyId(dmrCall.KeyId),
                            pktTime);
                        if (channel.RxStreamId > 0 && channel.RxStreamId != e.StreamId)
                            EndTarRxRecordingFromChannelState(system, cpgChannel, channel, slotStatus, pktTime);

                        ClearReceiveState(channel, slotStatus);
                        audioManager.ReleaseTalkgroupStream(channel.AudioOutputKey);
                        
                        // Update tab audio indicator
                        Dispatcher.Invoke(() => UpdateTabAudioIndicatorForChannel(channel));

                        TimeSpan callDuration = pktTime - slotStatus.RxStart;
                        Log.WriteLine($"({system.Name}) DMRD: Traffic *CALL END       * PEER {e.PeerId} SYS {system.Name} SRC_ID {e.SrcId} TGID {e.DstId} TS {e.Slot} DUR {callDuration} [STREAM ID {e.StreamId}]");
                        callHistoryWindow.ChannelUnkeyed(cpgChannel.Name, (int)e.SrcId);
                        continue;
                    }

                    if (!isConsoleRid)
                    {
                        if (string.IsNullOrEmpty(alias))
                            channel.LastSrcId = "Last ID: " + e.SrcId;
                        else
                            channel.LastSrcId = "Last: " + alias;
                    }

                    if (e.FrameType == FrameType.VOICE_SYNC || e.FrameType == FrameType.VOICE)
                    {
                        bool isEncrypted = dmrCall.IsEncrypted;
                        if (dmrCall.AlgorithmId != 0)
                        {
                            slotStatus.DMR_RxPILC ??= new PrivacyLC();
                            slotStatus.DMR_RxPILC.AlgId = dmrCall.AlgorithmId;
                            slotStatus.DMR_RxPILC.KId = dmrCall.KeyId;
                            slotStatus.DMR_RxPILC.FID = dmrCall.MFId;
                        }
                        EnsureTarRxRecording(
                            system,
                            cpgChannel,
                            channel,
                            e.PeerId,
                            e.StreamId,
                            e.SrcId,
                            alias,
                            isEncrypted,
                            DescribeDmrEncryptionAlgorithm(dmrCall.AlgorithmId),
                            NormalizeEncryptionKeyId(dmrCall.KeyId),
                            pktTime,
                            "DMR voice frame");
                        callHistoryWindow.ChannelKeyed(cpgChannel.Name, (int)e.SrcId, isEncrypted);
                        if (voiceLength == DMRFrame.VoiceBytes)
                            DMRDecodeAudioFrame(clearAmbe, e, handler, channel, system.Name);
                    }

                    slotStatus.RxRFS = e.SrcId;
                    slotStatus.RxType = e.FrameType;
                    slotStatus.RxTGId = e.DstId;
                    slotStatus.RxTime = pktTime;
                    slotStatus.RxStreamId = e.StreamId;
                }
                }
                catch (Exception ex)
                {
                    Log.WriteError($"DMR RX dispatch exception: {ex.Message}");
                    Log.StackTrace(ex, false);
                }
            });
            }
            catch (Exception ex)
            {
                Log.WriteError($"DMR RX handler exception: {ex.Message}");
                Log.StackTrace(ex, false);
            }
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole
