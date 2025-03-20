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
*
*/

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
        private SettingsManager settingsManager;

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
        }

        /// <summary>
        /// Bad name, adds samples to a provider or creates a new provider
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="audioData"></param>
        public void AddTalkgroupStream(string talkgroupId, byte[] audioData)
        {
            if (!talkgroupProviders.ContainsKey(talkgroupId))
                AddTalkgroupStream(talkgroupId);

            talkgroupProviders[talkgroupId].buffer.AddSamples(audioData, 0, audioData.Length);
        }

        /// <summary>
        /// Internal helper to create a talkgroup stream
        /// </summary>
        /// <param name="talkgroupId"></param>
        private void AddTalkgroupStream(string talkgroupId)
        {
            int deviceIndex = settingsManager.ChannelOutputDevices.ContainsKey(talkgroupId) ? settingsManager.ChannelOutputDevices[talkgroupId] : 0;

            var waveOut = new WaveOutEvent { DeviceNumber = deviceIndex };
            var bufferProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1)) { DiscardOnBufferOverflow = true };
            var gainProvider = new GainSampleProvider(bufferProvider.ToSampleProvider()) { Gain = 1.0f };
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(8000, 1)) { ReadFully = true };

            mixer.AddMixerInput(gainProvider);

            waveOut.Init(mixer);
            waveOut.Play();

            talkgroupProviders[talkgroupId] = (waveOut, mixer, bufferProvider, gainProvider);
        }

        /// <summary>
        /// Adjusts the volume of a specific talkgroup stream
        /// </summary>
        public void SetTalkgroupVolume(string talkgroupId, float volume)
        {
            if (talkgroupProviders.ContainsKey(talkgroupId))
                talkgroupProviders[talkgroupId].gainProvider.Gain = volume;
            else
            {
                AddTalkgroupStream(talkgroupId);
                talkgroupProviders[talkgroupId].gainProvider.Gain = volume;
            }
        }

        /// <summary>
        /// Set stream output device
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="deviceIndex"></param>
        public void SetTalkgroupOutputDevice(string talkgroupId, int deviceIndex)
        {
            if (talkgroupProviders.ContainsKey(talkgroupId))
            {
                talkgroupProviders[talkgroupId].waveOut.Stop();
                talkgroupProviders.Remove(talkgroupId);
            }

            settingsManager.UpdateChannelOutputDevice(talkgroupId, deviceIndex);
            AddTalkgroupStream(talkgroupId);
        }

        /// <summary>
        /// Lop off the wave out
        /// </summary>
        public void Stop()
        {
            foreach (var provider in talkgroupProviders.Values)
                provider.waveOut.Stop();
        }
    } // public class AudioManager
} // namespace dvmconsole
