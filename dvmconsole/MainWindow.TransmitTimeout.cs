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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using dvmconsole.Controls;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private const string TIMEOUT_AUDIO_KEY = "__operator_ptt_timeout__";
        private readonly ConcurrentDictionary<ChannelBox, OperatorPttTimeout> operatorPttTimeouts = new();
        private DispatcherTimer transmitTimeoutTimer;
        private long lastTimeoutAlert;
        private long lastTimeoutWarning;

        private void StartOperatorPtt(ChannelBox channel)
        {
            // Repeated input must not extend an existing call's deadline.
            operatorPttTimeouts.TryAdd(channel, new OperatorPttTimeout(channel.TxStreamId,
                Codeplug.TransmitTimeoutSeconds, Stopwatch.GetTimestamp()));
            transmitTimeoutTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Normal, CheckTransmitTimeouts, Dispatcher);
            transmitTimeoutTimer.Start();
        }

        private void StopOperatorPtt(ChannelBox channel)
        {
            if (operatorPttTimeouts.TryRemove(channel, out OperatorPttTimeout session))
                session.Stop();
        }

        private void StopOperatorPttTimers()
        {
            transmitTimeoutTimer?.Stop();
            foreach (ChannelBox channel in operatorPttTimeouts.Keys)
                StopOperatorPtt(channel);
            audioManager?.StopOneShot(TIMEOUT_AUDIO_KEY);
        }

        private bool CanSendOperatorPttAudio(ChannelBox channel, uint stream)
        {
            return stream != 0 && !channel.PttTimedOut &&
                operatorPttTimeouts.TryGetValue(channel, out OperatorPttTimeout session) &&
                session.StreamId == stream && !session.IsExpired(Stopwatch.GetTimestamp());
        }

        private void SendOperatorPttFrame(ChannelBox channel, uint stream, Action send)
        {
            if (operatorPttTimeouts.TryGetValue(channel, out OperatorPttTimeout session) && session.StreamId == stream)
                session.SendFrame(() =>
                {
                    if (channel.IsSelected && channel.TxStreamId == stream && !channel.PttTimedOut)
                        send();
                });
        }

        private void CheckTransmitTimeouts(object sender, EventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            bool warn = false;
            bool expired = false;
            foreach (var entry in operatorPttTimeouts.ToArray())
            {
                ChannelBox channel = entry.Key;
                OperatorPttTimeout session = entry.Value;
                if (channel.TxStreamId != session.StreamId)
                {
                    StopOperatorPtt(channel);
                    continue;
                }
                if (!session.IsExpired(now))
                {
                    warn |= session.TakeWarning(now);
                    continue;
                }

                session.Stop();
                channel.PttTimedOut = true;
                PatchPttTargetSession groupSession;
                lock (patchPttSync)
                    activePatchPttTargets.TryGetValue(BuildPatchTargetKey(channel.ResourceSystemName, channel.DstId), out groupSession);
                bool groupPtt = groupSession != null && groupSession.Channel == channel && groupSession.StreamId == session.StreamId;
                try
                {
                    // Discard queued voice, but retain the normal mode-specific call end.
                    EndAnalogTransmission(channel, discardPending: true);
                    EndDmrTransmission(channel, discardPending: true);
                    EndNxdnTransmission(channel, discardPending: true);
                    if (groupPtt)
                        StopPatchPttSession(groupSession);
                    else if (TryResolveChannelEndpoint(channel, out Codeplug.Channel config, out Codeplug.System system, out PeerSystem fne, out _))
                    {
                        EndTarTxRecording(channel, system, config);
                        if (IsFneSystemConnected(system.Name) && config.GetChannelMode() == Codeplug.ChannelMode.P25)
                            fne.SendP25TDU(uint.Parse(config.GetSourceRid(system)), uint.Parse(config.Tgid), false);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"Unable to complete timeout call-end for {channel.ResourceSystemName}/{channel.DstId}: {ex.Message}");
                }
                finally
                {
                    // Keep the input latched until release, even if the FNE disconnected during call-end.
                    ResetChannel(channel);
                    channel.VolumeMeterLevel = 0;
                    if (groupPtt)
                    {
                        channel.PatchForwardingTxState = false;
                        patchGroupsWindow.SetPttTimedOut(groupSession.GroupName, true);
                    }
                }
                Log.WriteWarning($"({channel.ResourceSystemName}) Operator PTT timed out on TG {channel.DstId} after {Codeplug.TransmitTimeoutSeconds} seconds.");
                expired = true;
            }

            if (expired)
            {
                PlayTransmitTimeoutAlert(true);
                if (operatorPttTimeouts.IsEmpty)
                    SetGlobalPttVisualState(false);
            }
            else if (warn)
                PlayTransmitTimeoutAlert(false);
            if (operatorPttTimeouts.IsEmpty)
                transmitTimeoutTimer?.Stop();
        }

        private void PlayTransmitTimeoutAlert(bool expired)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastTimeoutAlert != 0 && Stopwatch.GetElapsedTime(lastTimeoutAlert, now).TotalSeconds < 1)
                return;
            if (!expired && lastTimeoutWarning != 0 && Stopwatch.GetElapsedTime(lastTimeoutWarning, now).TotalMilliseconds < 750)
                return;
            if (expired)
                lastTimeoutAlert = now;
            else
                lastTimeoutWarning = now;
            try
            {
                // Dedicated local playback key: never pass these samples to a TX encoder or TAR.
                byte[] pcm = new ToneGenerator().GenerateTone(LEGACY_ALERT_TONE_FREQUENCY_HZ,
                    expired ? 1.0 : 0.1, 0.20);
                audioManager.StopOneShot(TIMEOUT_AUDIO_KEY);
                audioManager.PlayOneShot(TIMEOUT_AUDIO_KEY, pcm);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Unable to play local transmit timeout alert: {ex.Message}");
            }
        }
    }
}
