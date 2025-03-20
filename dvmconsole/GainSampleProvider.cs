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

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class GainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private float gain = 1.0f;

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public WaveFormat WaveFormat { get; }

        /// <summary>
        /// 
        /// </summary>
        public float Gain
        {
            get => gain;
            set => gain = Math.Max(0, value);
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="GainSampleProvider"/> class.
        /// </summary>
        /// <param name="source"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public GainSampleProvider(ISampleProvider source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            WaveFormat = source.WaveFormat;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            for (int i = 0; i < samplesRead; i++)
                buffer[offset + i] *= gain;

            return samplesRead;
        }
    } // public class GainSampleProvider : ISampleProvider
} // namespace dvmconsole
