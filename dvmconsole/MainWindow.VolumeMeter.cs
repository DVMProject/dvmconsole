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
*/

using dvmconsole.Controls;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private enum VolumeMeterSource
        {
            ConsoleTx,
            RadioRx
        }

        // Visual-only tuning. RX vocoder samples are usually much lower than local mic PCM,
        // so they need more display gain while console-originated audio needs restraint.
        private const double VOLUME_METER_TX_GAIN = 0.85;
        private const double VOLUME_METER_RX_GAIN = 3.8;
        private const double VOLUME_METER_RMS_WEIGHT = 0.72;
        private const double VOLUME_METER_PEAK_WEIGHT = 0.28;
        private const double VOLUME_METER_NOISE_FLOOR = 0.006;

        private void UpdateVolumeMeterFromSamples(ChannelBox channel, short[] samples, VolumeMeterSource source)
        {
            if (channel == null)
                return;
            if (samples == null || samples.Length == 0)
            {
                channel.VolumeMeterLevel = 0;
                return;
            }

            double sumSquares = 0;
            double peak = 0;
            foreach (short sample in samples)
            {
                double normalized = Math.Abs(sample / 32768d);
                sumSquares += normalized * normalized;
                if (normalized > peak)
                    peak = normalized;
            }

            double rms = Math.Sqrt(sumSquares / samples.Length);
            double blendedLevel = (rms * VOLUME_METER_RMS_WEIGHT) + (peak * VOLUME_METER_PEAK_WEIGHT);
            double gain = source == VolumeMeterSource.RadioRx ? VOLUME_METER_RX_GAIN : VOLUME_METER_TX_GAIN;
            double adjustedLevel = Math.Max(0, blendedLevel - VOLUME_METER_NOISE_FLOOR) * gain;

            channel.VolumeMeterLevel = Math.Min(1, adjustedLevel);
        }
    }
}
