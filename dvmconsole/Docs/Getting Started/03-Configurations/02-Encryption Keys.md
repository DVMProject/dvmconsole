# Encryption Keys

Encryption keys allow the console to decrypt and transmit encrypted voice traffic when supported by the connected FNE system.

The console can load local traffic keys from an encrypted `.ekc` container created with [DVMProject KFDEKC](https://github.com/DVMProject/kfdekc). Clear YAML key files are no longer supported, including files renamed to `.ekc`.

---

# FNE Compatibility

DVMConsole R02A00 is intended for use with DVMHost/FNE R06A00 or newer.

Older FNE builds are not recommended for encrypted console operation.

---

# Key File Location

Reference the key file with `keyFile` in the codeplug:

```yaml
keyFile: 'Keys.ekc'
```

---

# Container Setup

Relative paths resolve beside the codeplug. On first use, Console asks for the container password in a masked dialog. The password is saved only as a Windows DPAPI-protected blob for the current Windows account, under `EkcCredentials` in the Console AppData profile. It is not stored in the codeplug or `UserSettings.json`.

If the password changes or Windows cannot open the saved credential, Console asks again. Cancel continues without local keys; FNE key requests remain available. Reload the codeplug to retry or to load edits made in KFDEKC. To forget saved passwords, close Console and remove the relevant profile's `EkcCredentials` folder.

Create traffic encryption keys (TEKs), matching the channel algorithm and key ID:

| Use | EKC algorithm | Key length |
| --- | --- | --- |
| P25 / NXDN DES | `0x81` | 8 bytes |
| P25 / NXDN AES-256 | `0x84` | 32 bytes |
| P25 ARC4 | `0xAA` | 5 bytes |
| NXDN EHR | `0x01` | 2 bytes, value 1-32767 |

Only traffic keys are loaded. KEKs, UKEKs, LLAs, and unsupported algorithms are not voice keys. For Auto key types, SLNs 0-61439 identify traffic keys; 61440-65535 identify KEKs.

Console matches keys by algorithm and key ID, not keyset or SLN. Conflicting values for the same algorithm/key ID reject the container rather than selecting one arbitrarily. Use a dedicated Console container with only the traffic keys it needs.

Local keys apply to matching channels across systems in this codeplug and take precedence over downloaded keys. Use distinct key IDs where different systems need different material. No decrypted container or traffic keys are written to disk.

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

For P25, if `keyId` is blank or zero, the channel transmits clear. This does not prevent secure receive when the incoming key is available. DMR and NXDN encrypted channels instead block TX when the key configuration is invalid; they never silently fall back to clear.

NXDN supports `ehr` (15-bit scrambling), `des`, and `aes` (AES-256). EKC algorithm IDs match the FNE key service, not the NXDN wire cipher IDs. See **NXDN** for key sizes and FNE key-service behavior.

Encrypted DMR is temporarily unavailable. DMR channels must use `algo: "none"` to transmit. Loading an EKC does not enable encrypted DMR. See **DMR** for current limits.

---

# Selectable Encryption

P25 and NXDN secure-capable channels can expose an in-card encryption toggle:

```yaml
keyId: 0x50
algo: "aes"
selectable_encryption: true
```

When enabled, the resource card shows **SELECT** next to the TAR indicator area. Clicking **SELECT** toggles console transmit between encrypted and clear for that system/talkgroup.

The selected encrypted/clear state is saved and restored across restarts. The key and algorithm still come from the codeplug; the toggle only controls whether the console uses them for transmit.

Analog voice inversion can use the same **SELECT** control, but it is not encryption and does not use a key. See [Analog](08-Analog.md).

P25 and NXDN receive use the incoming algorithm and key ID, not the channel's configured transmit key or **SELECT** state. A matching key anywhere in the unlocked EKC can be used; downloaded keys are shared between resources on the same FNE system only. Local keys take precedence. Without a usable matching key, encrypted audio is muted rather than played as noise. This does not enable DMR encryption or change analog inversion.

The **ENC KID** column in Event History and channel Call History shows the on-air key ID in hexadecimal, including late-entry updates. Clear calls and non-call events leave it blank. This works without TAR recording enabled.

---

# FNE Key Requests

When **Restore Selected Channels On Startup** is enabled, selected encrypted channels may need to request keys after startup.

The console waits for the relevant FNE connection to complete before sending startup key requests. It then waits a short post-connect delay and spaces multiple key requests apart so the FNE is not flooded.

Missing keys on selected encrypted resources are also requested again after an FNE reconnect. Keys must exist in the FNE crypto container, and the system's regular `rid` must be authorized to request each KID.

Selecting multiple resources with the same algorithm/key ID sends one request per FNE system. Loaded keys are reused, and unanswered requests have a 30-second cooldown before a later selection can retry. Reconnecting clears the cooldown for missing keys. Keys and request state are cleared when the codeplug is reloaded. Receive traffic does not automatically request unknown keys.

NXDN EHR, DES, and AES use this same service. Their request/container algorithm IDs are `0x01`, `0x81`, and `0x84`, respectively; see **NXDN** for details and key lengths. Use unique key IDs for distinct material across protocols in the FNE container.

DMR key requests are disabled in this build.

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

- Protect the EKC container and your Windows account. DPAPI is not protection against software already running as your account.
- Migrate old clear keys into KFDEKC and securely retire the old files yourself; Console does not delete personal key files.
- Do not commit operational key material to source control.
- Use test keys for development environments.
