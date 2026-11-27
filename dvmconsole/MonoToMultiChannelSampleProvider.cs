// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using NAudio.Wave;

namespace dvmconsole
{
    internal sealed class MonoToMultiChannelSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly int outputChannels;
        private float[] sourceBuffer = Array.Empty<float>();

        public MonoToMultiChannelSampleProvider(ISampleProvider source, int outputChannels)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.WaveFormat.Channels != 1)
                throw new ArgumentException("Source provider must be mono.", nameof(source));
            if (outputChannels < 1)
                throw new ArgumentOutOfRangeException(nameof(outputChannels));

            this.outputChannels = outputChannels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outputChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<float> buffer)
        {
            int framesRequested = buffer.Length / outputChannels;
            if (framesRequested <= 0)
                return 0;

            if (sourceBuffer.Length < framesRequested)
                sourceBuffer = new float[framesRequested];

            int sourceSamplesRead = source.Read(sourceBuffer.AsSpan(0, framesRequested));
            for (int frame = 0; frame < sourceSamplesRead; frame++)
            {
                float sample = sourceBuffer[frame];
                int outputOffset = frame * outputChannels;
                for (int channel = 0; channel < outputChannels; channel++)
                    buffer[outputOffset + channel] = sample;
            }

            return sourceSamplesRead * outputChannels;
        }
    }
}
