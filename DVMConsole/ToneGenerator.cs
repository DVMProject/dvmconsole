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
*
*/

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class ToneGenerator
    {
        private readonly int sampleRate = 8000;
        private readonly int bitsPerSample = 16;
        private readonly int channels = 1;
        private WaveOutEvent waveOut;
        private BufferedWaveProvider waveProvider;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="ToneGenerator"/> class.
        /// </summary>
        public ToneGenerator()
        {
            waveOut = new WaveOutEvent();
            waveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels));
            waveOut.Init(waveProvider);
        }

        /// <summary>
        /// Generate a sine wave tone at the specified frequency and duration.
        /// </summary>
        /// <param name="frequency">Frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        /// <returns>PCM data as a byte array</returns>
        public byte[] GenerateTone(double frequency, double durationSeconds)
        {
            int sampleCount = (int)(sampleRate * durationSeconds);
            byte[] buffer = new byte[sampleCount * (bitsPerSample / 8)];

            for (int i = 0; i < sampleCount; i++)
            {
                double time = (double)i / sampleRate;
                short sampleValue = (short)(Math.Sin(2 * Math.PI * frequency * time) * short.MaxValue);

                buffer[i * 2] = (byte)(sampleValue & 0xFF);
                buffer[i * 2 + 1] = (byte)((sampleValue >> 8) & 0xFF);
            }

            return buffer;
        }

        /// <summary>
        /// Play the generated tone through the speakers.
        /// </summary>
        /// <param name="frequency">Frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        public void PlayTone(double frequency, double durationSeconds)
        {
            byte[] toneData = GenerateTone(frequency, durationSeconds);

            waveProvider.ClearBuffer();
            waveProvider.AddSamples(toneData, 0, toneData.Length);

            waveOut.Play();
        }

        /// <summary>
        /// Stop playback.
        /// </summary>
        public void StopTone()
        {
            waveOut.Stop();
        }

        /// <summary>
        /// Dispose of resources.
        /// </summary>
        public void Dispose()
        {
            waveOut.Dispose();
        }
    } // public class ToneGenerator
} // namespace dvmconsole
