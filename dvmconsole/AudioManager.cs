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
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace dvmconsole
{
    /// <summary>
    /// Class for managing audio streams.
    /// </summary>
    public class AudioManager
    {
        private Dictionary<string, (IWavePlayer player, BufferedWaveProvider buffer, GainSampleProvider gainProvider, AudioDeviceResolver.AudioDeviceSelection deviceSelection)> talkgroupProviders;
        private readonly Dictionary<string, float> talkgroupVolumes;
        private readonly Dictionary<string, DateTime> talkgroupLastAudioTimes;
        private readonly List<IWavePlayer> oneShotPlayers;
        private readonly Dictionary<string, List<IWavePlayer>> oneShotPlayersByTalkgroup;
        private SettingsManager settingsManager;
        private readonly object talkgroupProvidersSync = new object();
        private static readonly TimeSpan DefaultTalkgroupReleaseDelay = TimeSpan.FromSeconds(2);
        private const int WasapiSharedModeOutputLatencyMilliseconds = 200;

        /*
        ** Methods
        */

        /// <summary>
        /// Creates an instance of <see cref="AudioManager"/> class.
        /// </summary>
        public AudioManager(SettingsManager settingsManager)
        {
            this.settingsManager = settingsManager;
            talkgroupProviders = new Dictionary<string, (IWavePlayer, BufferedWaveProvider, GainSampleProvider, AudioDeviceResolver.AudioDeviceSelection)>();
            talkgroupVolumes = new Dictionary<string, float>();
            talkgroupLastAudioTimes = new Dictionary<string, DateTime>();
            oneShotPlayers = new List<IWavePlayer>();
            oneShotPlayersByTalkgroup = new Dictionary<string, List<IWavePlayer>>(StringComparer.OrdinalIgnoreCase);
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
                talkgroupLastAudioTimes[talkgroupId] = DateTime.UtcNow;
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
                talkgroupLastAudioTimes[talkgroupId] = DateTime.UtcNow;
                if (provider.buffer.BufferedDuration > maxBufferedDuration)
                    provider.buffer.ClearBuffer();

                provider.buffer.AddSamples(audioData, 0, audioData.Length);
            }
        }

        /// <summary>
        /// Plays a one-shot PCM clip without reusing the long-lived talkgroup playback provider.
        /// </summary>
        public void PlayOneShot(string talkgroupId, byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            AudioDeviceResolver.AudioDeviceSelection deviceSelection = ResolveTalkgroupOutputDevice(talkgroupId);

            Task.Run(() =>
            {
                IWavePlayer player = null;
                IWaveProvider playbackProvider = null;
                RawSourceWaveStream rawStream = null;
                MemoryStream memoryStream = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    memoryStream = new MemoryStream(audioData, writable: false);
                    rawStream = new RawSourceWaveStream(memoryStream, new WaveFormat(8000, 16, 1));
                    player = CreateOutputPlayer(deviceSelection);
                    playbackProvider = CreateOutputProvider(rawStream.ToSampleProvider(), deviceSelection);

                    RegisterOneShotPlayer(talkgroupId, player);

                    player.Init(playbackProvider);
                    player.Play();

                    while (player.PlaybackState == PlaybackState.Playing && !cancellationToken.IsCancellationRequested)
                        Thread.Sleep(25);
                }
                catch (Exception ex) when (deviceSelection.Backend == AudioBackendKind.Wasapi)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    Log.WriteWarning($"WASAPI one-shot playback failed for {talkgroupId}; falling back to legacy MME. {ex.Message}");
                    int legacyFallbackDeviceNumber = deviceSelection.DeviceNumber;
                    CleanupOneShotPlayer(talkgroupId, player, deviceSelection);
                    player = null;
                    deviceSelection = null;
                    rawStream?.Dispose();
                    memoryStream?.Dispose();

                    PlayOneShotWithMmeFallback(talkgroupId, audioData, legacyFallbackDeviceNumber, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the normal path when an operator stops an active tone.
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"Failed to play local one-shot audio for {talkgroupId}: {ex.Message}");
                }
                finally
                {
                    CleanupOneShotPlayer(talkgroupId, player, deviceSelection);

                    rawStream?.Dispose();
                    memoryStream?.Dispose();
                }
            });
        }

        private void PlayOneShotWithMmeFallback(string talkgroupId, byte[] audioData, int legacyDeviceNumber, CancellationToken cancellationToken)
        {
            AudioDeviceResolver.AudioDeviceSelection fallbackSelection = AudioDeviceResolver.ResolveMmeOutputFallback(legacyDeviceNumber);
            IWavePlayer player = null;
            RawSourceWaveStream rawStream = null;
            MemoryStream memoryStream = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                memoryStream = new MemoryStream(audioData, writable: false);
                rawStream = new RawSourceWaveStream(memoryStream, new WaveFormat(8000, 16, 1));
                player = CreateOutputPlayer(fallbackSelection);

                RegisterOneShotPlayer(talkgroupId, player);

                player.Init(rawStream);
                player.Play();

                while (player.PlaybackState == PlaybackState.Playing && !cancellationToken.IsCancellationRequested)
                    Thread.Sleep(25);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal path when an operator stops an active tone.
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Legacy MME one-shot playback failed for {talkgroupId}: {ex.Message}");
            }
            finally
            {
                CleanupOneShotPlayer(talkgroupId, player, fallbackSelection);
                rawStream?.Dispose();
                memoryStream?.Dispose();
            }
        }

        private void RegisterOneShotPlayer(string talkgroupId, IWavePlayer player)
        {
            if (player == null)
                return;

            string key = talkgroupId ?? string.Empty;
            lock (talkgroupProvidersSync)
            {
                oneShotPlayers.Add(player);

                if (!oneShotPlayersByTalkgroup.TryGetValue(key, out List<IWavePlayer> players))
                {
                    players = new List<IWavePlayer>();
                    oneShotPlayersByTalkgroup[key] = players;
                }

                players.Add(player);
            }
        }

        private void CleanupOneShotPlayer(string talkgroupId, IWavePlayer player, AudioDeviceResolver.AudioDeviceSelection deviceSelection)
        {
            if (player != null)
            {
                player.Stop();
                player.Dispose();

                lock (talkgroupProvidersSync)
                {
                    oneShotPlayers.Remove(player);

                    string key = talkgroupId ?? string.Empty;
                    if (oneShotPlayersByTalkgroup.TryGetValue(key, out List<IWavePlayer> players))
                    {
                        players.Remove(player);
                        if (players.Count == 0)
                            oneShotPlayersByTalkgroup.Remove(key);
                    }
                }
            }

            DisposeDeviceSelection(deviceSelection);
        }

        /// <summary>
        /// Stops queued local one-shot playback for a specific output key.
        /// </summary>
        public void StopOneShot(string talkgroupId)
        {
            if (string.IsNullOrWhiteSpace(talkgroupId))
                return;

            List<IWavePlayer> players;
            lock (talkgroupProvidersSync)
            {
                if (!oneShotPlayersByTalkgroup.TryGetValue(talkgroupId, out List<IWavePlayer> activePlayers))
                    return;

                players = activePlayers.ToList();
            }

            foreach (IWavePlayer player in players)
                player.Stop();
        }

        /// <summary>
        /// Internal helper to create a talkgroup stream
        /// </summary>
        /// <param name="talkgroupId"></param>
        private void AddTalkgroupStream(string talkgroupId)
        {
            AudioDeviceResolver.AudioDeviceSelection deviceSelection = ResolveTalkgroupOutputDevice(talkgroupId);

            var bufferProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1), TimeSpan.FromSeconds(10))
            {
                DiscardOnBufferOverflow = true
            };
            var gainProvider = new GainSampleProvider(bufferProvider.ToSampleProvider()) { Gain = ResolveTalkgroupVolume(talkgroupId) };
            IWavePlayer player = null;

            try
            {
                player = CreateOutputPlayer(deviceSelection);
                player.Init(CreateOutputProvider(gainProvider, deviceSelection));
                player.Play();
            }
            catch (Exception ex) when (deviceSelection.Backend == AudioBackendKind.Wasapi)
            {
                Log.WriteWarning($"WASAPI playback failed for {talkgroupId}; falling back to legacy MME. {ex.Message}");
                player?.Dispose();
                DisposeDeviceSelection(deviceSelection);
                deviceSelection = AudioDeviceResolver.ResolveMmeOutputFallback(deviceSelection.DeviceNumber);
                player = CreateOutputPlayer(deviceSelection);
                player.Init(CreateOutputProvider(gainProvider, deviceSelection));
                player.Play();
            }
            catch
            {
                player?.Dispose();
                throw;
            }

            talkgroupProviders[talkgroupId] = (player, bufferProvider, gainProvider, deviceSelection);
        }

        private (IWavePlayer player, BufferedWaveProvider buffer, GainSampleProvider gainProvider, AudioDeviceResolver.AudioDeviceSelection deviceSelection) GetOrCreateTalkgroupProvider(string talkgroupId)
        {
            if (!talkgroupProviders.ContainsKey(talkgroupId))
                AddTalkgroupStream(talkgroupId);
            else if (talkgroupProviders[talkgroupId].player.PlaybackState != PlaybackState.Playing)
            {
                RemoveTalkgroupProvider(talkgroupId);
                AddTalkgroupStream(talkgroupId);
            }

            return talkgroupProviders[talkgroupId];
        }

        /// <summary>
        /// Adjusts the volume of a specific talkgroup stream
        /// </summary>
        public void SetTalkgroupVolume(string talkgroupId, float volume)
        {
            lock (talkgroupProvidersSync)
            {
                talkgroupVolumes[talkgroupId] = volume;
                if (talkgroupProviders.TryGetValue(talkgroupId, out var provider))
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
        /// Clears queued local playback audio without tearing down output devices.
        /// </summary>
        public void ClearAllTalkgroupBuffers()
        {
            lock (talkgroupProvidersSync)
            {
                foreach (var provider in talkgroupProviders.Values)
                    provider.buffer.ClearBuffer();
            }
        }

        /// <summary>
        /// Set stream output device
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="deviceIndex"></param>
        public void SetTalkgroupOutputDevice(string talkgroupId, int deviceIndex, string deviceKey = null)
        {
            lock (talkgroupProvidersSync)
            {
                bool wasActive = talkgroupProviders.ContainsKey(talkgroupId);
                RemoveTalkgroupProvider(talkgroupId);

                settingsManager.UpdateChannelOutputDevice(talkgroupId, deviceIndex, deviceKey);
                if (wasActive)
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

        public void SetMasterOutputDevice(int deviceIndex, string deviceKey = null)
        {
            lock (talkgroupProvidersSync)
            {
                settingsManager.UpdateMasterOutputDevice(deviceIndex, deviceKey);
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

        private AudioDeviceResolver.AudioDeviceSelection ResolveTalkgroupOutputDevice(string talkgroupId)
        {
            if (!string.IsNullOrWhiteSpace(talkgroupId) &&
                settingsManager.ChannelOutputDeviceKeys.TryGetValue(talkgroupId, out string overrideDeviceKey))
            {
                settingsManager.ChannelOutputDevices.TryGetValue(talkgroupId, out int legacyOverrideDevice);
                return AudioDeviceResolver.ResolveOutputDevice(overrideDeviceKey, legacyOverrideDevice);
            }

            if (!string.IsNullOrWhiteSpace(talkgroupId) &&
                settingsManager.ChannelOutputDevices.TryGetValue(talkgroupId, out int legacyOnlyOverrideDevice))
                return AudioDeviceResolver.ResolveOutputDevice(null, legacyOnlyOverrideDevice);

            return AudioDeviceResolver.ResolveOutputDevice(settingsManager.MasterOutputDeviceKey, settingsManager.MasterOutputDevice);
        }

        private static IWavePlayer CreateOutputPlayer(AudioDeviceResolver.AudioDeviceSelection selection)
        {
            if (selection?.Backend == AudioBackendKind.Wasapi && selection.WasapiDevice != null)
            {
                return new WasapiPlayerBuilder()
                    .WithDevice(selection.WasapiDevice)
                    .WithSharedMode()
                    .WithPollingSync()
                    .WithLatency(WasapiSharedModeOutputLatencyMilliseconds)
                    .Build();
            }

            return new WaveOut { DeviceNumber = selection?.DeviceNumber ?? SettingsManager.WINDOWS_DEFAULT_AUDIO_DEVICE };
        }

        private static IWaveProvider CreateOutputProvider(ISampleProvider sourceProvider, AudioDeviceResolver.AudioDeviceSelection selection)
        {
            if (selection?.Backend != AudioBackendKind.Wasapi || selection.WasapiDevice == null)
                return new SampleToWaveProvider(sourceProvider);

            using AudioClient audioClient = selection.WasapiDevice.CreateAudioClient();
            WaveFormat mixFormat = audioClient.MixFormat;
            ISampleProvider outputProvider = sourceProvider;

            if (outputProvider.WaveFormat.SampleRate != mixFormat.SampleRate)
                outputProvider = new WdlResamplingSampleProvider(outputProvider, mixFormat.SampleRate);

            if (outputProvider.WaveFormat.Channels != mixFormat.Channels)
                outputProvider = new MonoToMultiChannelSampleProvider(outputProvider, mixFormat.Channels);

            return new SampleToWaveProvider(outputProvider);
        }

        private float ResolveTalkgroupVolume(string talkgroupId)
        {
            if (!string.IsNullOrWhiteSpace(talkgroupId) &&
                talkgroupVolumes.TryGetValue(talkgroupId, out float volume))
            {
                return volume;
            }

            return 1.0f;
        }

        public void ReleaseTalkgroupStream(string talkgroupId, TimeSpan? releaseDelay = null)
        {
            if (string.IsNullOrWhiteSpace(talkgroupId))
                return;

            DateTime observedLastAudio;
            lock (talkgroupProvidersSync)
            {
                if (!talkgroupProviders.ContainsKey(talkgroupId))
                    return;

                observedLastAudio = talkgroupLastAudioTimes.TryGetValue(talkgroupId, out DateTime lastAudio)
                    ? lastAudio
                    : DateTime.UtcNow;
            }

            Task.Run(async () =>
            {
                TimeSpan delay = releaseDelay ?? DefaultTalkgroupReleaseDelay;
                await Task.Delay(delay).ConfigureAwait(false);

                for (int attempt = 0; attempt < 8; attempt++)
                {
                    lock (talkgroupProvidersSync)
                    {
                        if (!talkgroupProviders.TryGetValue(talkgroupId, out var provider))
                            return;

                        if (talkgroupLastAudioTimes.TryGetValue(talkgroupId, out DateTime lastAudio) &&
                            lastAudio > observedLastAudio)
                            return;

                        if (provider.buffer.BufferedBytes == 0)
                        {
                            RemoveTalkgroupProvider(talkgroupId);
                            return;
                        }
                    }

                    await Task.Delay(250).ConfigureAwait(false);
                }

                lock (talkgroupProvidersSync)
                {
                    if (talkgroupLastAudioTimes.TryGetValue(talkgroupId, out DateTime lastAudio) &&
                        lastAudio > observedLastAudio)
                        return;

                    RemoveTalkgroupProvider(talkgroupId);
                }
            });
        }

        public void StopTalkgroupStream(string talkgroupId)
        {
            if (string.IsNullOrWhiteSpace(talkgroupId))
                return;

            lock (talkgroupProvidersSync)
            {
                RemoveTalkgroupProvider(talkgroupId);
            }
        }

        private void RemoveTalkgroupProvider(string talkgroupId)
        {
            if (!talkgroupProviders.TryGetValue(talkgroupId, out var provider))
                return;

            provider.buffer.ClearBuffer();
            provider.player.Stop();
            provider.player.Dispose();
            DisposeDeviceSelection(provider.deviceSelection);
            talkgroupProviders.Remove(talkgroupId);
            talkgroupLastAudioTimes.Remove(talkgroupId);
        }

        private static void DisposeDeviceSelection(AudioDeviceResolver.AudioDeviceSelection deviceSelection)
        {
            deviceSelection?.WasapiDevice?.Dispose();
        }

        /// <summary>
        /// Lop off the wave out
        /// </summary>
        public void Stop()
        {
            lock (talkgroupProvidersSync)
            {
                foreach (var provider in talkgroupProviders.Values)
                    provider.player.Stop();

                foreach (IWavePlayer player in oneShotPlayers.ToList())
                    player.Stop();
            }
        }
    } // public class AudioManager
} // namespace dvmconsole
