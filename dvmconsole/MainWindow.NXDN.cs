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

using System.Security.Cryptography;
using dvmconsole.Controls;
using dvmconsole.NXDN;
using fnecore;
using fnecore.NXDN;
using fnecore.P25;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private readonly object nxdnKeySync = new();
        private readonly Dictionary<(string System, byte Cipher, ushort Id), byte[]> nxdnKeys = new();
        private readonly HashSet<(string System, byte Cipher, ushort Id)> nxdnLocalKeys = new();

        private static string NxdnKeySystem(string name) => (name ?? string.Empty).Trim().ToUpperInvariant();

        private void SetNxdnKey(string system, byte cipher, ushort id, byte[] key, bool local = false)
        {
            // Validate key sizes and reject unusable DES/EHR material before storing it.
            using var validator = new NXDNCrypto();
            validator.SetKey(id, cipher, key);
            validator.Prepare(cipher, id, NXDNCrypto.CreateMessageIndicator());
            lock (nxdnKeySync)
            {
                var index = (NxdnKeySystem(system), cipher, id);
                if (!local && (nxdnLocalKeys.Contains(index) || nxdnLocalKeys.Contains((string.Empty, cipher, id))))
                    return;
                if (local)
                    nxdnLocalKeys.Add(index);
                if (nxdnKeys.Remove(index, out byte[] previous))
                    CryptographicOperations.ZeroMemory(previous);
                nxdnKeys[index] = key.ToArray();
            }
        }

        private byte[] GetNxdnKey(string system, byte cipher, ushort id)
        {
            lock (nxdnKeySync)
            {
                if (nxdnLocalKeys.Contains((string.Empty, cipher, id)) &&
                    !nxdnLocalKeys.Contains((NxdnKeySystem(system), cipher, id)))
                    return nxdnKeys[(string.Empty, cipher, id)].ToArray();
                if (nxdnKeys.TryGetValue((NxdnKeySystem(system), cipher, id), out byte[] key) ||
                    nxdnKeys.TryGetValue((string.Empty, cipher, id), out key))
                    return key.ToArray();
                return null;
            }
        }

        internal bool HasNxdnKey(Codeplug.Channel channel)
        {
            byte[] key = GetNxdnKey(channel.System, channel.GetNxdnCipherType(), channel.GetKeyId());
            if (key == null)
                return false;
            CryptographicOperations.ZeroMemory(key);
            return true;
        }

        private void ImportNxdnNetworkKeys(KeyResponseEvent e, string sourceSystemName)
        {
            // Synthetic P25 key-file events are not NXDN entries. Only real FNE
            // responses may bridge KMM algorithm IDs to NXDN wire cipher IDs.
            if (sourceSystemName == null)
                return;
            byte cipher = NXDNCrypto.FromKeyRequestAlgorithm(e.KmmKey.KeysetItem.AlgId);
            if (cipher == 0)
                return;
            foreach (Codeplug.System system in Codeplug?.Systems ?? [])
            {
                if (sourceSystemName != null && !ResourceIdentity.SystemMatches(sourceSystemName, system.Name))
                    continue;
                foreach (var key in e.KmmKey.KeysetItem.Keys.Where(key => key.KeyId is > 0 and <= 63))
                {
                    if (!(Codeplug.Zones ?? []).SelectMany(zone => zone.Channels ?? []).Any(channel =>
                        channel.GetChannelMode() == Codeplug.ChannelMode.NXDN &&
                        ResourceIdentity.SystemMatches(channel.System, system.Name) &&
                        channel.GetNxdnCipherType() == cipher && channel.GetKeyId() == key.KeyId))
                        continue;
                    byte[] material = key.GetKey().ToArray();
                    try
                    {
                        SetNxdnKey(system.Name, cipher, key.KeyId, material);
                    }
                    catch (Exception ex) { Log.WriteWarning($"({system.Name}) Invalid NXDN key {key.KeyId}: {ex.Message}"); }
                    finally { CryptographicOperations.ZeroMemory(material); }
                }
            }
        }

        private void ClearNxdnKeys()
        {
            lock (nxdnKeySync)
            {
                foreach (byte[] key in nxdnKeys.Values)
                    CryptographicOperations.ZeroMemory(key);
                nxdnKeys.Clear();
                nxdnLocalKeys.Clear();
            }
        }

        private bool ValidateNxdnTransmit(Codeplug.Channel config, ChannelBox channel = null, bool showWarning = false)
        {
            if (config.GetChannelMode() != Codeplug.ChannelMode.NXDN)
                return true;
            try
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(config);
                if (!ushort.TryParse(config.GetSourceRid(system), out ushort rid) || rid == 0 ||
                    !ushort.TryParse(config.Tgid, out ushort tgid) || tgid == 0)
                    throw new InvalidOperationException("NXDN RID and TGID must be between 1 and 65535. Set nxdnRid when the system RID is larger.");
                if (config.Ran is < 0 or > 63)
                    throw new InvalidOperationException("NXDN RAN must be between 0 and 63.");
                channel ??= FindChannelBySystemAndTgid(config.System, config.Tgid);
                if (channel != null && !channel.NxdnEndTask.IsCompleted)
                    throw new InvalidOperationException("The previous NXDN call is still finishing. Try PTT again.");
                byte cipher = config.GetNxdnCipherType();
                if (cipher > 3)
                    throw new InvalidOperationException("NXDN supports none, ehr, des, or aes encryption.");
                if (cipher != 0 && config.GetKeyId() is 0 or > 63)
                    throw new InvalidOperationException("NXDN key IDs must be 1-63 (keyId is hexadecimal).");
                if (cipher != 0 && (channel?.IsTxEncrypted ?? true) && !HasNxdnKey(config))
                    throw new InvalidOperationException("NXDN encryption key is unavailable. Load the key before transmitting.");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"NXDN TX blocked on {config.Name}: {ex.Message}");
                if (showWarning)
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show(ex.Message, "NXDN Transmit Unavailable",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
                return false;
            }
        }

        private void NXDNEncodeAudioFrame(byte[] pcm, PeerSystem fne, ChannelBox channel,
            Codeplug.Channel config, Codeplug.System system, uint? sourceIdOverride = null, uint? expectedStreamId = null)
        {
            lock (channel.NxdnSync)
            {
                uint stream = channel.TxStreamId;
                if (stream == 0 || channel.NxdnFailedStreamId == stream ||
                    (expectedStreamId.HasValue && stream != expectedStreamId.Value) || !channel.NxdnEndTask.IsCompleted)
                    return;
                try
                {
                    if (pcm.Length != PCM_SAMPLES_LENGTH)
                        throw new ArgumentException("NXDN requires 20 ms PCM chunks.");
                    if (channel.NxdnTx == null)
                    {
                        uint source = sourceIdOverride ?? uint.Parse(config.GetSourceRid(system));
                        byte[] key = null;
                        NXDNCallData call = new()
                        {
                            SrcId = source, DstId = uint.Parse(config.Tgid), TxStreamID = stream,
                            Ran = checked((byte)config.Ran)
                        };
                        try
                        {
                            if (channel.IsTxEncrypted)
                            {
                                byte cipher = config.GetNxdnCipherType();
                                key = GetNxdnKey(system.Name, cipher, config.GetKeyId()) ??
                                    throw new InvalidOperationException("NXDN key unavailable; refusing clear fallback.");
                                call.AlgorithmId = cipher;
                                call.KeyId = config.GetKeyId();
                                call.Crypto.SetKey(call.KeyId, cipher, key);
                            }
                            channel.NxdnTx = new NxdnTxCall(fne, call,
                                samples => EncodeNxdnCodeword(channel, samples),
                                (packet, sequence) =>
                                {
                                    if (IsFneSystemConnected(system.Name))
                                        fne.SendNXDNFrame(call, packet, sequence);
                                });
                        }
                        catch { call.Dispose(); throw; }
                        finally
                        {
                            if (key != null)
                                CryptographicOperations.ZeroMemory(key);
                        }
                    }
                    short[] samples = new short[160];
                    Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
                    // NXDN calls stay ordered in the capture callback instead of racing Task.Run chunks.
                    channel.NxdnTx.Process(samples);
                    Dispatcher.BeginInvoke(new Action(() => UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.ConsoleTx)));
                }
                catch (Exception ex)
                {
                    Log.WriteError($"({system.Name}) NXDN TX stopped: {ex.Message}");
                    channel.NxdnFailedStreamId = stream;
                    EndNxdnTransmission(channel, discardPending: true);
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

        private byte[] EncodeNxdnCodeword(ChannelBox channel, short[] samples)
        {
            if (channel.ExternalVocoderEnabled)
            {
                channel.ExtHalfRateVocoder ??= new AmbeVocoder(false);
                channel.ExtHalfRateVocoder.EncoderAgcEnabled = IsAudioInputAgcEnabled();
                channel.ExtHalfRateVocoder.encode(samples, out byte[] encoded, true);
                return encoded;
            }
            channel.Encoder ??= new MBEEncoder(MBE_MODE.DMR_AMBE);
            byte[] ambe = new byte[9];
            channel.Encoder.encode(samples, ambe);
            return ambe;
        }

        private void EndNxdnTransmission(ChannelBox channel, bool discardPending = false)
        {
            lock (channel.NxdnSync)
            {
                NxdnTxCall call = channel.NxdnTx;
                if (call == null)
                    return;
                channel.NxdnTx = null;
                try
                {
                    if (discardPending)
                        call.Abort();
                    else
                        call.End();
                }
                catch (Exception ex) { Log.WriteWarning($"NXDN release: {ex.Message}"); }
                channel.NxdnEndTask = FinishNxdnTransmissionAsync(call);
            }
        }

        private static async Task FinishNxdnTransmissionAsync(NxdnTxCall call)
        {
            try { await call.Completion.ConfigureAwait(false); }
            catch (Exception ex) { Log.WriteWarning($"NXDN transmit worker: {ex.Message}"); }
            finally { call.Dispose(); }
        }

        private void StopNxdnChannels()
        {
            foreach (ChannelBox channel in GetAllCanvases().SelectMany(canvas => canvas.Children.OfType<ChannelBox>()))
            {
                Codeplug.Channel config = Codeplug?.GetChannelByName(channel.ChannelName);
                if (config?.GetChannelMode() != Codeplug.ChannelMode.NXDN)
                    continue;
                Codeplug.System system = Codeplug.GetSystemForChannel(config);
                SlotStatus status = FindActiveReceiveStatus(channel, config);
                EndTarRxRecordingFromChannelState(system, config, channel, status, DateTime.Now);
                EndTarTxRecording(channel, system, config);
                EndNxdnTransmission(channel, discardPending: true);
                ClearReceiveState(channel, status);
                ResetChannel(channel);
            }
        }

        public void NXDNDataReceived(string sourceSystemName, NXDNDataReceivedEvent e, DateTime packetTime)
        {
            if (e.CallType != CallType.GROUP || e.StreamId == 0 || e.SrcId is 0 or > ushort.MaxValue ||
                e.DstId is 0 or > ushort.MaxValue || !NXDNFrame.TryExtractFrame(e.Data, stackalloc byte[48]))
                return;
            Dispatcher.Invoke(() =>
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels().ToList())
                {
                    Codeplug.Channel config = Codeplug.GetChannelByName(channel.ChannelName);
                    Codeplug.System system = config == null ? null : Codeplug.GetSystemForChannel(config);
                    if (system == null || config.GetChannelMode() != Codeplug.ChannelMode.NXDN ||
                        !ResourceIdentity.SystemMatches(system.Name, sourceSystemName) || config.Tgid != e.DstId.ToString() ||
                        !channel.IsEnabled || channel.PttState || channel.TxStreamId != 0 ||
                        patchManager.IsPatchedTransmitStream(system.Name, config.Tgid, e.StreamId))
                        continue;
                    bool duplicate = selectedChannelsManager.GetSelectedChannels().Any(other => other.InternalID != channel.InternalID &&
                        ChannelMatchesResource(other, system.Name, config.Tgid) &&
                        (other.RxStreamId == e.StreamId || other.TxStreamId == e.StreamId));
                    if (duplicate || (channel.RxStreamId == e.StreamId && channel.PeerId != e.PeerId))
                        continue;
                    string statusKey = ResourceIdentity.Build(system.Name, config.Tgid) + "|nxdn";
                    if (!systemStatuses.TryGetValue(statusKey, out SlotStatus status))
                        systemStatuses[statusKey] = status = new SlotStatus();
                    bool newCall = channel.NxdnRx == null || channel.NxdnRx.TxStreamID != e.StreamId;
                    if (newCall && e.FrameType == FrameType.TERMINATOR)
                        continue;
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
                            channel.NxdnRx?.Dispose();
                            channel.NxdnRx = new NXDNCallData { SrcId = e.SrcId, DstId = e.DstId, TxStreamID = e.StreamId };
                            channel.NxdnRxCodec ??= new NxdnAmbeCodec();
                            channel.IsReceiving = true;
                            channel.PeerId = e.PeerId;
                            channel.RxStreamId = e.StreamId;
                            status.RxStart = packetTime;
                            patchManager.HandleCallStart(system.Name, config.Tgid, e.StreamId, e.SrcId);
                        }
                        NXDNCallData call = channel.NxdnRx;
                        byte[] voice = new byte[NXDNFrame.VoiceBytes];
                        int voiceLength = NXDNFrame.Decode(call, e.Data, e.PacketSequence, voice, channel.NxdnRxCodec,
                            (cipher, id) => GetNxdnKey(system.Name, cipher, id));
                        channel.LastPktTime = packetTime;
                        channel.IsReceivingEncrypted = call.IsEncrypted;
                        channel.LastSrcId = string.IsNullOrEmpty(alias) ? "Last ID: " + e.SrcId : "Last: " + alias;
                        status.RxRFS = e.SrcId;
                        status.RxTGId = e.DstId;
                        status.RxStreamId = e.StreamId;
                        status.RxTime = packetTime;
                        status.RxType = e.FrameType;
                        if (newCall)
                        {
                            var recording = BeginTarRxRecording(system, config, e.StreamId, e.SrcId, alias,
                                call.IsEncrypted, DescribeNxdnEncryptionAlgorithm(call.AlgorithmId), call.KeyId == 0 ? null : call.KeyId, packetTime);
                            callHistoryWindow.AddCall(config.Name, (int)e.SrcId, (int)e.DstId, alias, packetTime.ToString("HH:mm:ss"), recording);
                            channel.AddCall(config.Name, (int)e.SrcId, (int)e.DstId, alias, packetTime.ToString("HH:mm:ss"), recording);
                            Log.WriteLine($"({system.Name}) NXDD: Traffic *CALL START     * SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");
                        }
                        if (call.IsReleased)
                        {
                            EndTarRxRecordingFromChannelState(system, config, channel, status, packetTime);
                            patchManager.HandleCallEnd(system.Name, config.Tgid, e.StreamId);
                            ClearReceiveState(channel, status);
                            audioManager.ReleaseTalkgroupStream(channel.AudioOutputKey);
                            callHistoryWindow.ChannelUnkeyed(config.Name, (int)e.SrcId);
                            Log.WriteLine($"({system.Name}) NXDD: Traffic *CALL END       * SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");
                        }
                        else
                        {
                            callHistoryWindow.ChannelKeyed(config.Name, (int)e.SrcId, call.IsEncrypted);
                            for (int offset = 0; offset < voiceLength; offset += 9)
                            {
                                byte[] word = voice.AsSpan(offset, 9).ToArray();
                                short[] samples = new short[160];
                                if (channel.ExternalVocoderEnabled)
                                {
                                    channel.ExtHalfRateVocoder ??= new AmbeVocoder(false);
                                    channel.ExtHalfRateVocoder.decode(word, out samples);
                                }
                                else
                                {
                                    channel.Decoder ??= new MBEDecoder(MBE_MODE.DMR_AMBE);
                                    channel.Decoder.decode(word, samples);
                                }
                                byte[] pcm = new byte[samples.Length * 2];
                                Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                                UpdateVolumeMeterFromSamples(channel, samples, VolumeMeterSource.RadioRx);
                                if (!ShouldMuteRxPlayback())
                                    audioManager.AddTalkgroupStream(channel.AudioOutputKey, pcm);
                                AppendTarRxAudio(system.Name, config.Tgid, e.StreamId, pcm);
                                patchManager.HandleAudio(system.Name, config.Tgid, e.StreamId, e.SrcId, pcm);
                            }
                        }
                        UpdateTabAudioIndicatorForChannel(channel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarning($"({system.Name}) NXDN RX ignored: {ex.Message}");
                    }
                }
            });
        }

        private static string DescribeNxdnEncryptionAlgorithm(byte cipher) => cipher switch
        {
            0 => string.Empty, 1 => "EHR", 2 => "DES-OFB", 3 => "AES-256", _ => $"Unknown NXDN cipher {cipher}"
        };
    }
}
