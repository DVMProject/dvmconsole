// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

using NAudio.Wave;
using System.Windows.Threading;

namespace DVMConsole
{
    public class WaveFilePlaybackManager
    {
        private readonly string _waveFilePath;
        private readonly DispatcherTimer _timer;
        private WaveOutEvent _waveOut;
        private AudioFileReader _audioFileReader;
        private bool _isPlaying;

        public WaveFilePlaybackManager(string waveFilePath, int intervalMilliseconds = 500)
        {
            if (string.IsNullOrEmpty(waveFilePath))
                throw new ArgumentNullException(nameof(waveFilePath), "Wave file path cannot be null or empty.");

            _waveFilePath = waveFilePath;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            if (_isPlaying)
                return;

            InitializeAudio();
            _isPlaying = true;
            _timer.Start();
        }

        public void Stop()
        {
            if (!_isPlaying)
                return;

            _timer.Stop();
            DisposeAudio();
            _isPlaying = false;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            PlayAudio();
        }

        private void InitializeAudio()
        {
            _audioFileReader = new AudioFileReader(_waveFilePath);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFileReader);
        }

        private void PlayAudio()
        {
            if (_waveOut != null && _waveOut.PlaybackState != PlaybackState.Playing)
            {
                _waveOut.Stop();
                _audioFileReader.Position = 0;
                _waveOut.Play();
            }
        }

        private void DisposeAudio()
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _audioFileReader?.Dispose();
            _waveOut = null;
            _audioFileReader = null;
        }
    }
}
