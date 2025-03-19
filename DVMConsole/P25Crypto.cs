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

// TODO: Move to fnecore

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;

namespace DVMConsole
{
    public class P25Crypto
    {
        private ProtocolType protocol;
        private byte algId;
        private ushort keyId;
        private byte[] messageIndicator = new byte[9];
        private Dictionary<ushort, KeyInfo> keys = new Dictionary<ushort, KeyInfo>();
        private byte[] aesKeystream = new byte[240]; // AES buffer
        private byte[] adpKeystream = new byte[469]; // ADP buffer
        private int aesPosition;
        private int adpPosition;

        public P25Crypto()
        {
            this.protocol = ProtocolType.Unknown;
            this.algId = 0x80;
            this.keyId = 0;
            this.aesPosition = 0;
            this.adpPosition = 0;
        }

        public void Reset()
        {
            keys.Clear();
        }

        public void AddKey(ushort keyid, byte algid, byte[] key)
        {
            if (keyid == 0 || algid == 0x80)
                return;

            keys[keyid] = new KeyInfo(algid, key);
        }

        public bool HasKey(ushort keyId)
        {
            return keys.ContainsKey(keyId);
        }

        public bool Prepare(byte algid, ushort keyid, ProtocolType protocol, byte[] MI)
        {
            this.algId = algid;
            this.keyId = keyid;
            this.protocol = protocol;
            Array.Copy(MI, this.messageIndicator, Math.Min(MI.Length, this.messageIndicator.Length));

            if (!keys.ContainsKey(keyid))
                return false;

            if (algid == 0x84) // AES-256
            {
                this.aesPosition = 0;
                GenerateAesKeystream();
                return true;
            }
            else if (algid == 0xAA) // ADP (RC4)
            {
                this.adpPosition = 0;
                GenerateAdpKeystream();
                return true;
            }

            return false;
        }

        public bool Process(byte[] PCW, FrameType frameType, int voiceSubframe)
        {
            if (!keys.ContainsKey(keyId))
                return false;

            return algId switch
            {
                0x84 => AesProcess(PCW, frameType, voiceSubframe),
                0xAA => AdpProcess(PCW, frameType, voiceSubframe),
                _ => false
            };
        }

        /// <summary>
        /// Create ADP key stream
        /// </summary>
        private void GenerateAdpKeystream()
        {
            byte[] adpKey = new byte[13];
            byte[] S = new byte[256];
            byte[] K = new byte[256];

            if (!keys.ContainsKey(keyId))
                return;

            byte[] keyData = keys[keyId].Key;

            int keySize = keyData.Length;
            int padding = Math.Max(5 - keySize, 0);
            int i, j = 0, k;

            for (i = 0; i < padding; i++)
                adpKey[i] = 0;

            for (; i < 5; i++)
                adpKey[i] = keySize > 0 ? keyData[i - padding] : (byte)0;

            for (i = 5; i < 13; ++i)
            {
                adpKey[i] = messageIndicator[i - 5];
            }

            for (i = 0; i < 256; ++i)
            {
                K[i] = adpKey[i % 13];
                S[i] = (byte)i;
            }

            for (i = 0; i < 256; ++i)
            {
                j = (j + S[i] + K[i]) & 0xFF;
                Swap(S, i, j);
            }

            i = j = 0;
            for (k = 0; k < 469; ++k)
            {
                i = (i + 1) & 0xFF;
                j = (j + S[i]) & 0xFF;
                Swap(S, i, j);
                adpKeystream[k] = S[(S[i] + S[j]) & 0xFF];
            }
        }

        /// <summary>
        /// Preform a swap
        /// </summary>
        /// <param name="S"></param>
        /// <param name="i"></param>
        /// <param name="j"></param>
        private void Swap(byte[] S, int i, int j)
        {
            byte temp = S[i];
            S[i] = S[j];
            S[j] = temp;
        }

        /// <summary>
        /// Process AES256
        /// </summary>
        /// <param name="PCW"></param>
        /// <param name="frameType"></param>
        /// <param name="voiceSubframe"></param>
        /// <returns></returns>
        private bool AesProcess(byte[] PCW, FrameType frameType, int voiceSubframe)
        {
            int offset = 16;

            switch (frameType)
            {
                case FrameType.LDU1: offset += 0; break;
                case FrameType.LDU2: offset += 101; break;
                case FrameType.V4_0: offset += 7 * voiceSubframe; break;
                case FrameType.V4_1: offset += 7 * (voiceSubframe + 4); break;
                case FrameType.V4_2: offset += 7 * (voiceSubframe + 8); break;
                case FrameType.V4_3: offset += 7 * (voiceSubframe + 12); break;
                case FrameType.V2: offset += 7 * (voiceSubframe + 16); break;
                default: return false;
            }

            if (protocol == ProtocolType.P25Phase1)
            {
                offset += (aesPosition * 11) + 11 + (aesPosition < 8 ? 0 : 2);
                aesPosition = (aesPosition + 1) % 9;

                for (int j = 0; j < 11; ++j)
                {
                    PCW[j] ^= aesKeystream[j + offset];
                }
            }
            else if (protocol == ProtocolType.P25Phase2)
            {
                for (int j = 0; j < 7; ++j)
                {
                    PCW[j] ^= aesKeystream[j + offset];
                }
                PCW[6] &= 0x80;
            }

            return true;
        }

