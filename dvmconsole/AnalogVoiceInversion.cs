// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using System;

namespace dvmconsole
{
    internal sealed class AnalogVoiceInversion
    {
        private const int SampleRate = 8000;
        private const int TapCount = 127;
        private const int Delay = (TapCount - 1) / 2;
        private readonly double[] bandpassTaps = new double[TapCount];
        private readonly double[] hilbertTaps = new double[TapCount];
        private readonly double[] inputHistory = new double[TapCount];
        private readonly double[] bandHistory = new double[TapCount];
        private readonly double phaseStep;
        private int position;
        private double phase;

        public static bool TryGetFrequency(int code, out int frequency)
        {
            frequency = code switch
            {
                2 => 3000,
                3 => 3050,
                4 => 3010,
                5 => 4096,
                6 => 2000,
                7 => 2500,
                8 => 2700,
                9 => 3400,
                10 => 4000,
                11 => 2900,
                12 => 3100,
                13 => 2222,
                14 => 3333,
                15 => 2555,
                16 => 2333,
                _ => 0
            };
            return frequency != 0;
        }

        public AnalogVoiceInversion(int code)
        {
            if (!TryGetFrequency(code, out int frequency))
                throw new ArgumentOutOfRangeException(nameof(code), "Analog scrambler code must be 2 through 16.");

            phaseStep = 2.0 * Math.PI * frequency / SampleRate;
            double low = Math.Max(150.0, frequency - 3800.0);
            double high = Math.Min(3800.0, frequency - 150.0);
            for (int tap = 0; tap < TapCount; tap++)
            {
                int offset = tap - Delay;
                double window = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * tap / (TapCount - 1));
                bandpassTaps[tap] = offset == 0
                    ? 2.0 * (high - low) / SampleRate
                    : (Math.Sin(2.0 * Math.PI * high * offset / SampleRate) -
                       Math.Sin(2.0 * Math.PI * low * offset / SampleRate)) / (Math.PI * offset);
                bandpassTaps[tap] *= window;
                if (offset != 0 && (offset & 1) != 0)
                    hilbertTaps[tap] = 2.0 * window / (Math.PI * offset);
            }
        }

        public void Process(Span<short> samples)
        {
            for (int sample = 0; sample < samples.Length; sample++)
            {
                inputHistory[position] = samples[sample];
                double band = 0;
                for (int tap = 0; tap < TapCount; tap++)
                    band += bandpassTaps[tap] * inputHistory[(position - tap + TapCount) % TapCount];

                bandHistory[position] = band;
                double quadrature = 0;
                for (int tap = 0; tap < TapCount; tap++)
                    quadrature += hilbertTaps[tap] * bandHistory[(position - tap + TapCount) % TapCount];

                double delayed = bandHistory[(position - Delay + TapCount) % TapCount];
                double inverted = delayed * Math.Cos(phase) + quadrature * Math.Sin(phase);
                samples[sample] = (short)Math.Clamp(Math.Round(inverted), short.MinValue, short.MaxValue);
                phase += phaseStep;
                if (phase >= 2.0 * Math.PI)
                    phase -= 2.0 * Math.PI;
                position = (position + 1) % TapCount;
            }
        }
    }
}
