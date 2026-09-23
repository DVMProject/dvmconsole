// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using fnecore.NXDN;

namespace dvmconsole.NXDN;

// DMR and NXDN share the individual 72-bit AMBE codeword layout.
internal sealed class NxdnAmbeCodec : INxdnAmbeCodec
{
    private readonly MBEEncoder encoder = new(MBE_MODE.DMR_AMBE);
    private readonly MBEDecoder decoder = new(MBE_MODE.DMR_AMBE);
    private readonly char[] decodeBits = new char[49];
    private readonly char[] encodeBits = new char[72];

    public int Decode(byte[] codeword, byte[] parameters)
    {
        if (codeword.Length != 9 || parameters.Length != 49)
            throw new ArgumentException("NXDN AMBE requires nine bytes and 49 parameters.");
        int errors = decoder.decodeBits(codeword, decodeBits);
        for (int i = 0; i < 49; i++) parameters[i] = (byte)(decodeBits[i] & 1);
        return errors;
    }

    public void Encode(byte[] parameters, byte[] codeword)
    {
        if (codeword.Length != 9 || parameters.Length != 49)
            throw new ArgumentException("NXDN AMBE requires nine bytes and 49 parameters.");
        // Native FEC reads 72 input positions; the unused tail stays zero.
        for (int i = 0; i < 49; i++) encodeBits[i] = (char)(parameters[i] & 1);
        encoder.encodeBits(encodeBits, codeword);
    }
}
