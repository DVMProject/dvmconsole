// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DVMConsole
{
    /// <summary>
    /// Class for managing audio streams
    /// </summary>
    public class AudioManager
    {
        private Dictionary<string, (WaveOutEvent waveOut, MixingSampleProvider mixer, BufferedWaveProvider buffer, GainSampleProvider gainProvider)> _talkgroupProviders;
        private SettingsManager _settingsManager;

        /// <summary>
        /// Creates an instance of <see cref="AudioManager"/>
        /// </summary>
        public AudioManager(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _talkgroupProviders = new Dictionary<string, (WaveOutEvent, MixingSampleProvider, BufferedWaveProvider, GainSampleProvider)>();
        }

        /// <summary>
        /// Bad name, adds samples to a provider or creates a new provider
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="audioData"></param>
        public void AddTalkgroupStream(string talkgroupId, byte[] audioData)
        {
            if (!_talkgroupProviders.ContainsKey(talkgroupId))
                AddTalkgroupStream(talkgroupId);

            _talkgroupProviders[talkgroupId].buffer.AddSamples(audioData, 0, audioData.Length);
        }

        /// <summary>
        /// Internal helper to create a talkgroup stream
        /// </summary>
        /// <param name="talkgroupId"></param>
        private void AddTalkgroupStream(string talkgroupId)
        {
            int deviceIndex = _settingsManager.ChannelOutputDevices.ContainsKey(talkgroupId) ? _settingsManager.ChannelOutputDevices[talkgroupId] : 0;

            var waveOut = new WaveOutEvent
            {
                DeviceNumber = deviceIndex
            };

            var bufferProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1))
            {
                DiscardOnBufferOverflow = true
            };

            var gainProvider = new GainSampleProvider(bufferProvider.ToSampleProvider())
            {
                Gain = 1.0f
            };

            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(8000, 1))
            {
                ReadFully = true
            };

            mixer.AddMixerInput(gainProvider);

            waveOut.Init(mixer);
            waveOut.Play();

            _talkgroupProviders[talkgroupId] = (waveOut, mixer, bufferProvider, gainProvider);
        }

        /// <summary>
        /// Adjusts the volume of a specific talkgroup stream
        /// </summary>
        public void SetTalkgroupVolume(string talkgroupId, float volume)
        {
            if (_talkgroupProviders.ContainsKey(talkgroupId))
            {
                _talkgroupProviders[talkgroupId].gainProvider.Gain = volume;
            }
            else
            {
                AddTalkgroupStream(talkgroupId);
                _talkgroupProviders[talkgroupId].gainProvider.Gain = volume;
            }
        }

        /// <summary>
        /// Set stream output device
        /// </summary>
        /// <param name="talkgroupId"></param>
        /// <param name="deviceIndex"></param>
        public void SetTalkgroupOutputDevice(string talkgroupId, int deviceIndex)
        {
            if (_talkgroupProviders.ContainsKey(talkgroupId))
            {
                _talkgroupProviders[talkgroupId].waveOut.Stop();
                _talkgroupProviders.Remove(talkgroupId);
            }

            _settingsManager.UpdateChannelOutputDevice(talkgroupId, deviceIndex);
            AddTalkgroupStream(talkgroupId);
        }

        /// <summary>
        /// Lop off the wave out
        /// </summary>
        public void Stop()
        {
            foreach (var provider in _talkgroupProviders.Values)
                provider.waveOut.Stop();
        }
    }
}
