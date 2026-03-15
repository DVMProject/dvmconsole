# Encryption Keys

Encryption keys allow the console to decrypt and transmit encrypted voice traffic when supported by the connected FNE system.

The console loads encryption keys from an external YAML key file referenced in the codeplug.

This file contains the key material used for encrypted talkgroups.

---

# Key File Location

Encryption keys are referenced from the console codeplug using the `keyFile` field.

Example:

```yaml
keyFile: "Full/Path/To/Keyfile.clear"
```

The specified file should contain encryption key definitions.

---

# Key File Structure

The encryption key file is a YAML document containing a list of keys.

Each key entry defines:

- `keyId`
- `algId`
- `key`

Example:

```yaml
keys:
  -
    keyId: 0x1
    algId: 0x84
    key: "1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890ABCDEFGHIJKLMNOPQR"
  -
    keyId: 0x2
    algId: 0xAA
    key: "1234567890"
```

---

# Field Descriptions

### keyId

The key ID associated with the encryption key.

This value is referenced by channels in the codeplug.

Example:

```yaml
keyId: 0x1
```

Key IDs are typically written in hexadecimal format.

---

### algId

The encryption algorithm identifier.

Example:

```yaml
algId: 0x84
```

The algorithm ID determines how the key material is interpreted.

Typical values depend on the encryption type used by the system.

---

### key

The encryption key material.

Example:

```yaml
key: "1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890ABCDEFGHIJKLMNOPQR"
```

---

# Using Keys in a Codeplug

Once defined in the key file, keys can be referenced by channels using the `keyId` field.

Example channel configuration:

```yaml
- name: "Encrypted Channel"
  system: "System 1"
  tgid: "2001"
  keyId: 0x1
  algo: "aes"
```

When traffic is transmitted or received on this channel, the console will use the corresponding key.

---

# Notes

- The `encryptionKey` channel field is reserved for future functionality.
- If `keyId` is omitted or set to `0`, the channel is treated as clear (unencrypted).

---

# Security Considerations

Encryption keys provide access to protected radio traffic.

You should:

- Store key files securely
- Limit access to trusted administrators
- Avoid committing key files to public repositories
- Rotate keys periodically when required

---

# Troubleshooting

### Encrypted traffic cannot be decrypted

Check the following:

- The key file path is correct in the codeplug
- The correct `keyId` is configured on the channel
- The `algId` matches the system encryption algorithm
- The key material is valid

### Encrypted transmit fails

Ensure:

- The channel has a valid `keyId`
- The correct `algo` is defined