# Encryption Keys

Encryption keys allow the console to decrypt and transmit encrypted voice traffic when supported by the connected FNE system.

The console can load local key material from a YAML key file referenced by the codeplug.

---

# Key File Location

Reference the key file with `keyFile` in the codeplug:

```yaml
keyFile: "Full/Path/To/Keyfile.clear"
```

---

# Key File Format

The key file contains a `keys` list.

Example:

```yaml
keys:
  - keyId: 0x1
    algId: 0x84
    key: "1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890ABCDEFGHIJKLMNOPQR"

  - keyId: 0x2
    algId: 0xAA
    key: "1234567890"
```

Fields:

- `keyId`: key ID referenced by channels.
- `algId`: algorithm ID.
- `key`: key material.

---

# Channel Encryption Fields

Encrypted channels can include:

```yaml
keyId: 0x50
algo: "aes"
```

Supported `algo` values include:

- `aes`
- `des`
- `arc4`
- `none`

If `keyId` is blank or zero, the channel is treated as clear for normal operation.

---

# Selectable Encryption

P25 secure-capable channels can expose an in-card encryption toggle:

```yaml
keyId: 0x50
algo: "aes"
selectable_encryption: true
```

When enabled, the resource card shows **SELECT** next to the TAR indicator area. Clicking **SELECT** toggles console transmit between encrypted and clear for that system/talkgroup.

The selected encrypted/clear state is saved and restored across restarts. The key and algorithm still come from the codeplug; the toggle only controls whether the console uses them for transmit.

---

# FNE Key Requests

When **Restore Selected Channels On Startup** is enabled, selected encrypted channels may need to request keys after startup.

The console waits for the relevant FNE connection to complete before sending startup key requests. It then waits a short post-connect delay and spaces multiple key requests apart so the FNE is not flooded.

This startup delay applies to restored selected encrypted resources. Normal key behavior outside startup remains unchanged.

---

# Key Status

Use the key status toolbar button to inspect loaded or received key state for configured encrypted resources.

If an encrypted channel does not decrypt correctly:

- verify the channel `keyId`
- verify the channel `algo`
- verify the key file path
- verify that the FNE is connected
- verify that the FNE has delivered required key material

---

# Safety Notes

- Protect clear key files.
- Do not commit operational key material to source control.
- Use test keys for development environments.
