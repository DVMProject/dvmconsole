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