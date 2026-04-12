// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, K7CBL
*/

namespace dvmconsole
{
    public partial class MainWindow
    {
        // Lightweight input AGC for console-originated microphone audio. Default setting is off.
        private const double INPUT_AGC_TARGET_RMS = 0.20;
        private const double INPUT_AGC_MAX_GAIN = 3.5;
        private const double INPUT_AGC_MIN_GAIN = 0.45;
        private const double INPUT_AGC_ATTACK = 0.18;
        private const double INPUT_AGC_RELEASE = 0.04;
        private const double INPUT_AGC_NOISE_GATE = 0.012;

        private double inputAgcGain = 1.0;

        private void ApplyInputAgc(byte[] pcm)
        {
            if (pcm == null || pcm.Length < 2 || !settingsManager.AudioInputAgcEnabled)
                return;

            int sampleCount = pcm.Length / 2;
            double sumSquares = 0;
            for (int i = 0; i < pcm.Length - 1; i += 2)
            {
                short sample = (short)((pcm[i + 1] << 8) | pcm[i]);
                double normalized = sample / 32768d;
                sumSquares += normalized * normalized;
            }

            double rms = Math.Sqrt(sumSquares / sampleCount);
            if (rms < INPUT_AGC_NOISE_GATE)
                return;

            double desiredGain = Math.Clamp(INPUT_AGC_TARGET_RMS / rms, INPUT_AGC_MIN_GAIN, INPUT_AGC_MAX_GAIN);
            double smoothing = desiredGain < inputAgcGain ? INPUT_AGC_ATTACK : INPUT_AGC_RELEASE;
            inputAgcGain += (desiredGain - inputAgcGain) * smoothing;

            for (int i = 0; i < pcm.Length - 1; i += 2)
            {
                short sample = (short)((pcm[i + 1] << 8) | pcm[i]);
                int adjusted = (int)Math.Round(sample * inputAgcGain);
                adjusted = Math.Clamp(adjusted, short.MinValue, short.MaxValue);
                pcm[i] = (byte)(adjusted & 0xFF);
                pcm[i + 1] = (byte)((adjusted >> 8) & 0xFF);
            }
        }
    }
}
