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

namespace dvmconsole
{
    public partial class MainWindow
    {
        private const string FNE_CONNECTION_CHIME_OUTPUT_KEY = "__dvmconsole_fne_connection_chime";
        private const int FNE_CHIME_SAMPLE_RATE = 8000;
        private const double FNE_CHIME_TONE_DURATION_SECONDS = 0.115;
        private const double FNE_CHIME_GAP_SECONDS = 0.055;
        private const double FNE_CHIME_AMPLITUDE = 0.18;
        private const int FNE_CHIME_FADE_SAMPLES = 48;

        private static readonly double[] FNE_CONNECT_CHIME_FREQUENCIES = { 880.0, 1175.0 };
        private static readonly double[] FNE_DISCONNECT_CHIME_FREQUENCIES = { 440.0, 330.0 };

        private bool fneStartupConnectChimePlayed;

        private void PlayFneConnectedChime(FneConnectionEntry entry)
        {
            if (entry?.IsInitialAutoStartPending == true)
            {
                entry.IsInitialAutoStartPending = false;
                if (fneStartupConnectChimePlayed)
                    return;

                fneStartupConnectChimePlayed = true;
            }

            PlayFneConnectionChime(connected: true);
        }

        private void PlayFneConnectionChime(bool connected)
        {
            try
            {
                byte[] chimePcm = BuildFneConnectionChime(connected ? FNE_CONNECT_CHIME_FREQUENCIES : FNE_DISCONNECT_CHIME_FREQUENCIES);
                audioManager.PlayOneShot(FNE_CONNECTION_CHIME_OUTPUT_KEY, chimePcm);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Failed to play FNE connection chime: {ex.Message}");
            }
        }

        private static byte[] BuildFneConnectionChime(IReadOnlyList<double> frequencies)
        {
            int toneSamples = Math.Max(1, (int)Math.Round(FNE_CHIME_SAMPLE_RATE * FNE_CHIME_TONE_DURATION_SECONDS));
            int gapSamples = Math.Max(0, (int)Math.Round(FNE_CHIME_SAMPLE_RATE * FNE_CHIME_GAP_SECONDS));
            int totalSamples = (toneSamples * frequencies.Count) + (gapSamples * Math.Max(0, frequencies.Count - 1));
            byte[] pcm = new byte[totalSamples * 2];

            int sampleOffset = 0;
            for (int toneIndex = 0; toneIndex < frequencies.Count; toneIndex++)
            {
                WriteFneChimeTone(pcm, sampleOffset, toneSamples, frequencies[toneIndex]);
                sampleOffset += toneSamples;

                if (toneIndex < frequencies.Count - 1)
                    sampleOffset += gapSamples;
            }

            return pcm;
        }

        private static void WriteFneChimeTone(byte[] pcm, int sampleOffset, int sampleCount, double frequency)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                double envelope = CalculateFneChimeEnvelope(i, sampleCount);
                double sample = Math.Sin(2.0 * Math.PI * frequency * i / FNE_CHIME_SAMPLE_RATE) * FNE_CHIME_AMPLITUDE * envelope;
                short pcmSample = (short)Math.Clamp(Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
                int byteOffset = (sampleOffset + i) * 2;
                pcm[byteOffset] = (byte)(pcmSample & 0xFF);
                pcm[byteOffset + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }
        }

        private static double CalculateFneChimeEnvelope(int sampleIndex, int sampleCount)
        {
            int fadeSamples = Math.Min(FNE_CHIME_FADE_SAMPLES, sampleCount / 2);
            if (fadeSamples <= 0)
                return 1.0;

            if (sampleIndex < fadeSamples)
                return sampleIndex / (double)fadeSamples;

            int samplesFromEnd = sampleCount - sampleIndex - 1;
            if (samplesFromEnd < fadeSamples)
                return samplesFromEnd / (double)fadeSamples;

            return 1.0;
        }
    }
}
