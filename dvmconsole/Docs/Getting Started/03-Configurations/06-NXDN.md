# NXDN

NXDN resources use 4800-baud NXDN group voice through the FNE. They can share a codeplug and FNE connection with P25 and DMR resources.

Normal PTT, global/multi-select PTT, patches, generated/custom tones, channel hold, audio routing, RID aliases, Event/Call History, and TAR use the same console controls. NXDN encryption has its own wire format and key limits.

---

# Codeplug

Set the system's `nxdnRid` when its regular `rid` is outside the NXDN range or a different NXDN identity is wanted. Otherwise NXDN uses `rid`. Both the NXDN subscriber ID and talkgroup must be between 1 and 65535. The console does not truncate larger IDs.

Example system fields to add to an existing connection:

```yaml
rid: "600109"
nxdnRid: "1001"
```

Example channels under a zone:

```yaml
channels:
  - name: "NXDN Clear"
    system: "System 1"
    tgid: "300"
    mode: "nxdn"
    ran: 0
    algo: "none"
    rx_only: false

  - name: "NXDN Secure"
    system: "System 1"
    tgid: "303"
    mode: "nxdn"
    ran: 0
    algo: "aes"
    keyId: "03"
    selectable_encryption: true
```

`ran` is the transmitted radio access number, from 0 through 63. It defaults to 0. NXDN does not use the DMR `slot` field. Configure the FNE to allow NXDN traffic and provision the talkgroups in its active rules.

Use distinct talkgroup IDs for different protocols within a system: console resource settings, patches, and audio routing are identified by system/talkgroup.

---

# Encryption

| Channel `algo` | NXDN key-file `algId` | Key material |
| --- | --- | --- |
| `none` | Not applicable | Clear voice |
| `ehr` or `scrambler` | 1 | 2 bytes / 4 hex digits; value 1-32767 |
| `des` | 2 | 8 bytes / 16 hex digits; weak DES keys rejected |
| `aes` or `aes256` | 3 | 32 bytes / 64 hex digits |

EHR is scrambling, not strong encryption. NXDN key IDs are 1-63. Channel `keyId` strings are hexadecimal, so `"3F"` means key 63. Key-file IDs are YAML numbers.

Reference the key file using the existing top-level `keyFile` setting. NXDN entries must specify `protocol: nxdn`. The optional `system` field limits a local key to one FNE; omit it only when that key should be shared across systems.

Synthetic test key example (do not use as an operational key):

```yaml
keys:
  - protocol: nxdn
    system: "System 1"
    algId: 3
    keyId: 3
    key: "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F"
```

Existing P25 key entries without `protocol` keep their existing meaning. Explicit NXDN local keys take precedence over FNE responses.

## FNE-Supplied Keys

Selected NXDN resources can request EHR, DES, and AES material from the FNE without a local `keyFile`. Key requests use the existing peer KMM service, not over-the-air NXDN rekeying.

| NXDN channel | FNE container / request algorithm | Key length |
| --- | --- | --- |
| EHR | `0x01` | 2 bytes |
| DES | `0x81` | 8 bytes |
| AES | `0x84` | 32 bytes |

These FNE algorithm IDs differ from the NXDN local key-file/wire cipher IDs. EHR requires a container entry with algorithm 1 and a two-byte key; not every container editor exposes that combination. The FNE looks up material by key ID, so use distinct key IDs for different keys across protocols in the same container.

The FNE must have crypto-container support compiled in, a successfully loaded container, and RID authorization for the requested keys. Authorize the console system's regular `rid`, **not** `nxdnRid`, with `CanRequestKeys` and the appropriate allowed KIDs in the FNE radio-ID list.

If the FNE enables `kmfEncKeyRequest`, set `kmfPresharedKey` under the console system to the same 64-hex-character wrapping key. This is separate from `encrypted` / `presharedKey`, which protect the FNE network transport. Protect the codeplug because it contains these secrets. Prefer encrypted transport as well; without either protection, key responses expose traffic keys on the network.

Startup requests wait five seconds after connection and are spaced 100 ms apart. Missing keys for selected resources are requested again after reconnect. Downloaded NXDN keys are scoped to the source FNE and kept in memory, not saved to a local key file.

FNECore unwraps EHR responses to the required two-byte key and rejects invalid padding. Check **Key Status** for the actual loaded state.

**SELECT** switches secure-capable resources between clear and encrypted TX and saves that choice. Encrypted TX is blocked if the key is missing or invalid. Incoming encrypted audio is muted without a matching key or usable synchronization. A wrong key cannot be reliably detected by these unauthenticated voice ciphers.

---

# Operation and Limits

FNECore handles NXDN framing, FACCH/SACCH signaling, EHR/DES/AES privacy, IV rotation, receive synchronization, and peer key-response unwrapping. The console supplies the native AMBE vocoder adapter and owns audio devices, transmit pacing, key selection/storage, UI, history, TAR, and group routing. This follows the same protocol-library/application split as P25; it does not require FNECore to load the console's native audio libraries.

NXDN carries four 20 ms AMBE codewords per 80 ms network voice frame. Call end completes a partial frame and sends one release; cancelled tones discard pending audio. A new call waits until the previous release has finished.

The receiver supports late entry and reacquires privacy synchronization after packet loss. An encrypted frame with stolen voice slots is muted until fresh synchronization rather than played with an uncertain keystream position.

Console-to-console tests cover clear, EHR, DES, and AES through a local FNE, including history and TAR. FNE key downloads are also tested against an SSL-enabled Linux FNE with an encrypted key container. Radio/repeater interoperability and the target site's key permissions/configuration still require verification before operational use.

This is NXDN group voice, not a translation of every P25 service. P25 subscriber paging/check/inhibit commands remain P25-only. NXDN 9600/EFR, individual calls, packet data, OTAR, and trunking control-channel services are not implemented by this console path.
