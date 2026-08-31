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

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace dvmconsole
{
    public sealed class ConsoleAudioDataAvailableEventArgs : EventArgs
    {
        public ConsoleAudioDataAvailableEventArgs(byte[] buffer)
        {
            Buffer = buffer ?? Array.Empty<byte>();
            BytesRecorded = Buffer.Length;
        }

        public byte[] Buffer { get; }
        public int BytesRecorded { get; }
    }

    public interface IConsoleAudioInput : IDisposable
    {
        event EventHandler<ConsoleAudioDataAvailableEventArgs> DataAvailable;
        event EventHandler<StoppedEventArgs> RecordingStopped;

        AudioBackendKind Backend { get; }
        string DeviceDescription { get; }
        int LegacyFallbackDeviceNumber { get; }

        void StartRecording();
        void StopRecording();
    }

    public static class ConsoleAudioInputFactory
    {
        public static IConsoleAudioInput CreatePreferred(string deviceKey, int legacyDeviceNumber, bool forceLegacyMme = false)
        {
            bool preferWasapi = AudioDeviceResolver.IsWindowsDefault(deviceKey) ||
                AudioDeviceResolver.IsWasapiInputDeviceKey(deviceKey);

            if (preferWasapi && !forceLegacyMme)
            {
                try
                {
                    AudioDeviceResolver.AudioDeviceSelection selection =
                        AudioDeviceResolver.ResolveInputDevice(deviceKey, legacyDeviceNumber);
                    if (selection.Backend == AudioBackendKind.Wasapi && selection.WasapiDevice != null)
                        return new WasapiConsoleAudioInput(selection.WasapiDevice, selection.DisplayName, selection.DeviceNumber);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"WASAPI input unavailable; falling back to legacy MME input. {ex.Message}");
                }
            }

            AudioDeviceResolver.AudioDeviceSelection mmeSelection =
                AudioDeviceResolver.ResolveMmeInputFallback(legacyDeviceNumber, deviceKey);
            return new MmeConsoleAudioInput(mmeSelection.DeviceNumber, mmeSelection.DisplayName);
        }
    }

    internal sealed class MmeConsoleAudioInput : IConsoleAudioInput
    {
        private readonly WaveInEvent waveIn;
        private readonly FixedPcmBlockDispatcher blockDispatcher = new FixedPcmBlockDispatcher(AudioConverter.OriginalPcmLength);

        public MmeConsoleAudioInput(int deviceNumber, string displayName)
        {
            waveIn = new WaveInEvent
            {
                DeviceNumber = SettingsManager.NormalizeAudioDeviceIndex(deviceNumber),
                WaveFormat = new WaveFormat(8000, 16, 1)
            };
            DeviceDescription = string.IsNullOrWhiteSpace(displayName)
                ? $"Legacy MME input {waveIn.DeviceNumber}"
                : displayName;

            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;
        }

        public event EventHandler<ConsoleAudioDataAvailableEventArgs> DataAvailable;
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        public AudioBackendKind Backend => AudioBackendKind.Mme;
        public string DeviceDescription { get; }
        public int LegacyFallbackDeviceNumber => waveIn.DeviceNumber;

        public void StartRecording()
        {
            waveIn.StartRecording();
        }

        public void StopRecording()
        {
            waveIn.StopRecording();
        }

        public void Dispose()
        {
            waveIn.DataAvailable -= WaveIn_DataAvailable;
            waveIn.RecordingStopped -= WaveIn_RecordingStopped;
            waveIn.Dispose();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            blockDispatcher.AddSamples(e.Buffer, e.BytesRecorded, EmitAudioBlock);
        }

        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            blockDispatcher.Clear();
            RecordingStopped?.Invoke(this, e);
        }

        private void EmitAudioBlock(byte[] buffer)
        {
            DataAvailable?.Invoke(this, new ConsoleAudioDataAvailableEventArgs(buffer));
        }
    }

    internal sealed class WasapiConsoleAudioInput : IConsoleAudioInput
    {
        private const int SharedModeCaptureBufferMilliseconds = 100;

        private readonly MMDevice device;
        private readonly WasapiCapture capture;
        private readonly Pcm16Mono8kConverter converter;
        private readonly FixedPcmBlockDispatcher blockDispatcher = new FixedPcmBlockDispatcher(AudioConverter.OriginalPcmLength);
        private readonly object converterSync = new object();
        private bool loggedFirstBuffer;

        public WasapiConsoleAudioInput(MMDevice device, string displayName, int legacyFallbackDeviceNumber)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            capture = new WasapiCapture(device, useEventSync: false, audioBufferMillisecondsLength: SharedModeCaptureBufferMilliseconds)
            {
                ShareMode = AudioClientShareMode.Shared
            };
            converter = new Pcm16Mono8kConverter(capture.WaveFormat);
            DeviceDescription = string.IsNullOrWhiteSpace(displayName)
                ? $"WASAPI input {device.FriendlyName}"
                : displayName;
            LegacyFallbackDeviceNumber = SettingsManager.NormalizeAudioDeviceIndex(legacyFallbackDeviceNumber);

            Log.WriteLine($"WASAPI input format for {DeviceDescription}: {capture.WaveFormat}");

            capture.DataAvailable += Capture_DataAvailable;
            capture.RecordingStopped += Capture_RecordingStopped;
        }

        public event EventHandler<ConsoleAudioDataAvailableEventArgs> DataAvailable;
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        public AudioBackendKind Backend => AudioBackendKind.Wasapi;
        public string DeviceDescription { get; }
        public int LegacyFallbackDeviceNumber { get; }

        public void StartRecording()
        {
            capture.StartRecording();
        }

        public void StopRecording()
        {
            capture.StopRecording();
        }

        public void Dispose()
        {
            capture.DataAvailable -= Capture_DataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            capture.Dispose();
            device.Dispose();
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] convertedAudio;
            lock (converterSync)
                convertedAudio = converter.Convert(e.Buffer, e.BytesRecorded);

            if (!loggedFirstBuffer)
            {
                loggedFirstBuffer = true;
                Log.WriteLine($"WASAPI input buffer received from {DeviceDescription}: {e.BytesRecorded} bytes converted to {convertedAudio.Length} bytes.");
            }

            if (convertedAudio.Length > 0)
                blockDispatcher.AddSamples(convertedAudio, convertedAudio.Length, EmitAudioBlock);
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            blockDispatcher.Clear();
            RecordingStopped?.Invoke(this, e);
        }

        private void EmitAudioBlock(byte[] buffer)
        {
            DataAvailable?.Invoke(this, new ConsoleAudioDataAvailableEventArgs(buffer));
        }
    }

    internal sealed class FixedPcmBlockDispatcher
    {
        private readonly object sync = new object();
        private readonly byte[] pendingBuffer;
        private int pendingBytes;

        public FixedPcmBlockDispatcher(int blockSize)
        {
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize));

            pendingBuffer = new byte[blockSize];
        }

        public void AddSamples(byte[] buffer, int bytesRecorded, Action<byte[]> emitBlock)
        {
            if (buffer == null || bytesRecorded <= 0 || emitBlock == null)
                return;

            List<byte[]> readyBlocks = new List<byte[]>();
            lock (sync)
            {
                int offset = 0;
                int remaining = Math.Min(bytesRecorded, buffer.Length);
                while (remaining > 0)
                {
                    int bytesToCopy = Math.Min(pendingBuffer.Length - pendingBytes, remaining);
                    Buffer.BlockCopy(buffer, offset, pendingBuffer, pendingBytes, bytesToCopy);
                    pendingBytes += bytesToCopy;
                    offset += bytesToCopy;
                    remaining -= bytesToCopy;

                    if (pendingBytes != pendingBuffer.Length)
                        continue;

                    byte[] block = new byte[pendingBuffer.Length];
                    Buffer.BlockCopy(pendingBuffer, 0, block, 0, pendingBuffer.Length);
                    pendingBytes = 0;
                    readyBlocks.Add(block);
                }
            }

            foreach (byte[] block in readyBlocks)
                emitBlock(block);
        }

        public void Clear()
        {
            lock (sync)
                pendingBytes = 0;
        }
    }

    internal sealed class Pcm16Mono8kConverter
    {
        private const int TargetSampleRate = 8000;
        private readonly WaveFormat sourceFormat;
        private readonly List<float> pendingInputSamples = new List<float>();
        private double nextOutputSourceIndex;

        public Pcm16Mono8kConverter(WaveFormat sourceFormat)
        {
            this.sourceFormat = NormalizeSourceFormat(sourceFormat ?? throw new ArgumentNullException(nameof(sourceFormat)));
        }

        public byte[] Convert(byte[] buffer, int bytesRecorded)
        {
            if (buffer == null || bytesRecorded <= 0 || sourceFormat.SampleRate <= 0 || sourceFormat.Channels <= 0)
                return Array.Empty<byte>();

            AppendMonoSamples(buffer, bytesRecorded);
            if (pendingInputSamples.Count < 2)
                return Array.Empty<byte>();

            double sourceFramesPerOutputFrame = sourceFormat.SampleRate / (double)TargetSampleRate;
            List<short> outputSamples = new List<short>();

            while (nextOutputSourceIndex + 1 < pendingInputSamples.Count)
            {
                int sampleIndex = (int)nextOutputSourceIndex;
                double fraction = nextOutputSourceIndex - sampleIndex;
                float first = pendingInputSamples[sampleIndex];
                float second = pendingInputSamples[sampleIndex + 1];
                float sample = first + (float)((second - first) * fraction);
                outputSamples.Add(FloatToPcm16(sample));
                nextOutputSourceIndex += sourceFramesPerOutputFrame;
            }

            int consumedSamples = (int)Math.Floor(nextOutputSourceIndex);
            if (consumedSamples > 0)
            {
                pendingInputSamples.RemoveRange(0, Math.Min(consumedSamples, pendingInputSamples.Count));
                nextOutputSourceIndex -= consumedSamples;
            }

            byte[] output = new byte[outputSamples.Count * sizeof(short)];
            for (int i = 0; i < outputSamples.Count; i++)
            {
                short sample = outputSamples[i];
                output[i * 2] = (byte)(sample & 0xFF);
                output[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return output;
        }

        private void AppendMonoSamples(byte[] buffer, int bytesRecorded)
        {
            int blockAlign = Math.Max(1, sourceFormat.BlockAlign);
            int channels = Math.Max(1, sourceFormat.Channels);
            int bytesPerSample = Math.Max(1, sourceFormat.BitsPerSample / 8);
            int frameCount = bytesRecorded / blockAlign;

            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0;
                int sampleCount = 0;

                for (int channel = 0; channel < channels; channel++)
                {
                    int sampleOffset = (frame * blockAlign) + (channel * bytesPerSample);
                    if (sampleOffset + bytesPerSample > bytesRecorded)
                        continue;

                    sum += ReadSample(buffer, sampleOffset, bytesPerSample);
                    sampleCount++;
                }

                if (sampleCount > 0)
                    pendingInputSamples.Add(sum / sampleCount);
            }
        }

        private float ReadSample(byte[] buffer, int offset, int bytesPerSample)
        {
            if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                return ClampSample(BitConverter.ToSingle(buffer, offset));

            if (sourceFormat.Encoding != WaveFormatEncoding.Pcm)
                return 0;

            return bytesPerSample switch
            {
                1 => ((buffer[offset] - 128) / 128f),
                2 => BitConverter.ToInt16(buffer, offset) / 32768f,
                3 => ReadPcm24(buffer, offset) / 8388608f,
                4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => 0
            };
        }

        private static int ReadPcm24(byte[] buffer, int offset)
        {
            int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0)
                value |= unchecked((int)0xFF000000);

            return value;
        }

        private static short FloatToPcm16(float sample)
        {
            float clamped = ClampSample(sample);
            return (short)Math.Round(clamped * short.MaxValue);
        }

        private static float ClampSample(float sample)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample))
                return 0;

            return Math.Clamp(sample, -1.0f, 1.0f);
        }

        private static WaveFormat NormalizeSourceFormat(WaveFormat sourceFormat)
        {
            if (sourceFormat is WaveFormatExtensible extensible)
            {
                try
                {
                    return extensible.ToStandardWaveFormat();
                }
                catch
                {
                    // Fall through and let the sample reader handle the original format if possible.
                }
            }

            return sourceFormat;
        }
    }
}
