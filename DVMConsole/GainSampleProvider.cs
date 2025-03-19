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
using System;

namespace DVMConsole
{
    public class GainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _gain = 1.0f;

        public GainSampleProvider(ISampleProvider source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public float Gain
        {
            get => _gain;
            set => _gain = Math.Max(0, value);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                buffer[offset + i] *= _gain;
            }

            return samplesRead;
        }
    }
}