        /// <summary>
        /// Process ADP
        /// </summary>
        /// <param name="PCW"></param>
        /// <param name="frameType"></param>
        /// <param name="voiceSubframe"></param>
        /// <returns></returns>
        private bool AdpProcess(byte[] PCW, FrameType frameType, int voiceSubframe)
        {
            int offset = 256;

            switch (frameType)
            {
                case FrameType.LDU1: offset = 0; break;
                case FrameType.LDU2: offset = 101; break;
                case FrameType.V4_0: offset += 7 * voiceSubframe; break;
                case FrameType.V4_1: offset += 7 * (voiceSubframe + 4); break;
                case FrameType.V4_2: offset += 7 * (voiceSubframe + 8); break;
                case FrameType.V4_3: offset += 7 * (voiceSubframe + 12); break;
                case FrameType.V2: offset += 7 * (voiceSubframe + 16); break;
                default: return false;
            }

            if (protocol == ProtocolType.P25Phase1)
            {
                offset += (adpPosition * 11) + 267 + (adpPosition < 8 ? 0 : 2);
                adpPosition = (adpPosition + 1) % 9;

                for (int j = 0; j < 11; ++j)
                {
                    PCW[j] ^= adpKeystream[j + offset];
                }
            }
            else if (protocol == ProtocolType.P25Phase2)
            {
                for (int j = 0; j < 7; ++j)
                {
                    PCW[j] ^= adpKeystream[j + offset];
                }
                PCW[6] &= 0x80;
            }

            return true;
        }

        /// <summary>
        /// Create AES key stream
        /// </summary>
        private void GenerateAesKeystream()
        {
            if (!keys.ContainsKey(keyId))
                return;

            byte[] key = keys[keyId].Key;
            byte[] iv = ExpandMiTo128(messageIndicator);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = key.Length == 32 ? key : key.Concat(new byte[32 - key.Length]).ToArray();
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] input = new byte[16];
                    Array.Copy(iv, input, 16);
                    byte[] output = new byte[16];

                    for (int i = 0; i < aesKeystream.Length / 16; i++)
                    {
                        encryptor.TransformBlock(input, 0, 16, output, 0);
                        Buffer.BlockCopy(output, 0, aesKeystream, i * 16, 16);
                        Array.Copy(output, input, 16);
                    }
                }
            }
        }

        /// <summary>
        /// Cycle P25 LFSR
        /// </summary>
        /// <param name="MI"></param>
        /// <exception cref="ArgumentException"></exception>
        public static void CycleP25Lfsr(byte[] MI)
        {
            // TODO: use step LFSR
            if (MI == null || MI.Length < 9)
                throw new ArgumentException("MI must be at least 9 bytes long.");

            ulong lfsr = 0;

            // Load the first 8 bytes into the LFSR
            for (int i = 0; i < 8; i++)
            {
                lfsr = (lfsr << 8) | MI[i];
            }

            // Perform 64-bit LFSR cycling using the polynomial:
            // C(x) = x^64 + x^62 + x^46 + x^38 + x^27 + x^15 + 1
            for (int cnt = 0; cnt < 64; cnt++)
            {
                ulong bit = ((lfsr >> 63) ^ (lfsr >> 61) ^ (lfsr >> 45) ^ (lfsr >> 37) ^ (lfsr >> 26) ^ (lfsr >> 14)) & 0x1;
                lfsr = (lfsr << 1) | bit;
            }

            // Store the result back into MI
            for (int i = 7; i >= 0; i--)
            {
                MI[i] = (byte)(lfsr & 0xFF);
                lfsr >>= 8;
            }

            MI[8] = 0; // Last byte is always set to zero
        }

        /// <summary>
        /// Step LFSR
        /// </summary>
        /// <param name="lfsr"></param>
        /// <returns></returns>
        private static ulong StepP25Lfsr(ref ulong lfsr)
        {
            // Extract overflow bit (bit 63)
            ulong ovBit = (lfsr >> 63) & 0x1;

            // Compute feedback bit using polynomial: x^64 + x^62 + x^46 + x^38 + x^27 + x^15 + 1
            ulong fbBit = ((lfsr >> 63) ^ (lfsr >> 61) ^ (lfsr >> 45) ^ (lfsr >> 37) ^
                           (lfsr >> 26) ^ (lfsr >> 14)) & 0x1;

            // Shift LFSR left and insert feedback bit
            lfsr = (lfsr << 1) | fbBit;

            return ovBit;
        }

        /// <summary>
        /// Expland MI to 128 IV
        /// </summary>
        /// <param name="mi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static byte[] ExpandMiTo128(byte[] mi)
        {
            if (mi == null || mi.Length < 8)
                throw new ArgumentException("MI must be at least 8 bytes long.");

            byte[] iv = new byte[16];

            // Copy first 64 bits of MI into LFSR
            ulong lfsr = 0;
            for (int i = 0; i < 8; i++)
            {
                lfsr = (lfsr << 8) | mi[i];
            }

            // Use LFSR routine to compute the expansion
            ulong overflow = 0;
            for (int i = 0; i < 64; i++)
            {
                overflow = (overflow << 1) | StepP25Lfsr(ref lfsr);
            }

            // Copy expansion and LFSR to IV
            for (int i = 7; i >= 0; i--)
            {
                iv[i] = (byte)(overflow & 0xFF);
                overflow >>= 8;
            }
            for (int i = 15; i >= 8; i--)
            {
                iv[i] = (byte)(lfsr & 0xFF);
                lfsr >>= 8;
            }

            return iv;
        }


        private class KeyInfo
        {
            public byte AlgId { get; }
            public byte[] Key { get; }

            public KeyInfo(byte algid, byte[] key)
            {
                AlgId = algid;
                Key = key;
            }
        }

        public enum ProtocolType
        {
            Unknown = 0,
            P25Phase1,
            P25Phase2
        }

        public enum FrameType
        {
            Unknown = 0,
            LDU1,
            LDU2,
            V2,
            V4_0,
            V4_1,
            V4_2,
            V4_3
        }
    }
}
