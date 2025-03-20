// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Patrick McDonnell, W3AXL
*
*/

using NWaves.Signals;
using NWaves.Transforms;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class MBEToneDetector
    {
        // Samplerate is 8000 Hz
        private static int sample_rate = 8000;

        // We operate on 160 sample (20ms @ 8kHz) windows
        private static int window_size = 160;

        // There are 128 possible tone indexes per TIA-102.BABA-1
        private static int num_coeffs = 128;

        // Bin size in hz
        private static float bin_size_hz = (float)sample_rate / 2f / (float)num_coeffs;

        // Bounds of tone detection (in bin index format)
        private int low_bin_limit;
        private int high_bin_limit;

        // This is the tone detection ratio (amplitude of max bin divided by average of all others)
        private int detect_ratio;

        // This is the number of "hits" on a frequency we need to get before we detect a valid tone
        private int hits_reqd;

        // Counter for the above hits
        private int hits_freq;
        private int num_hits;

        // The STFT (short-time fourier transform) operator
        private Stft stft;

        /*
        ** Methods
        */

        /// <summary>
        /// Create a pitch detector which reports the running average of pitch for a sequence of samples
        /// </summary>
        /// <param name="detect_ratio">Ratio required for a valid tone detection</param>
        /// <param name="hits_reqd">Number of repeated "hits" on a frequency to count as a tone detection</param>
        public MBEToneDetector(int detect_ratio = 90, int hits_reqd = 2, int low_limit = 250, int high_limit = 3000)
        {
            this.detect_ratio = detect_ratio;
            this.hits_reqd = hits_reqd;
            stft = new Stft(window_size, 1, NWaves.Windows.WindowType.Hann, num_coeffs);
            hits_freq = 0;
            num_hits = 0;
            low_bin_limit = (int)(low_limit / bin_size_hz);
            high_bin_limit = (int)(high_limit / bin_size_hz);
        }

        /// <summary>
        /// Perform a tone analysis on the provided samples, and return a tone frequency if one is detected
        /// </summary>
        /// <param name="samples"></param>
        /// <returns></returns>
        public int Detect(DiscreteSignal signal)
        {
            // Validate input
            if (signal.Length != window_size)
            {
                throw new ArgumentOutOfRangeException($"Signal must be {window_size} samples long!");
            }
            if (signal.SamplingRate != sample_rate)
            {
                throw new ArgumentOutOfRangeException($"Signal must have sample rate of {sample_rate} Hz!");
            }

            // Analyze
            float[] values = stft.Spectrogram(signal)[0];

            // Remove bins outside our limit
            float[] limited_values = values[low_bin_limit..high_bin_limit];

            // Find max (from https://stackoverflow.com/a/50239922/1842613)
            (float max_val, int max_idx) = limited_values.Select((n, i) => (n, i)).Max();

            // Add back in our lower limit so the index is correct
            max_idx += low_bin_limit;

            // Calculate sum of all others
            float sum = values.Sum() - max_val;

            // Find average
            float avg = sum / (window_size - 1);

            // Find ratio
            float ratio = max_val / avg;

            // Debug
            //Log.Logger.Debug($"(Tone detector): max at {max_idx} ({(int)(max_idx * bin_size_hz)} Hz): {max_val}, ratio: {ratio}");

            // Return if above threshold
            if (ratio > detect_ratio)
            {
                // Calculate the tone frequency
                int tone_freq = (int)(bin_size_hz * max_idx);

                // Determine hits
                if (hits_freq == tone_freq)
                {
                    num_hits++;
                    if (num_hits >= hits_reqd)
                    {
                        // Debug
                        //Log.Logger.Debug($"Detected {tone_freq} Hz tone! (ratio {ratio})");
                        return tone_freq;
                    }
                }
                else
                {
                    num_hits = 1;
                    hits_freq = tone_freq;
                }
            }
            return 0;
        }
    } // public class MBEToneDetector
} // namespace dvmconsole
