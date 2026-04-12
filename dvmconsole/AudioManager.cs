// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace dvmconsole
{
    /// <summary>
    /// Class for managing audio streams.
    /// </summary>
    public class AudioManager
    {
        private Dictionary<string, (WaveOutEvent waveOut, MixingSampleProvider mixer, BufferedWaveProvider buffer, GainSampleProvider gainProvider)> talkgroupProviders;
        private readonly List<WaveOutEvent> oneShotPlayers;
        private SettingsManager settingsManager;
        private readonly object talkgroupProvidersSync = new object();

        /*
        ** Methods
        */

        /// <summary>
        /// Creates an instance of <see cref="AudioManager"/> class.
        /// </summary>
        public AudioManager(SettingsManager settingsManager)
        {
            this.settingsManager = settingsManager;
            talkgroupProviders = new Dictionary<string, (WaveOutEvent, MixingSampleProvider, BufferedWaveProvider, GainSampleProvider)>();
            oneShotPlayers = new List<WaveOutEvent>();
        }

        /// <summary>
        /// Bad name, adds samples to a provider or creates a new provider
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="audioData"></param>
        public void AddTalkgroupStream(string talkgroupId, byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            lock (talkgroupProvidersSync)
            {
                var provider = GetOrCreateTalkgroupProvider(talkgroupId);
                provider.buffer.AddSamples(audioData, 0, audioData.Length);
            }
        }

        /// <summary>
        /// Adds live monitor audio while shedding stale backlog to keep playback current.
        /// </summary>
        public void AddLiveMonitorStream(string talkgroupId, byte[] audioData, TimeSpan maxBufferedDuration)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            lock (talkgroupProvidersSync)
            {
                var provider = GetOrCreateTalkgroupProvider(talkgroupId);
                if (provider.buffer.BufferedDuration > maxBufferedDuration)
                    provider.buffer.ClearBuffer();

                provider.buffer.AddSamples(audioData, 0, audioData.Length);
            }
        }

        /// <summary>
        /// Plays a one-shot PCM clip without reusing the long-lived talkgroup playback provider.
        /// </summary>
        public void PlayOneShot(string talkgroupId, byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            int deviceIndex = ResolveTalkgroupOutputDevice(talkgroupId);

            Task.Run(() =>
            {
                WaveOutEvent waveOut = null;
                RawSourceWaveStream rawStream = null;
                MemoryStream memoryStream = null;

                try
                {
                    memoryStream = new MemoryStream(audioData, writable: false);
                    rawStream = new RawSourceWaveStream(memoryStream, new WaveFormat(8000, 16, 1));
                    waveOut = new WaveOutEvent { DeviceNumber = deviceIndex };

                    lock (talkgroupProvidersSync)
                        oneShotPlayers.Add(waveOut);

                    waveOut.Init(rawStream);
                    waveOut.Play();

                    while (waveOut.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(25);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"Failed to play local one-shot audio for {talkgroupId}: {ex.Message}");
                }
                finally
                {
                    if (waveOut != null)
                    {
                        waveOut.Stop();
                        waveOut.Dispose();

                        lock (talkgroupProvidersSync)
                            oneShotPlayers.Remove(waveOut);
                    }

                    rawStream?.Dispose();
                    memoryStream?.Dispose();
                }
            });
        }

        /// <summary>
        /// Internal helper to create a talkgroup stream
        /// </summary>
        /// <param name="talkgroupId"></param>
        private void AddTalkgroupStream(string talkgroupId)
        {
            int deviceIndex = ResolveTalkgroupOutputDevice(talkgroupId);

            var waveOut = new WaveOutEvent { DeviceNumber = deviceIndex };
            var bufferProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1))
            {
                BufferDuration = TimeSpan.FromSeconds(10),
                DiscardOnBufferOverflow = true
            };
            var gainProvider = new GainSampleProvider(bufferProvider.ToSampleProvider()) { Gain = 1.0f };
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(8000, 1)) { ReadFully = true };

            mixer.AddMixerInput(gainProvider);

            waveOut.Init(mixer);
            waveOut.Play();

            talkgroupProviders[talkgroupId] = (waveOut, mixer, bufferProvider, gainProvider);
        }

        private (WaveOutEvent waveOut, MixingSampleProvider mixer, BufferedWaveProvider buffer, GainSampleProvider gainProvider) GetOrCreateTalkgroupProvider(string talkgroupId)
        {
            if (!talkgroupProviders.ContainsKey(talkgroupId))
                AddTalkgroupStream(talkgroupId);

            return talkgroupProviders[talkgroupId];
        }

        /// <summary>
        /// Adjusts the volume of a specific talkgroup stream
        /// </summary>
        public void SetTalkgroupVolume(string talkgroupId, float volume)
        {
            lock (talkgroupProvidersSync)
            {
                var provider = GetOrCreateTalkgroupProvider(talkgroupId);
                provider.gainProvider.Gain = volume;
            }
        }

        /// <summary>
        /// Clears any buffered audio for a talkgroup without removing its provider.
        /// </summary>
        public void ClearTalkgroupBuffer(string talkgroupId)
        {
            if (string.IsNullOrWhiteSpace(talkgroupId))
                return;

            lock (talkgroupProvidersSync)
            {
                if (talkgroupProviders.TryGetValue(talkgroupId, out var provider))
                    provider.buffer.ClearBuffer();
            }
        }

        /// <summary>
        /// Set stream output device
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="deviceIndex"></param>
        public void SetTalkgroupOutputDevice(string talkgroupId, int deviceIndex)
        {
            lock (talkgroupProvidersSync)
            {
                RemoveTalkgroupProvider(talkgroupId);

                settingsManager.UpdateChannelOutputDevice(talkgroupId, deviceIndex);
                AddTalkgroupStream(talkgroupId);
            }
        }

        public void ClearTalkgroupOutputDevice(string talkgroupId)
        {
            lock (talkgroupProvidersSync)
            {
                bool wasActive = talkgroupProviders.ContainsKey(talkgroupId);
                RemoveTalkgroupProvider(talkgroupId);
                settingsManager.RemoveChannelOutputDevice(talkgroupId);
                if (wasActive)
                    AddTalkgroupStream(talkgroupId);
            }
        }

        public void SetMasterOutputDevice(int deviceIndex)
        {
            lock (talkgroupProvidersSync)
            {
                settingsManager.UpdateMasterOutputDevice(deviceIndex);
                ReloadOutputDevices();
            }
        }

        public void ReloadOutputDevices()
        {
            lock (talkgroupProvidersSync)
            {
                List<string> activeTalkgroups = talkgroupProviders.Keys.ToList();
                foreach (string talkgroupId in activeTalkgroups)
                    RemoveTalkgroupProvider(talkgroupId);

                foreach (string talkgroupId in activeTalkgroups)
                    AddTalkgroupStream(talkgroupId);
            }
        }

        private int ResolveTalkgroupOutputDevice(string talkgroupId)
        {
            int deviceIndex;
            if (!string.IsNullOrWhiteSpace(talkgroupId) &&
                settingsManager.ChannelOutputDevices.TryGetValue(talkgroupId, out int overrideDevice))
                deviceIndex = SettingsManager.NormalizeAudioDeviceIndex(overrideDevice);
            else
                deviceIndex = SettingsManager.NormalizeAudioDeviceIndex(settingsManager.MasterOutputDevice);

            return deviceIndex >= WaveOut.DeviceCount ? SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE : deviceIndex;
        }

        private void RemoveTalkgroupProvider(string talkgroupId)
        {
            if (!talkgroupProviders.TryGetValue(talkgroupId, out var provider))
                return;

            provider.waveOut.Stop();
            provider.waveOut.Dispose();
            talkgroupProviders.Remove(talkgroupId);
        }

        /// <summary>
        /// Lop off the wave out
        /// </summary>
        public void Stop()
        {
            lock (talkgroupProvidersSync)
            {
                foreach (var provider in talkgroupProviders.Values)
                    provider.waveOut.Stop();

                foreach (WaveOutEvent player in oneShotPlayers.ToList())
                    player.Stop();
            }
        }
    } // public class AudioManager
} // namespace dvmconsole
