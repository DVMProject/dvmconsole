// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using fnecore.DMR;

namespace dvmconsole.DMR;

internal sealed class DmrAmbeCodec : IDmrAmbeCodec
{
    private readonly MBEInterleaver interleaver = new(MBE_MODE.DMR_AMBE);

    public int Decode(byte[] codeword, byte[] parameters) => interleaver.Decode(codeword, parameters);
    public void Encode(byte[] parameters, byte[] codeword) => interleaver.Encode(parameters, codeword);
}
