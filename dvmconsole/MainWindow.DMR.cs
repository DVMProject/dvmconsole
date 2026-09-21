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
using System.Security.Cryptography;

using dvmconsole.Controls;
using dvmconsole.DMR;

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
        private readonly object dmrKeySync = new();
        private readonly Dictionary<(string System, byte Algorithm, ushort Id), byte[]> dmrKeys = new();
        private readonly HashSet<(string System, byte Algorithm, ushort Id)> dmrLocalKeys = new();

        private static string DmrKeySystem(string name) => (name ?? string.Empty).Trim().ToUpperInvariant();

        private void SetDmrKey(string system, byte algorithm, ushort id, byte[] key, bool local = false)
        {
            if (id is 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(id), "DMR key IDs are between 1 and 255.");
            using var validator = new DmrPrivacyProcessor(
                DmrPrivacyOptions.CreateRandom(algorithm, checked((byte)id), key), new DmrAmbeCodec());
            lock (dmrKeySync)
            {
                var index = (DmrKeySystem(system), algorithm, id);
                if (!local && (dmrLocalKeys.Contains(index) || dmrLocalKeys.Contains((string.Empty, algorithm, id))))
                    return;
                if (local)
                    dmrLocalKeys.Add(index);
                if (dmrKeys.Remove(index, out byte[] previous))
                    CryptographicOperations.ZeroMemory(previous);
                dmrKeys[index] = key.ToArray();
            }
        }

        private byte[] GetDmrKey(string system, byte algorithm, ushort id)
        {
            lock (dmrKeySync)
            {
                string scope = DmrKeySystem(system);
                if (dmrLocalKeys.Contains((string.Empty, algorithm, id)) &&
                    !dmrLocalKeys.Contains((scope, algorithm, id)))
                    return dmrKeys[(string.Empty, algorithm, id)].ToArray();
                if (dmrKeys.TryGetValue((scope, algorithm, id), out byte[] key) ||
                    dmrKeys.TryGetValue((string.Empty, algorithm, id), out key))
                    return key.ToArray();
                return null;
            }
        }

        internal bool HasDmrKey(Codeplug.Channel channel)
        {
            byte[] key = GetDmrKey(channel.System, channel.GetDmrAlgorithmId(), channel.GetKeyId());
            if (key == null)
                return false;
            CryptographicOperations.ZeroMemory(key);
            return true;
        }

        private void ImportDmrNetworkKeys(KeyResponseEvent e, string sourceSystemName)
        {
            if (sourceSystemName == null)
                return;
            byte algorithm = DmrPrivacyAlgorithms.FromKeyRequestAlgorithm(e.KmmKey.KeysetItem.AlgId);
            if (algorithm == 0)
                return;
            foreach (Codeplug.System system in Codeplug?.Systems ?? [])
            {
                if (!ResourceIdentity.SystemMatches(sourceSystemName, system.Name))
                    continue;
                foreach (var key in e.KmmKey.KeysetItem.Keys.Where(item => item.KeyId is > 0 and <= byte.MaxValue))
                {
                    if (!(Codeplug.Zones ?? []).SelectMany(zone => zone.Channels ?? []).Any(channel =>
                        channel.GetChannelMode() == Codeplug.ChannelMode.DMR &&
                        ResourceIdentity.SystemMatches(channel.System, system.Name) &&
                        channel.GetDmrAlgorithmId() == algorithm && channel.GetKeyId() == key.KeyId))
                        continue;
                    byte[] material = key.GetKey().ToArray();
                    try { SetDmrKey(system.Name, algorithm, key.KeyId, material); }
                    catch (Exception ex) { Log.WriteWarning($"({system.Name}) Invalid DMR key {key.KeyId}: {ex.Message}"); }
                    finally { CryptographicOperations.ZeroMemory(material); }
                }
            }
        }

        private void ClearDmrKeys()
        {
            lock (dmrKeySync)
            {
                foreach (byte[] key in dmrKeys.Values)
                    CryptographicOperations.ZeroMemory(key);
                dmrKeys.Clear();
                dmrLocalKeys.Clear();
            }
        }

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
                byte algorithm = config.GetDmrAlgorithmId();
                if (algorithm is not (0 or DmrPrivacyAlgorithms.Arc4 or DmrPrivacyAlgorithms.DesOfb or DmrPrivacyAlgorithms.Aes256))
                    throw new InvalidOperationException("DMR supports none, arc4, des, or aes encryption.");
                if (algorithm != 0 && config.GetKeyId() is 0 or > byte.MaxValue)
                    throw new InvalidOperationException("DMR key IDs must be 1-255 (keyId is hexadecimal).");
                if (algorithm != 0 && (channel?.IsTxEncrypted ?? true) && !HasDmrKey(config))
                    throw new InvalidOperationException("DMR encryption key is unavailable. Load the key before transmitting.");
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
                        byte[] key = null;
                        DmrPrivacyOptions privacy = null;
                        try
                        {
                            if (channel.IsTxEncrypted)
                            {
                                byte algorithm = config.GetDmrAlgorithmId();
                                key = GetDmrKey(system.Name, algorithm, config.GetKeyId()) ??
                                    throw new InvalidOperationException("DMR key unavailable; refusing clear fallback.");
                                privacy = DmrPrivacyOptions.CreateRandom(algorithm,
                                    checked((byte)config.GetKeyId()), key);
                            }
                            channel.DmrTx = new DmrTxCall(source, uint.Parse(config.Tgid),
                                checked((byte)config.Slot), stream,
                                samples => EncodeDmrCodeword(channel, samples),
                                (packet, sequence) =>
                                {
                                    if (IsFneSystemConnected(system.Name))
                                        fne.peer.SendMasterTraffic(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL,
                                            Constants.NET_PROTOCOL_SUBFUNC_DMR), packet, sequence, stream);
                                }, privacy);
                        }
                        finally
                        {
                            if (key != null)
                                CryptographicOperations.ZeroMemory(key);
                        }
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
            byte[] ambe = new byte[DmrVoicePacketCodec.CodewordBytes];
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

                    byte[] data = new byte[DmrVoicePacketCodec.FrameBytes];
                    Buffer.BlockCopy(e.Data, DmrVoicePacketCodec.HeaderBytes, data, 0, DmrVoicePacketCodec.FrameBytes);

                    channel.LastPktTime = pktTime;

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
                        channel.DmrRx = new DmrRxCall(e.SrcId, e.DstId, e.StreamId,
                            (algorithm, id) => GetDmrKey(system.Name, algorithm, id), new DmrAmbeCodec());
                    }
                    DmrRxCall dmrCall = channel.DmrRx;
                    byte[] clearAmbe = dmrCall.Process(e.Data, e.FrameType, e.DataType, e.n);
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

                        // if we can, use the LC from the voice header as to keep all options intact
                        if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_LC_HEADER))
                        {
                            LC lc = FullLC.Decode(data, DMRDataType.VOICE_LC_HEADER);
                            slotStatus.DMR_RxLC = lc;
                        }
                        else // if we don't have a voice header; don't wait to decode it, just make a dummy header
                            slotStatus.DMR_RxLC = new LC()
                            {
                                SrcId = e.SrcId,
                                DstId = e.DstId
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

                    // if we can, use the PI LC from the PI voice header as to keep all options intact
                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.VOICE_PI_HEADER))
                    {
                        PrivacyLC lc = FullLC.DecodePI(data);
                        slotStatus.DMR_RxPILC = lc;
                        if (dmrCall.IsEncrypted && !dmrCall.HasKey)
                            Log.WriteWarning($"({system.Name}) DMR encrypted receive is missing ALGID {dmrCall.AlgorithmId} KID {dmrCall.KeyId}.");
                        //Log.WriteLine($"({SystemName}) DMRD: Traffic *CALL PI PARAMS  * PEER {e.PeerId} DST_ID {e.DstId} TS {e.Slot + 1} ALGID {lc.AlgId} KID {lc.KId} [STREAM ID {e.StreamId}]");
                        //Log.WriteLine($"({SystemName}) TS {e.Slot + 1} [STREAM ID {e.StreamId}] RX_PI_LC {FneUtils.HexDump(systemStatuses[cpgChannel.Name + e.Slot].DMR_RxPILC.GetBytes())}");
                    }

                    if ((e.FrameType == FrameType.DATA_SYNC) && (e.DataType == DMRDataType.TERMINATOR_WITH_LC) && (slotStatus.RxType != FrameType.TERMINATOR))
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
                            slotStatus.DMR_RxPILC.FID = DmrPrivacyAlgorithms.FeatureId;
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
                        if (clearAmbe.Length == DmrVoicePacketCodec.AmbeBytes)
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
