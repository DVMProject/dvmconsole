# DMR

DMR resources use conventional two-slot DMR group voice through the FNE. They can share a codeplug and FNE connection with P25 and NXDN resources.

Normal PTT, global/multi-select PTT, patches, generated/custom tones, channel hold, audio routing, RID aliases, Event/Call History, and TAR use the same console controls. DMR Association privacy has its own wire algorithm IDs, key limits, and synchronization signaling.

---

# Codeplug

The system `rid`, channel `tgid`, and DMR timeslot must be valid before transmit. RID and TGID values are between 1 and 16777215. Timeslots are configured as 1 or 2.

Example channels under a zone:

```yaml
channels:
  - name: "DMR Clear"
    system: "System 1"
    tgid: "3010"
    mode: "dmr"
    slot: 1
    algo: "none"
    rx_only: false

  - name: "DMR Secure"
    system: "System 1"
    tgid: "3011"
    mode: "dmr"
    slot: 2
    algo: "aes"
    keyId: "23"
    selectable_encryption: true
```

Configure the FNE to allow DMR traffic and provision each talkgroup/timeslot in its active rules. Use distinct talkgroup IDs for different protocols within a system: console resource settings, patches, and audio routing are identified by system/talkgroup.

---

# Encryption

| Channel `algo` | DMR key-file `algId` | Key material |
| --- | --- | --- |
| `none` | Not applicable | Clear voice |
| `arc4` | 1 | 5 bytes / 10 hex digits |
| `des` or `des-ofb` | 2 | 8 bytes / 16 hex digits; weak DES keys rejected |
| `aes` or `aes256` | 5 | 32 bytes / 64 hex digits |

DMR key IDs are 1-255. Channel `keyId` strings are hexadecimal, so `"23"` means key 35. Key-file IDs are YAML numbers.

Reference the key file using the existing top-level `keyFile` setting. DMR entries must specify `protocol: dmr`. The optional `system` field limits a local key to one FNE; omit it only when that key should be shared across systems.

Synthetic test-key examples (do not use as operational keys):

```yaml
keys:
  - protocol: dmr
    system: "System 1"
    algId: 1
    keyId: 0x21
    key: "0102030405"

  - protocol: dmr
    system: "System 1"
    algId: 2
    keyId: 0x22
    key: "133457799BBCDFF1"

  - protocol: dmr
    system: "System 1"
    algId: 5
    keyId: 0x23
    key: "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F"
```

Existing P25 key entries without `protocol` keep their existing meaning. Explicit DMR local keys take precedence over FNE responses.

## FNE-Supplied Keys

Selected DMR resources can request ARC4, DES, and AES material from the FNE without a local `keyFile`. Key requests use the existing peer KMM service, not over-the-air DMR rekeying.

| DMR channel | FNE container / request algorithm | Key length |
| --- | --- | --- |
| ARC4 | `0xAA` | 5 bytes |
| DES | `0x81` | 8 bytes |
| AES | `0x84` | 32 bytes |

These FNE algorithm IDs differ from the DMR local key-file and wire algorithm IDs. The FNE looks up material by key ID, so use distinct key IDs for different keys across protocols in the same container.

The FNE must have crypto-container support compiled in, a successfully loaded container, and RID authorization for the requested keys. Authorize the console system's regular `rid` with `CanRequestKeys` and the appropriate allowed KIDs in the FNE radio-ID list.

If the FNE enables `kmfEncKeyRequest`, set `kmfPresharedKey` under the console system to the same 64-hex-character wrapping key. This is separate from `encrypted` / `presharedKey`, which protect the FNE network transport. Protect the codeplug because it contains these secrets.

Startup requests wait five seconds after connection and are spaced 100 ms apart. Missing keys for selected resources are requested again after reconnect. Downloaded DMR keys are scoped to the source FNE and kept in memory, not saved to a local key file.

**SELECT** switches secure-capable resources between clear and encrypted TX and saves that choice. Encrypted TX is blocked if the key is missing or invalid. Incoming encrypted audio is muted without a matching key or usable synchronization. A wrong key cannot be reliably detected by these unauthenticated voice ciphers.

---

# Operation and Limits

FNECore handles DMR LC/PI framing, embedded signaling, ARC4/DES/AES privacy, message-indicator cycling, receive synchronization, superframe completion, and call-end signaling. The console supplies the native AMBE vocoder adapter and owns audio devices, transmit pacing, key selection/storage, UI, history, TAR, and group routing. This follows the same protocol-library/application split as P25 and NXDN.

DMR carries three 20 ms AMBE codewords per 60 ms network voice frame. Call end completes the current six-burst superframe and sends one terminator; cancelled tones discard pending audio. A new call waits until the previous terminator has finished.

The receiver supports normal PI-header synchronization and Association late entry. Encrypted voice is never passed to the vocoder unless the required key and message indicator are available.

Protocol tests cover clear and ARC4/DES/AES packet generation, privacy round trips, PI headers, embedded encryption identifiers, late-entry metadata, bounded short-call completion, and terminator sequencing. Console-to-console and radio/repeater interoperability still require live validation before operational use.

This is DMR group voice. Individual calls, packet data, OTAR, and trunking control-channel services are not implemented by this console path.
