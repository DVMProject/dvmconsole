# Encryption Keys

Encryption keys allow the console to decrypt and transmit encrypted voice traffic when supported by the connected FNE system.

The console can load local key material from a YAML key file referenced by the codeplug.

---

# FNE Compatibility

DVMConsole R02A00 is intended for use with DVMHost/FNE R06A00 or newer.

Older FNE builds are not recommended for encrypted console operation.

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

Supported P25 `algo` values include:

- `aes`
- `des`
- `arc4`
- `none`

For P25, if `keyId` is blank or zero, the channel is treated as clear for normal operation. DMR and NXDN encrypted channels instead block TX when the key configuration is invalid; they never silently fall back to clear.

NXDN supports `ehr` (15-bit scrambling), `des`, and `aes` (AES-256). Local NXDN entries require `protocol: nxdn` and use wire algorithm IDs 1, 2, and 3 rather than P25 algorithm IDs. See **NXDN** for key sizes, examples, and FNE key-service behavior.

DMR supports Association ARC4, DES-OFB, and AES-256. Local DMR entries require `protocol: dmr` and use wire algorithm IDs 1, 2, and 5. See **DMR** for key sizes, examples, and FNE key-service behavior.

---

# Selectable Encryption

P25, DMR, and NXDN secure-capable channels can expose an in-card encryption toggle:

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

Missing keys on selected encrypted resources are also requested again after an FNE reconnect. Keys must exist in the FNE crypto container, and the system's regular `rid` must be authorized to request each KID.

NXDN EHR, DES, and AES use this same service. Their request/container algorithm IDs are `0x01`, `0x81`, and `0x84`, respectively; see **NXDN** for details and key lengths. Use unique key IDs for distinct material across protocols in the FNE container.

DMR ARC4, DES, and AES also use this service with request/container algorithm IDs `0xAA`, `0x81`, and `0x84`, respectively. These differ from the DMR wire IDs stored in local key files; see **DMR** for details.

When the FNE enables encrypted key responses (`kmfEncKeyRequest`), configure the matching `kmfPresharedKey` under the console system. It must contain exactly 64 hexadecimal characters. This wrapping key is separate from the FNE transport `presharedKey`. Protect both keys and prefer encrypted FNE transport; unprotected key responses expose traffic keys on the network.

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
