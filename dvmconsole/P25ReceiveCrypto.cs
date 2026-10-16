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

using fnecore.P25;

namespace dvmconsole
{
    /// <summary>
    /// Receive-only cipher state; never reuses the channel's transmit key.
    /// </summary>
    internal sealed class P25ReceiveCrypto
    {
        private readonly P25Crypto crypto = new();
        private bool ready;
        private bool encrypted;

        public void Reset()
        {
            ready = false;
            encrypted = false;
            crypto.Reset();
        }

        public void Prepare(byte algorithm, ushort id, byte[] indicator, byte[] key)
        {
            Reset();
            encrypted = algorithm != P25Defines.P25_ALGO_UNENCRYPT;
            if (!encrypted)
            {
                ready = true;
                return;
            }
            if (key == null)
                return;
            crypto.SetKey(id, algorithm, key);
            ready = crypto.Prepare(algorithm, id, indicator);
        }

        public bool Process(byte[] voice, P25DUID duid) => ready && (!encrypted || crypto.Process(voice, duid));
    }
}
