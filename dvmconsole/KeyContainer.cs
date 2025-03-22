// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*
*/

namespace dvmconsole;

/// <summary>
/// POCO which is used to decode a YML keyfile
/// </summary>
public class KeyContainer
{
    public List<KeyEntry> Keys { get; set; } = [];
}

public class KeyEntry
{
    public ushort KeyId { get; set; }
    public int AlgId { get; set; }
    public string Key { get; set; }

    /// <summary>
    /// Gets the contents of the Key property as a byte[]
    /// </summary>
    public byte[] KeyBytes => string.IsNullOrEmpty(Key) ? [] : StringToByteArray(Key);
    
    private static byte[] StringToByteArray(string hex) {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }
}