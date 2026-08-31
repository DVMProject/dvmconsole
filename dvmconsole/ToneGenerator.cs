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
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
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
        private const double DEFAULT_TONE_AMPLITUDE = 0.35;
        private const int DEFAULT_FADE_MS = 5;

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
        }

        /// <summary>
        /// Generate a sine wave tone at the specified frequency and duration.
        /// </summary>
        /// <param name="frequency">Frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        /// <returns>PCM data as a byte array</returns>
        public byte[] GenerateTone(double frequency, double durationSeconds)
        {
            return GenerateTone(frequency, durationSeconds, DEFAULT_TONE_AMPLITUDE);
        }

        /// <summary>
        /// Generate a sine wave tone at the specified frequency, duration, and amplitude.
        /// </summary>
        /// <param name="frequency">Frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        /// <param name="amplitude">Linear amplitude, from 0.0 to 1.0</param>
        /// <returns>PCM data as a byte array</returns>
        public byte[] GenerateTone(double frequency, double durationSeconds, double amplitude)
        {
            int sampleCount = Math.Max(1, (int)Math.Round(sampleRate * durationSeconds));
            byte[] buffer = new byte[sampleCount * (bitsPerSample / 8)];
            double clampedAmplitude = Math.Clamp(amplitude, 0.0, 1.0);

            for (int i = 0; i < sampleCount; i++)
            {
                double time = (double)i / sampleRate;
                short sampleValue = (short)(Math.Sin(2 * Math.PI * frequency * time) * clampedAmplitude * short.MaxValue * GetEnvelope(i, sampleCount));

                buffer[i * 2] = (byte)(sampleValue & 0xFF);
                buffer[i * 2 + 1] = (byte)((sampleValue >> 8) & 0xFF);
            }

            return buffer;
        }

        /// <summary>
        /// Generate two sine waves mixed together at the specified frequencies and duration.
        /// </summary>
        /// <param name="lowFrequency">Low group frequency in Hz</param>
        /// <param name="highFrequency">High group frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        /// <returns>PCM data as a byte array</returns>
        public byte[] GenerateDualTone(double lowFrequency, double highFrequency, double durationSeconds)
        {
            return GenerateDualTone(lowFrequency, highFrequency, durationSeconds, DEFAULT_TONE_AMPLITUDE);
        }

        /// <summary>
        /// Generate two sine waves mixed together at the specified frequencies, duration, and amplitude.
        /// </summary>
        /// <param name="lowFrequency">Low group frequency in Hz</param>
        /// <param name="highFrequency">High group frequency in Hz</param>
        /// <param name="durationSeconds">Duration in seconds</param>
        /// <param name="amplitude">Linear amplitude, from 0.0 to 1.0</param>
        /// <returns>PCM data as a byte array</returns>
        public byte[] GenerateDualTone(double lowFrequency, double highFrequency, double durationSeconds, double amplitude)
        {
            int sampleCount = Math.Max(1, (int)Math.Round(sampleRate * durationSeconds));
            byte[] buffer = new byte[sampleCount * (bitsPerSample / 8)];
            double clampedAmplitude = Math.Clamp(amplitude, 0.0, 1.0);

            for (int i = 0; i < sampleCount; i++)
            {
                double time = (double)i / sampleRate;
                double low = Math.Sin(2 * Math.PI * lowFrequency * time);
                double high = Math.Sin(2 * Math.PI * highFrequency * time);
                short sampleValue = (short)(((low + high) / 2.0) * clampedAmplitude * short.MaxValue * GetEnvelope(i, sampleCount));

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
            EnsurePlaybackInitialized();

            waveProvider.ClearBuffer();
            waveProvider.AddSamples(toneData, 0, toneData.Length);

            waveOut.Play();
        }

        /// <summary>
        /// Stop playback.
        /// </summary>
        public void StopTone()
        {
            waveOut?.Stop();
        }

        /// <summary>
        /// Dispose of resources.
        /// </summary>
        public void Dispose()
        {
            waveOut?.Dispose();
            waveOut = null;
            waveProvider = null;
        }

        private void EnsurePlaybackInitialized()
        {
            if (waveOut != null && waveProvider != null)
                return;

            waveOut = new WaveOutEvent();
            waveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels));
            waveOut.Init(waveProvider);
        }

        private double GetEnvelope(int sampleIndex, int sampleCount)
        {
            int fadeSamples = Math.Min(sampleRate * DEFAULT_FADE_MS / 1000, sampleCount / 2);
            if (fadeSamples <= 0)
                return 1.0;

            if (sampleIndex < fadeSamples)
                return (double)sampleIndex / fadeSamples;

            if (sampleIndex >= sampleCount - fadeSamples)
                return (double)(sampleCount - sampleIndex - 1) / fadeSamples;

            return 1.0;
        }
    } // public class ToneGenerator
} // namespace dvmconsole
