// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

using System.Windows.Threading;

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class WaveFilePlaybackManager
    {
        private readonly string waveFilePath;
        private readonly DispatcherTimer timer;
        private WaveOutEvent waveOut;
        private AudioFileReader audioFileReader;
        private bool isPlaying;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="WaveFilePlaybackManager"/> class.
        /// </summary>
        /// <param name="waveFilePath"></param>
        /// <param name="intervalMilliseconds"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public WaveFilePlaybackManager(string waveFilePath, int intervalMilliseconds = 500)
        {
            if (string.IsNullOrEmpty(waveFilePath))
                throw new ArgumentNullException(nameof(waveFilePath), "Wave file path cannot be null or empty.");

            this.waveFilePath = waveFilePath;
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };
            timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            if (isPlaying)
                return;

            InitializeAudio();
            isPlaying = true;
            timer.Start();
        }

        /// <summary>
        /// 
        /// </summary>
        public void Stop()
        {
            if (!isPlaying)
                return;

            timer.Stop();
            DisposeAudio();
            isPlaying = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTimerTick(object sender, EventArgs e)
        {
            PlayAudio();
        }

        /// <summary>
        /// 
        /// </summary>
        private void InitializeAudio()
        {
            audioFileReader = new AudioFileReader(waveFilePath);
            waveOut = new WaveOutEvent();
            waveOut.Init(audioFileReader);
        }

        /// <summary>
        /// 
        /// </summary>
        private void PlayAudio()
        {
            if (waveOut != null && waveOut.PlaybackState != PlaybackState.Playing)
            {
                waveOut.Stop();
                audioFileReader.Position = 0;
                waveOut.Play();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void DisposeAudio()
        {
            waveOut?.Stop();
            waveOut?.Dispose();
            audioFileReader?.Dispose();
            waveOut = null;
            audioFileReader = null;
        }
    } // public class WaveFilePlaybackManager
} // namespace dvmconsole
