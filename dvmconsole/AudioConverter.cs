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

using System.Diagnostics;

namespace dvmconsole
{
    /// <summary>
    /// Helper to convert audio between different chunk sizes
    /// </summary>
    public static class AudioConverter
    {
        public const int OriginalPcmLength = 1600;
        public const int ExpectedPcmLength = 320;

        /// <summary>
        /// Helper to go from a big chunk size to smaller
        /// </summary>
        /// <param name="audioData"></param>
        /// <returns></returns>
        public static List<byte[]> SplitToChunks(byte[] audioData, int origLen = OriginalPcmLength, int expectedLength = ExpectedPcmLength)
        {
            List<byte[]> chunks = new List<byte[]>();

            if (audioData.Length != origLen)
            {
                Log.WriteLine($"Invalid PCM length: {audioData.Length}, expected: {origLen}");
                return chunks;
            }

            for (int offset = 0; offset < origLen; offset += expectedLength)
            {
                byte[] chunk = new byte[ExpectedPcmLength];
                Buffer.BlockCopy(audioData, offset, chunk, 0, expectedLength);
                chunks.Add(chunk);
            }

            return chunks;
        }

        /// <summary>
        /// Helper to go from small chunks to a big chunk
        /// </summary>
        /// <param name="chunks"></param>
        /// <returns></returns>
        public static byte[] CombineChunks(List<byte[]> chunks, int origLen = OriginalPcmLength, int expectedLength = ExpectedPcmLength)
        {
            if (chunks.Count * expectedLength != origLen)
            {
                Log.WriteLine($"Invalid number of chunks: {chunks.Count}, expected total length: {origLen}");
                return null;
            }

            byte[] combined = new byte[origLen];
            int offset = 0;

            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, combined, offset, expectedLength);
                offset += expectedLength;
            }

            return combined;
        }

        /// <summary>
        /// From https://github.com/W3AXL/rc2-dvm/blob/main/rc2-dvm/Audio.cs
        /// </summary>
        /// <param name="pcm16"></param>
        /// <returns></returns>
        public static float[] PcmToFloat(short[] pcm16)
        {
            float[] floats = new float[pcm16.Length];
            for (int i = 0; i < pcm16.Length; i++)
            {
                float v = (float)pcm16[i] / (float)short.MaxValue;
                if (v > 1) { v = 1; }
                if (v < -1) { v = -1; }
                floats[i] = v;
            }
            return floats;
        }
    } // public static class AudioConverter
} // namespace dvmconsole
