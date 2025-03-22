// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Patrick McDonnell, W3AXL
*
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using fnecore;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public enum MBE_MODE
    {
        DMR_AMBE,    //! DMR AMBE
        IMBE_88BIT,  //! 88-bit IMBE (P25)
    } // public enum MBE_MODE

    /// <summary>
    /// Wrapper class for the C++ dvmvocoder encoder library.
    /// </summary>
    /// Using info from https://stackoverflow.com/a/315064/1842613
    public class MBEEncoder
    {
        private IntPtr encoder;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="MBEEncoder"/> class.
        /// </summary>
        /// <param name="mode">Vocoder Mode (DMR or P25)</param>
        public MBEEncoder(MBE_MODE mode)
        {
            encoder = MBEEncoder_Create(mode);
        }

        /// <summary>
        /// Finalizes a instance of the <see cref="MBEEncoder"/> class.
        /// </summary>
        ~MBEEncoder()
        {
            MBEEncoder_Delete(encoder);
        }

        /// <summary>
        /// Create a new MBEEncoder
        /// </summary>
        /// <returns></returns>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr MBEEncoder_Create(MBE_MODE mode);

        /// <summary>
        /// Encode PCM16 samples to MBE codeword
        /// </summary>
        /// <param name="samples">Input PCM samples</param>
        /// <param name="codeword">Output MBE codeword</param>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MBEEncoder_Encode(IntPtr pEncoder, [In] Int16[] samples, [Out] byte[] codeword);

        /// <summary>
        /// Encode MBE to bits
        /// </summary>
        /// <param name="pEncoder"></param>
        /// <param name="bits"></param>
        /// <param name="codeword"></param>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MBEEncoder_EncodeBits(IntPtr pEncoder, [In] char[] bits, [Out] byte[] codeword);

        /// <summary>
        /// Delete a created MBEEncoder
        /// </summary>
        /// <param name="pEncoder"></param>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MBEEncoder_Delete(IntPtr pEncoder);

        /// <summary>
        /// Encode PCM16 samples to MBE codeword
        /// </summary>
        /// <param name="samples"></param>
        /// <param name="codeword"></param>
        public void encode([In] Int16[] samples, [Out] byte[] codeword)
        {
            MBEEncoder_Encode(encoder, samples, codeword);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="codeword"></param>
        public void encodeBits([In] char[] bits, [Out] byte[] codeword)
        {
            MBEEncoder_EncodeBits(encoder, bits, codeword);
        }
    } // public class MBEEncoder

    /// <summary>
    /// Wrapper class for the C++ dvmvocoder decoder library.
    /// </summary>
    public class MBEDecoder
    {
        private IntPtr decoder;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="MBEDecoder"/> class.
        /// </summary>
        /// <param name="mode">Vocoder Mode (DMR or P25)</param>
        public MBEDecoder(MBE_MODE mode)
        {
            decoder = MBEDecoder_Create(mode);
        }

        /// <summary>
        /// Finalizes a instance of the <see cref="MBEDecoder"/> class.
        /// </summary>
        ~MBEDecoder()
        {
            MBEDecoder_Delete(decoder);
        }

        /// <summary>
        /// Create a new MBEDecoder
        /// </summary>
        /// <returns></returns>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr MBEDecoder_Create(MBE_MODE mode);

        /// <summary>
        /// Decode MBE codeword to samples
        /// </summary>
        /// <param name="samples">Input PCM samples</param>
        /// <param name="codeword">Output MBE codeword</param>
        /// <returns>Number of decode errors</returns>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 MBEDecoder_Decode(IntPtr pDecoder, [In] byte[] codeword, [Out] Int16[] samples);

        /// <summary>
        /// Decode MBE to bits
        /// </summary>
        /// <param name="pDecoder"></param>
        /// <param name="codeword"></param>
        /// <param name="mbeBits"></param>
        /// <returns></returns>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 MBEDecoder_DecodeBits(IntPtr pDecoder, [In] byte[] codeword, [Out] char[] bits);

        /// <summary>
        /// Delete a created MBEDecoder
        /// </summary>
        /// <param name="pDecoder"></param>
        [DllImport("libvocoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MBEDecoder_Delete(IntPtr pDecoder);

        /// <summary>
        /// Decode MBE codeword to PCM16 samples
        /// </summary>
        /// <param name="samples"></param>
        /// <param name="codeword"></param>
        public Int32 decode([In] byte[] codeword, [Out] Int16[] samples)
        {
            return MBEDecoder_Decode(decoder, codeword, samples);
        }

        /// <summary>
        /// Decode MBE codeword to bits
        /// </summary>
        /// <param name="codeword"></param>
        /// <param name="bits"></param>
        /// <returns></returns>
        public Int32 decodeBits([In] byte[] codeword, [Out] char[] bits)
        {
            return MBEDecoder_DecodeBits(decoder, codeword, bits);
        }
    } // public class MBEDecoder

    /// <summary>
    /// 
    /// </summary>
    public static class MBEToneGenerator
    {
        /// <summary>
        /// Encodes a single tone to an AMBE tone frame
        /// </summary>
        /// <param name="tone_freq_hz"></param>
        /// <param name="tone_amplitude"></param>
        /// <param name="codeword"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void AMBEEncodeSingleTone(int tone_freq_hz, char tone_amplitude, [Out] byte[] codeword)
        {
            // U bit vectors
            // u0 and u1 are 12 bits
            // u2 is 11 bits
            // u3 is 14 bits
            // total length is 49 bits
            ushort[] u = new ushort[4];

            // Convert the tone frequency to the nearest tone index
            uint tone_idx = (uint)((float)tone_freq_hz / 31.25f);

            // Validate tone index
            if (tone_idx < 5 || tone_idx > 122)
                throw new ArgumentOutOfRangeException($"Tone index for frequency out of range!");

            // Validate amplitude value
            if (tone_amplitude > 127)
                throw new ArgumentOutOfRangeException("Tone amplitude must be between 0 and 127!");

            // Make sure tone index only has 7 bits (it should but we make sure :) )
            tone_idx &= 0b01111111;

            // Encode u vectors per TIA-102.BABA-1 section 7.2

            // u0[11-6] are always 1 to indicate a tone, so we left-shift 63u (0x00111111) a full byte (8 bits)
            u[0] |= (ushort)(63 << 8);

            // u0[5-0] are AD (tone amplitude byte) bits 6-1
            u[0] |= (ushort)(tone_amplitude >> 1);

            // u1[11-4] are tone index bits 7-0 (the full byte)
            u[1] |= (ushort)(tone_idx << 4);

            // u1[3-0] are tone index bits 7-4
            u[1] |= (ushort)(tone_idx >> 4);

            // u2[10-7] are tone index bits 3-0
            u[2] |= (ushort)((tone_idx & 0b00001111) << 7);

            // u2[6-0] are tone index bits 7-1
            u[2] |= (ushort)(tone_idx >> 1);

            // u3[13] is the last bit of the tone index
            u[3] |= (ushort)((tone_idx & 0b1) << 13);

            // u3[12-5] is the full tone index byte
            u[3] |= (ushort)(tone_idx << 5);

            // u3[4] is the last bit of the amplitude byte
            u[3] |= (ushort)((tone_amplitude & 0b1) << 4);

            // u3[3-0] is always 0 so we don't have to do anything here

            // Convert u buffer to byte
            Buffer.BlockCopy(u, 0, codeword, 0, 8);
        }

        /// <summary>
        /// Encode a single tone to an IMBE codeword sequence using a lookup table
        /// </summary>
        /// <param name="tone_freq_hz"></param>
        /// <param name="codeword"></param>
        public static void IMBEEncodeSingleTone(ushort tone_freq_hz, [Out] byte[] codeword)
        {
            // Find nearest tone in the lookup table
            List<ushort> tone_keys = VocoderToneLookupTable.IMBEToneFrames.Keys.ToList();
            ushort nearest = tone_keys.Aggregate((x, y) => Math.Abs(x - tone_freq_hz) < Math.Abs(y - tone_freq_hz) ? x : y);
            byte[] tone_codeword = VocoderToneLookupTable.IMBEToneFrames[nearest];
            Array.Copy(tone_codeword, codeword, tone_codeword.Length);
        }
    } // public static class MBEToneGenerator

    /// <summary>
    /// 
    /// </summary>
    public class MBEInterleaver
    {
        public const int PCM_SAMPLES = 160;
        public const int AMBE_CODEWORD_SAMPLES = 9;
        public const int AMBE_CODEWORD_BITS = 49;
        public const int IMBE_CODEWORD_SAMPLES = 11;
        public const int IMBE_CODEWORD_BITS = 88;

        private MBE_MODE mode;

        private MBEEncoder encoder;
        private MBEDecoder decoder;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="MBEInterleaver"/> class.
        /// </summary>
        /// <param name="mode"></param>
        public MBEInterleaver(MBE_MODE mode)
        {
            this.mode = mode;
            encoder = new MBEEncoder(this.mode);
            decoder = new MBEDecoder(this.mode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="codeword"></param>
        /// <param name="mbeBits"></param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int Decode([In] byte[] codeword, [Out] byte[] mbeBits)
        {
            // Input validation
            if (codeword == null)
                throw new NullReferenceException("Input MBE codeword is null!");

            char[] bits = null;
            int bitCount = 0;

            // Set up based on mode
            if (mode == MBE_MODE.DMR_AMBE)
            {
                if (codeword.Length != AMBE_CODEWORD_SAMPLES)
                    throw new ArgumentOutOfRangeException($"AMBE codeword length is != {AMBE_CODEWORD_SAMPLES}");
                bitCount = AMBE_CODEWORD_BITS;
                bits = new char[bitCount];
            }
            else if (mode == MBE_MODE.IMBE_88BIT)
            {
                if (codeword.Length != IMBE_CODEWORD_SAMPLES)
                    throw new ArgumentOutOfRangeException($"IMBE codeword length is != {IMBE_CODEWORD_SAMPLES}");
                bitCount = IMBE_CODEWORD_BITS;
                bits = new char[bitCount];
            }

            if (bits == null)
                throw new NullReferenceException("Failed to initialize decoder");

            // Decode
            int errs = decoder.decodeBits(codeword, bits);

            // Copy
            for (int i = 0; i < bitCount; i++)
                mbeBits[i] = (byte)(bits[i] & 0x01);

            return errs;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mbeBits"></param>
        /// <param name="codeword"></param>
        /// <exception cref="NullReferenceException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void Encode([In] byte[] mbeBits, [Out] byte[] codeword)
        {
            if (mbeBits == null)
            {
                throw new NullReferenceException("Input MBE bit array is null!");
            }

            char[] bits = null;

            // Set up based on mode
            if (mode == MBE_MODE.DMR_AMBE)
            {
                if (mbeBits.Length != AMBE_CODEWORD_BITS)
                {
                    throw new ArgumentOutOfRangeException($"AMBE codeword bit length is != {AMBE_CODEWORD_BITS}");
                }
                bits = new char[AMBE_CODEWORD_BITS];
                for (int i = 0; i < mbeBits.Length; i++)
                    bits[i] = (char)(mbeBits[i] & 0x01);
            }
            else if (mode == MBE_MODE.IMBE_88BIT)
            {
                if (mbeBits.Length != IMBE_CODEWORD_BITS)
                {
                    throw new ArgumentOutOfRangeException($"IMBE codeword bit length is != {IMBE_CODEWORD_BITS}");
                }
                bits = new char[IMBE_CODEWORD_BITS];
                for (int i = 0; i < mbeBits.Length; i++)
                    bits[i] = (char)(mbeBits[i] & 0x01);
            }

            if (bits == null)
            {
                throw new ArgumentException("Bit array did not get set up properly!");
            }

            // Encode samples
            if (mode == MBE_MODE.DMR_AMBE)
            {
                // Create output array
                byte[] codewords = new byte[AMBE_CODEWORD_SAMPLES];
                // Encode
                encoder.encodeBits(bits, codewords);
                // Copy
                for (int i = 0; i < AMBE_CODEWORD_SAMPLES; i++)
                    codeword[i] = codewords[i];
            }
            else if (mode == MBE_MODE.IMBE_88BIT)
            {
                // Create output array
                byte[] codewords = new byte[IMBE_CODEWORD_SAMPLES];
                // Encode
                encoder.encodeBits(bits, codewords);
                // Copy
                for (int i = 0; i < IMBE_CODEWORD_SAMPLES; i++)
                    codeword[i] = codewords[i];
            }
        }
    } // public class MBEInterleaver
} // namespace dvmconsole
