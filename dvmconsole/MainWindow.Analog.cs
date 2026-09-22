// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using dvmconsole.Controls;
using fnecore;
using fnecore.Analog;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private bool ValidateAnalogTransmit(Codeplug.Channel config, bool showWarning = false)
        {
            if (config.GetChannelMode() != Codeplug.ChannelMode.Analog)
                return true;
            Codeplug.System system = Codeplug.GetSystemForChannel(config);
            bool valid = uint.TryParse(config.GetSourceRid(system), out uint sourceId) && sourceId is > 0 and <= 0xFFFFFF &&
                uint.TryParse(config.Tgid, out uint destinationId) && destinationId is > 0 and <= 0xFFFFFF;
            bool clear = string.IsNullOrWhiteSpace(config.Algo) ||
                string.Equals(config.Algo, "none", StringComparison.OrdinalIgnoreCase);
            bool scramblerValid = config.ScramblerCode == 0 ||
                AnalogVoiceInversion.TryGetFrequency(config.ScramblerCode, out _);
            if (valid && clear && scramblerValid)
                return true;
            string message = !valid ? "Analog RID and TGID must be between 1 and 16777215." :
                !clear ? "Analog voice inversion uses scrambler_code, not algo. Set algo: none." :
                "Analog scrambler_code must be 0 (off) or 2 through 16. Code 1's frequency is unknown.";
            Log.WriteWarning($"Analog TX blocked on {config.Name}: {message}");
            if (showWarning)
                Dispatcher.Invoke(() => System.Windows.MessageBox.Show(message, "Analog Transmit Unavailable",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
            return false;
        }

        private void AnalogEncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel,
            Codeplug.Channel config, Codeplug.System system, uint? sourceIdOverride = null, uint? expectedStreamId = null)
        {
            lock (channel.AnalogSync)
            {
                uint stream = channel.TxStreamId;
                if (stream == 0 || (expectedStreamId.HasValue && stream != expectedStreamId.Value) ||
                    !IsFneSystemConnected(system.Name) || pcm?.Length != PCM_SAMPLES_LENGTH)
                    return;
                try
                {
                    uint sourceId = sourceIdOverride ?? uint.Parse(config.GetSourceRid(system));
                    uint destinationId = uint.Parse(config.Tgid);
                    short[] samples = new short[AnalogVoicePacketCodec.SampleCount];
                    Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
                    if (!channel.AnalogStarted)
                        channel.AnalogTxInverter = !channel.IsTxEncrypted ? null : new AnalogVoiceInversion(config.ScramblerCode);
                    channel.AnalogTxInverter?.Process(samples);
                    AudioFrameType frameType = channel.AnalogStarted ? AudioFrameType.VOICE : AudioFrameType.VOICE_START;
                    if (channel.pktSeq == Constants.RtpCallEndSeq)
                        channel.pktSeq = 0;
                    byte[] packet = AnalogVoicePacketCodec.Encode(sourceId, destinationId, (byte)channel.pktSeq, frameType, samples);
                    fne.peer.SendMasterTraffic(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL,
                        Constants.NET_PROTOCOL_SUBFUNC_ANALOG), packet, channel.pktSeq++, stream);
                    channel.AnalogSourceId = sourceId;
                    channel.AnalogStarted = true;
                    Dispatcher.BeginInvoke(new Action(() => UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.ConsoleTx)));
                }
                catch (Exception ex)
                {
                    Log.WriteError($"({system.Name}) Analog TX stopped: {ex.Message}");
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

        private void EndAnalogTransmission(ChannelBox channel)
        {
            lock (channel.AnalogSync)
            {
                if (!channel.AnalogStarted)
                    return;
                channel.AnalogStarted = false;
                channel.AnalogTxInverter = null;
                Codeplug.Channel config = Codeplug?.GetChannelByName(channel.ChannelName);
                Codeplug.System system = config == null ? null : Codeplug.GetSystemForChannel(config);
                PeerSystem fne = system == null ? null : fneSystemManager.GetFneSystem(system.Name);
                if (config == null || system == null || fne == null || channel.TxStreamId == 0 ||
                    !IsFneSystemConnected(system.Name))
                    return;
                try
                {
                    uint sourceId = channel.AnalogSourceId != 0 ? channel.AnalogSourceId : uint.Parse(config.GetSourceRid(system));
                    byte[] packet = AnalogVoicePacketCodec.Encode(sourceId, uint.Parse(config.Tgid),
                        (byte)channel.pktSeq, AudioFrameType.TERMINATOR, ReadOnlySpan<short>.Empty);
                    fne.peer.SendMasterTraffic(new Tuple<byte, byte>(Constants.NET_FUNC_PROTOCOL,
                        Constants.NET_PROTOCOL_SUBFUNC_ANALOG), packet, Constants.RtpCallEndSeq, channel.TxStreamId);
                }
                catch (Exception ex) { Log.WriteWarning($"({system.Name}) Analog release: {ex.Message}"); }
                finally { channel.AnalogSourceId = 0; }
            }
        }

        private void StopAnalogChannels()
        {
            foreach (ChannelBox channel in GetAllCanvases().SelectMany(canvas => canvas.Children.OfType<ChannelBox>()))
            {
                Codeplug.Channel config = Codeplug?.GetChannelByName(channel.ChannelName);
                if (config?.GetChannelMode() != Codeplug.ChannelMode.Analog)
                    continue;
                Codeplug.System system = Codeplug.GetSystemForChannel(config);
                SlotStatus status = FindActiveReceiveStatus(channel, config);
                EndTarRxRecordingFromChannelState(system, config, channel, status, DateTime.Now);
                EndTarTxRecording(channel, system, config);
                EndAnalogTransmission(channel);
                channel.AnalogRxInverter = null;
                ClearReceiveState(channel, status);
                ResetChannel(channel);
            }
        }

        public void AnalogDataReceived(string sourceSystemName, AnalogDataReceivedEvent e, DateTime packetTime)
        {
            if (e.CallType != CallType.GROUP || e.StreamId == 0 || e.SrcId == 0 || e.DstId == 0 ||
                e.AudioFrameType is not (AudioFrameType.VOICE_START or AudioFrameType.VOICE or AudioFrameType.TERMINATOR) ||
                e.Data == null || e.Data.Length < (e.AudioFrameType == AudioFrameType.TERMINATOR ? 20 : 344) ||
                e.Data[0] != 'A' || e.Data[1] != 'N' ||
                e.Data[2] != 'O' || e.Data[3] != 'D')
                return;
            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels().ToList())
                {
                    Codeplug.Channel config = Codeplug.GetChannelByName(channel.ChannelName);
                    Codeplug.System system = config == null ? null : Codeplug.GetSystemForChannel(config);
                    if (system == null || config.GetChannelMode() != Codeplug.ChannelMode.Analog ||
                        !ResourceIdentity.SystemMatches(system.Name, sourceSystemName) || config.Tgid != e.DstId.ToString() ||
                        !channel.IsEnabled || channel.PttState || channel.TxStreamId != 0 ||
                        patchManager.IsPatchedTransmitStream(system.Name, config.Tgid, e.StreamId))
                        continue;
                    bool duplicate = selectedChannelsManager.GetSelectedChannels().Any(other => other.InternalID != channel.InternalID &&
                        ChannelMatchesResource(other, system.Name, config.Tgid) &&
                        (other.RxStreamId == e.StreamId || other.TxStreamId == e.StreamId));
                    if (duplicate || (channel.RxStreamId == e.StreamId && channel.PeerId != e.PeerId))
                        continue;
                    string statusKey = ResourceIdentity.Build(system.Name, config.Tgid) + "|analog";
                    if (!systemStatuses.TryGetValue(statusKey, out SlotStatus status))
                        systemStatuses[statusKey] = status = new SlotStatus();
                    bool newCall = channel.RxStreamId != e.StreamId;
                    if (newCall && e.AudioFrameType != AudioFrameType.VOICE_START)
                        continue;
                    if (newCall && config.ScramblerCode != 0 &&
                        !AnalogVoiceInversion.TryGetFrequency(config.ScramblerCode, out _))
                    {
                        Log.WriteWarning($"({system.Name}) Analog RX ignored on {config.Name}: unsupported scrambler_code {config.ScramblerCode}.");
                        continue;
                    }
                    try
                    {
                        string alias = TryResolveSubscriberAlias(system, (int)e.SrcId);
                        if (newCall)
                        {
                            if (channel.RxStreamId != 0)
                            {
                                EndTarRxRecordingFromChannelState(system, config, channel, status, packetTime);
                                patchManager.HandleCallEnd(system.Name, config.Tgid, channel.RxStreamId);
                                callHistoryWindow.ClearChannelActivity(channel.ChannelName);
                            }
                            channel.IsReceiving = true;
                            channel.IsReceivingEncrypted = false;
                            channel.PeerId = e.PeerId;
                            channel.RxStreamId = e.StreamId;
                            bool decodeScrambledRx = config.ScramblerCode != 0 &&
                                (!channel.IsEncryptionSelectable || channel.IsTxEncrypted);
                            channel.AnalogRxInverter = decodeScrambledRx ? new AnalogVoiceInversion(config.ScramblerCode) : null;
                            status.RxStart = packetTime;
                            patchManager.HandleCallStart(system.Name, config.Tgid, e.StreamId, e.SrcId);
                        }
                        channel.LastPktTime = packetTime;
                        channel.LastSrcId = string.IsNullOrEmpty(alias) ? "Last ID: " + e.SrcId : "Last: " + alias;
                        status.RxRFS = e.SrcId;
                        status.RxTGId = e.DstId;
                        status.RxStreamId = e.StreamId;
                        status.RxTime = packetTime;
                        status.RxType = e.FrameType;
                        if (newCall)
                        {
                            var recording = BeginTarRxRecording(system, config, e.StreamId, e.SrcId, alias,
                                false, string.Empty, null, packetTime);
                            callHistoryWindow.AddCall(config.Name, (int)e.SrcId, (int)e.DstId, alias, packetTime.ToString("HH:mm:ss"), recording);
                            channel.AddCall(config.Name, (int)e.SrcId, (int)e.DstId, alias, packetTime.ToString("HH:mm:ss"), recording);
                            Log.WriteLine($"({system.Name}) Analog Traffic *CALL START     * SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");
                        }
                        if (e.AudioFrameType == AudioFrameType.TERMINATOR)
                        {
                            EndTarRxRecordingFromChannelState(system, config, channel, status, packetTime);
                            patchManager.HandleCallEnd(system.Name, config.Tgid, e.StreamId);
                            ClearReceiveState(channel, status);
                            channel.AnalogRxInverter = null;
                            audioManager.ReleaseTalkgroupStream(channel.AudioOutputKey);
                            callHistoryWindow.ChannelUnkeyed(config.Name, (int)e.SrcId);
                            Log.WriteLine($"({system.Name}) Analog Traffic *CALL END       * SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");
                        }
                        else if (AnalogVoicePacketCodec.TryDecode(e.Data, out short[] samples))
                        {
                            channel.AnalogRxInverter?.Process(samples);
                            callHistoryWindow.ChannelKeyed(config.Name, (int)e.SrcId, false);
                            byte[] pcm = new byte[PCM_SAMPLES_LENGTH];
                            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                            UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.RadioRx);
                            if (!ShouldMuteRxPlayback())
                                audioManager.AddTalkgroupStream(channel.AudioOutputKey, pcm);
                            AppendTarRxAudio(system.Name, config.Tgid, e.StreamId, pcm);
                            patchManager.HandleAudio(system.Name, config.Tgid, e.StreamId, e.SrcId, pcm);
                        }
                        UpdateTabAudioIndicatorForChannel(channel);
                    }
                    catch (Exception ex) { Log.WriteWarning($"({system.Name}) Analog RX ignored: {ex.Message}"); }
                }
            });
        }
    }
}
