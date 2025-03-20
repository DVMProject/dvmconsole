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

using System.Security.Cryptography;

using fnecore.P25;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class P25Crypto
    {
        public const int IMBE_BUF_LEN = 11;

        private byte algId;
        private ushort keyId;

        private byte[] messageIndicator = new byte[9];

        private Dictionary<ushort, KeyInfo> keys = new Dictionary<ushort, KeyInfo>();

        private byte[] aesKeystream = new byte[240]; // AES buffer
        private byte[] adpKeystream = new byte[469]; // ADP buffer

        private int ksPosition;

        /*
        ** Class
        */

        /// <summary>
        /// 
        /// </summary>
        private class KeyInfo
        {
            /*
            ** Properties
            */

            /// <summary>
            /// 
            /// </summary>
            public byte AlgId { get; }
            /// <summary>
            /// 
            /// </summary>
            public byte[] Key { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="KeyInfo"/> class.
            /// </summary>
            /// <param name="algid"></param>
            /// <param name="key"></param>
            public KeyInfo(byte algid, byte[] key)
            {
                AlgId = algid;
                Key = key;
            }
        } // private class KeyInfo

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="P25Crypto"/> class.
        /// </summary>
        public P25Crypto()
        {
            this.algId = P25Defines.P25_ALGO_UNENCRYPT;
            this.keyId = 0;

            this.ksPosition = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Reset()
        {
            keys.Clear();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyid"></param>
        /// <param name="algid"></param>
        /// <param name="key"></param>
        public void AddKey(ushort keyid, byte algid, byte[] key)
        {
            if (keyid == 0 || algid == 0x80)
                return;

            keys[keyid] = new KeyInfo(algid, key);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyId"></param>
        /// <returns></returns>
        public bool HasKey(ushort keyId)
        {
            return keys.ContainsKey(keyId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="algid"></param>
        /// <param name="keyid"></param>
        /// <param name="protocol"></param>
        /// <param name="MI"></param>
        /// <returns></returns>
        public bool Prepare(byte algid, ushort keyid, byte[] MI)
        {
            this.algId = algid;
            this.keyId = keyid;

            Array.Copy(MI, this.messageIndicator, Math.Min(MI.Length, this.messageIndicator.Length));

            if (!keys.ContainsKey(keyid))
                return false;

            this.ksPosition = 0;

            if (algid == P25Defines.P25_ALGO_AES)
            {
                GenerateAESKeystream();
                return true;
            }
            else if (algid == P25Defines.P25_ALGO_ARC4)
            {
                GenerateARC4Keystream();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imbe"></param>
        /// <param name="duid"></param>
        /// <returns></returns>
        public bool Process(byte[] imbe, P25DUID duid)
        {
            if (!keys.ContainsKey(keyId))
                return false;

            return algId switch
            {
                P25Defines.P25_ALGO_AES => AESProcess(imbe, duid),
                P25Defines.P25_ALGO_ARC4 => ARC4Process(imbe, duid),
                _ => false
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="i1"></param>
        /// <param name="i2"></param>
        private void Swap(byte[] a, int i1, int i2)
        {
            byte temp = a[i1];
            a[i1] = a[i2];
            a[i2] = temp;
        }

        /// <summary>
        /// Create ARC4 keystream.
        /// </summary>
        private void GenerateARC4Keystream()
        {
            byte[] adpKey = new byte[13];
            byte[] permutation = new byte[256];
            byte[] key = new byte[256];

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

            // generate ARC4 keystream
            // initialize state variable
            for (i = 0; i < 256; ++i)
            {
                key[i] = adpKey[i % 13];
                permutation[i] = (byte)i;
            }

            // randomize, using key
            for (i = 0; i < 256; ++i)
            {
                j = (j + permutation[i] + key[i]) & 0xFF;
                Swap(permutation, i, j);
            }

            // perform RC4 transformation
            i = j = 0;
            for (k = 0; k < 469; ++k)
            {
                i = (i + 1) & 0xFF;
                j = (j + permutation[i]) & 0xFF;

                // swap permutation[i] and permutation[j]
                Swap(permutation, i, j);

                // transform byte
                adpKeystream[k] = permutation[(permutation[i] + permutation[j]) & 0xFF];
            }
        }

        /// <summary>
        /// Create AES keystream.
        /// </summary>
        private void GenerateAESKeystream()
        {
            if (!keys.ContainsKey(keyId))
                return;

            byte[] key = keys[keyId].Key;
            byte[] iv = ExpandMIToIV(messageIndicator);

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
        /// Helper to process IMBE audio using AES-256.
        /// </summary>
        /// <param name="imbe"></param>
        /// <param name="duid"></param>
        /// <returns></returns>
        private bool AESProcess(byte[] imbe, P25DUID duid)
        {
            int offset = 16;
            if (duid == P25DUID.LDU2)
                offset += 101;

            offset += (ksPosition * IMBE_BUF_LEN) + IMBE_BUF_LEN + (ksPosition < 8 ? 0 : 2);
            ksPosition = (ksPosition + 1) % 9;

            for (int j = 0; j < IMBE_BUF_LEN; ++j)
                imbe[j] ^= aesKeystream[j + offset];

            return true;
        }

        /// <summary>
        /// Helper to process IMBE audio using ARC4.
        /// </summary>
        /// <param name="imbe"></param>
        /// <param name="duid"></param>
        /// <returns></returns>
        private bool ARC4Process(byte[] imbe, P25DUID duid)
        {
            int offset = 256;

            if (duid == P25DUID.LDU1)
                offset = 0;
            else if (duid == P25DUID.LDU2)
                offset = 101;

            offset += (ksPosition * IMBE_BUF_LEN) + 267 + (ksPosition < 8 ? 0 : 2);
            ksPosition = (ksPosition + 1) % 9;

            for (int j = 0; j < IMBE_BUF_LEN; ++j)
                imbe[j] ^= adpKeystream[j + offset];

            return true;
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
        private static byte[] ExpandMIToIV(byte[] mi)
        {
            if (mi == null || mi.Length < 8)
                throw new ArgumentException("MI must be at least 8 bytes long.");

            byte[] iv = new byte[16];

            // Copy first 64 bits of MI into LFSR
            ulong lfsr = 0;
            for (int i = 0; i < 8; i++)
                lfsr = (lfsr << 8) | mi[i];

            // Use LFSR routine to compute the expansion
            ulong overflow = 0;
            for (int i = 0; i < 64; i++)
                overflow = (overflow << 1) | StepP25Lfsr(ref lfsr);

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
    } // public class P25Crypto
} // namespace dvmconsole
