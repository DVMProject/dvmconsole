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
        private const double MIC_PROCESSING_SAMPLE_RATE = 8000.0;
        private const double MIC_EQ_LOW_CENTER_HZ = 150.0;
        private const double MIC_EQ_MID_CENTER_HZ = 700.0;
        private const double MIC_EQ_HIGH_CENTER_HZ = 2000.0;
        private const double MIC_EQ_Q = 1.0;
        private const double MIC_PROCESSING_EPSILON = 0.0001;

        private readonly object micProcessingSettingsSync = new object();
        private bool? previewMicAgcEnabled;
        private double previewMicInputGain = double.NaN;
        private double previewMicEqLowGainDb = double.NaN;
        private double previewMicEqMidGainDb = double.NaN;
        private double previewMicEqHighGainDb = double.NaN;
        private readonly PeakingEqFilter micEqLowFilter = new PeakingEqFilter();
        private readonly PeakingEqFilter micEqMidFilter = new PeakingEqFilter();
        private readonly PeakingEqFilter micEqHighFilter = new PeakingEqFilter();
        private bool micEqFiltersConfigured = false;
        private double configuredLowGainDb = double.NaN;
        private double configuredMidGainDb = double.NaN;
        private double configuredHighGainDb = double.NaN;

        private readonly struct MicrophoneProcessingSettings
        {
            public MicrophoneProcessingSettings(double gain, double lowGainDb, double midGainDb, double highGainDb)
            {
                Gain = gain;
                LowGainDb = lowGainDb;
                MidGainDb = midGainDb;
                HighGainDb = highGainDb;
            }

            public double Gain { get; }
            public double LowGainDb { get; }
            public double MidGainDb { get; }
            public double HighGainDb { get; }
        }

        private void PreviewMicrophoneProcessingSettings(bool agcEnabled, double gain, double lowGainDb, double midGainDb, double highGainDb)
        {
            lock (micProcessingSettingsSync)
            {
                previewMicAgcEnabled = agcEnabled;
                previewMicInputGain = SettingsManager.NormalizeAudioInputGain(gain);
                previewMicEqLowGainDb = SettingsManager.NormalizeAudioInputEqGainDb(lowGainDb);
                previewMicEqMidGainDb = SettingsManager.NormalizeAudioInputEqGainDb(midGainDb);
                previewMicEqHighGainDb = SettingsManager.NormalizeAudioInputEqGainDb(highGainDb);
            }

            ResetMicrophoneProcessingState();
            ResetInputAgcGain();
        }

        private void RestoreSavedMicrophoneProcessingSettings()
        {
            lock (micProcessingSettingsSync)
            {
                previewMicAgcEnabled = null;
                previewMicInputGain = double.NaN;
                previewMicEqLowGainDb = double.NaN;
                previewMicEqMidGainDb = double.NaN;
                previewMicEqHighGainDb = double.NaN;
            }

            ResetMicrophoneProcessingState();
            ResetInputAgcGain();
        }

        private void ResetMicrophoneProcessingState()
        {
            micEqLowFilter.Reset();
            micEqMidFilter.Reset();
            micEqHighFilter.Reset();
            micEqFiltersConfigured = false;
        }

        private void ApplyMicrophoneProcessing(byte[] pcm)
        {
            if (pcm == null || pcm.Length < 2)
                return;

            MicrophoneProcessingSettings processingSettings = GetMicrophoneProcessingSettings();
            double gain = processingSettings.Gain;
            double lowGainDb = processingSettings.LowGainDb;
            double midGainDb = processingSettings.MidGainDb;
            double highGainDb = processingSettings.HighGainDb;

            bool hasGain = Math.Abs(gain - 1.0) > MIC_PROCESSING_EPSILON;
            bool hasEq = Math.Abs(lowGainDb) > MIC_PROCESSING_EPSILON ||
                Math.Abs(midGainDb) > MIC_PROCESSING_EPSILON ||
                Math.Abs(highGainDb) > MIC_PROCESSING_EPSILON;

            if (!hasGain && !hasEq)
            {
                ResetMicrophoneProcessingState();
                return;
            }

            if (hasEq)
                ConfigureMicEqFilters(lowGainDb, midGainDb, highGainDb);

            for (int i = 0; i < pcm.Length - 1; i += 2)
            {
                short sample = (short)((pcm[i + 1] << 8) | pcm[i]);
                double shapedSample = sample / 32768.0;

                if (hasEq)
                {
                    if (Math.Abs(lowGainDb) > MIC_PROCESSING_EPSILON)
                        shapedSample = micEqLowFilter.Process(shapedSample);
                    if (Math.Abs(midGainDb) > MIC_PROCESSING_EPSILON)
                        shapedSample = micEqMidFilter.Process(shapedSample);
                    if (Math.Abs(highGainDb) > MIC_PROCESSING_EPSILON)
                        shapedSample = micEqHighFilter.Process(shapedSample);
                }

                shapedSample *= gain;

                int adjusted = (int)Math.Round(shapedSample * 32768.0);
                adjusted = Math.Clamp(adjusted, short.MinValue, short.MaxValue);
                pcm[i] = (byte)(adjusted & 0xFF);
                pcm[i + 1] = (byte)((adjusted >> 8) & 0xFF);
            }
        }

        private void ConfigureMicEqFilters(double lowGainDb, double midGainDb, double highGainDb)
        {
            if (micEqFiltersConfigured &&
                Math.Abs(configuredLowGainDb - lowGainDb) <= MIC_PROCESSING_EPSILON &&
                Math.Abs(configuredMidGainDb - midGainDb) <= MIC_PROCESSING_EPSILON &&
                Math.Abs(configuredHighGainDb - highGainDb) <= MIC_PROCESSING_EPSILON)
            {
                return;
            }

            micEqLowFilter.Configure(MIC_PROCESSING_SAMPLE_RATE, MIC_EQ_LOW_CENTER_HZ, MIC_EQ_Q, lowGainDb);
            micEqMidFilter.Configure(MIC_PROCESSING_SAMPLE_RATE, MIC_EQ_MID_CENTER_HZ, MIC_EQ_Q, midGainDb);
            micEqHighFilter.Configure(MIC_PROCESSING_SAMPLE_RATE, MIC_EQ_HIGH_CENTER_HZ, MIC_EQ_Q, highGainDb);

            configuredLowGainDb = lowGainDb;
            configuredMidGainDb = midGainDb;
            configuredHighGainDb = highGainDb;
            micEqFiltersConfigured = true;
        }

        private MicrophoneProcessingSettings GetMicrophoneProcessingSettings()
        {
            lock (micProcessingSettingsSync)
            {
                return new MicrophoneProcessingSettings(
                    double.IsNaN(previewMicInputGain) ? SettingsManager.NormalizeAudioInputGain(settingsManager.AudioInputGain) : previewMicInputGain,
                    double.IsNaN(previewMicEqLowGainDb) ? SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqLowGainDb) : previewMicEqLowGainDb,
                    double.IsNaN(previewMicEqMidGainDb) ? SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqMidGainDb) : previewMicEqMidGainDb,
                    double.IsNaN(previewMicEqHighGainDb) ? SettingsManager.NormalizeAudioInputEqGainDb(settingsManager.AudioInputEqHighGainDb) : previewMicEqHighGainDb);
            }
        }

        private bool IsAudioInputAgcEnabled()
        {
            lock (micProcessingSettingsSync)
                return previewMicAgcEnabled ?? settingsManager.AudioInputAgcEnabled;
        }

        private sealed class PeakingEqFilter
        {
            private double b0 = 1.0;
            private double b1 = 0.0;
            private double b2 = 0.0;
            private double a1 = 0.0;
            private double a2 = 0.0;
            private double x1 = 0.0;
            private double x2 = 0.0;
            private double y1 = 0.0;
            private double y2 = 0.0;

            public void Configure(double sampleRate, double centerFrequencyHz, double q, double gainDb)
            {
                double clampedFrequency = Math.Clamp(centerFrequencyHz, 1.0, sampleRate / 2.0 - 1.0);
                double a = Math.Pow(10.0, gainDb / 40.0);
                double omega = 2.0 * Math.PI * clampedFrequency / sampleRate;
                double alpha = Math.Sin(omega) / (2.0 * Math.Max(q, 0.01));
                double cosOmega = Math.Cos(omega);

                double rawB0 = 1.0 + alpha * a;
                double rawB1 = -2.0 * cosOmega;
                double rawB2 = 1.0 - alpha * a;
                double rawA0 = 1.0 + alpha / a;
                double rawA1 = -2.0 * cosOmega;
                double rawA2 = 1.0 - alpha / a;

                b0 = rawB0 / rawA0;
                b1 = rawB1 / rawA0;
                b2 = rawB2 / rawA0;
                a1 = rawA1 / rawA0;
                a2 = rawA2 / rawA0;
            }

            public double Process(double sample)
            {
                double output = (b0 * sample) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2);
                x2 = x1;
                x1 = sample;
                y2 = y1;
                y1 = output;
                return output;
            }

            public void Reset()
            {
                x1 = 0.0;
                x2 = 0.0;
                y1 = 0.0;
                y2 = 0.0;
            }
        }
    }
}
