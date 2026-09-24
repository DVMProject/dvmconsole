# Codeplug Creation

A codeplug is a YAML configuration file that defines the systems, tabs, resources, and group tabs used by the console.

At minimum, a codeplug defines:

- `systems`
- `zones`

Common optional sections include:

- `groups`
- `keyFile`
- `patchSourceIdPassthrough`
- `web_streams` under a zone

---

# R02A00 Compatibility

Codeplugs created for R01A00 should be reviewed before use with R02A00.

There have been major changes to resource configuration. Do not assume an older codeplug is ready for operational use without review and testing.

---

# Basic Structure

```yaml
keyFile: "Full/Path/To/Keyfile.clear"

systems:
  - ...

groups:
  - ...

patchSourceIdPassthrough: false

zones:
  - ...
```

---

# Systems

Systems define FNE connections.

Example:

```yaml
systems:
  - name: "System 1"
    identity: "Console 1"
    address: "fne.example.local"
    port: 62031
    peerId: 1000001
    rid: "1001"
    password: "RPT_PASSWORD"
    encrypted: false
    presharedKey: "123ABC1234"
    aliasPath: "Full/Path/To/alias.yml"
```

Fields:

- `name`: internal system name. Channels reference this value.
- `identity`: peer identity shown to the FNE.
- `address`: FNE hostname or IP address.
- `port`: FNE port.
- `peerId`: peer ID used for the console connection.
- `rid`: radio ID used when the console transmits.
- `nxdnRid`: optional NXDN-specific radio ID (1-65535). NXDN uses `rid` if this is omitted; IDs are never truncated.
- `password`: FNE password.
- `encrypted`: whether the FNE connection uses transport encryption.
- `presharedKey`: key used when `encrypted` is enabled.
- `kmfPresharedKey`: optional 64-hex-character AES-256 key for FNE-wrapped key responses. Required when the FNE enables `kmfEncKeyRequest`; separate from the transport key.
- `aliasPath`: optional RID alias YAML file.
- `syncRadioAliases`: optional, defaults to `false`. Download and cache this system's RID aliases from a supporting FNE after connecting. See **RID Aliases** for requirements and local-file fallback.

---

# Zones

Zones become main console tabs.

Example:

```yaml
zones:
  - name: "Primary"
    tabColor: "#E57373"
    tabTextColor: "#000000"
    channels:
      - ...
```

Fields:

- `name`: tab label.
- `tabColor`: tab background color in hex.
- `tabTextColor`: tab text color in hex.
- `channels`: resources shown on that tab.
- `web_streams`: optional web URL stream chips shown on that tab.

Long tab names are allowed. The console trims long labels so activity icons remain visible.

---

# Channels

Channels define resource cards.

Example:

```yaml
channels:
  - name: "Channel 1"
    system: "System 1"
    tgid: "2001"
    mode: "p25"
    keyId: 0x50
    algo: "aes"
    selectable_encryption: true
    resourceColor: "#150282"
    rx_only: false
    card_size: normal
```

Fields:

- `name`: resource/card name.
- `system`: system name from the `systems` section.
- `tgid`: target talkgroup ID.
- `mode`: `p25`, `dmr`, `nxdn`, or `analog`. If omitted, P25 is used.
- `keyId`: optional encryption key ID.
- `algo`: optional encryption algorithm, such as `aes`, `des`, `arc4`, or `none`.
- `scrambler_code`: optional analog voice-inversion code (`0` for off, or `2` through `16`); see [Analog](08-Analog.md). This is not an FNE encryption key.
- `selectable_encryption`: optional flag for P25, NXDN, or analog resources with `scrambler_code`. When `true`, the card shows a **SELECT** toggle so operators can choose protected or clear transmit. Digital modes require a valid `keyId` and `algo`; analog inversion uses its codeplug frequency and no key. Encrypted DMR is temporarily unavailable.
- `resourceColor`: optional resource card color in hex.
- `rx_only`: optional receive-only flag. When `true`, the resource card hides PTT, alert tone select, and channel marker/hold controls, and the resource is skipped by global, patch/group, and alert-tone transmit target paths.
- `card_size`: optional fixed resource card size. Supported values are `small`, `normal`, and `large`. If omitted or invalid, `normal` is used.
- `slot`: DMR timeslot, either 1 or 2. See **DMR** for clear-channel examples and current limits.
- `ran`: optional NXDN radio access number (0-63, default 0). NXDN does not use DMR timeslots. See **NXDN** for a complete example and encryption requirements.

The console validates target TGs against active talkgroup rules received from the connected FNE when a user attempts to transmit or otherwise use the TG.

Card size behavior:

- `small`: compact status/PTT card. The volume slider, alert tone select, channel marker, and call history buttons are hidden.
- `normal`: default resource card size and layout.
- `large`: larger resource card with larger text and controls.

---

# Web Streams

Web streams define compact URL audio chips on a zone tab.

Example:

```yaml
web_streams:
  - name: "Stream 1"
    url: "https://streams.example.local/stream-1"
    authUsername: "stream-user"
    authPassword: "stream-password"
    idleColor: "#150282"
```

Fields:

- `name`: stream chip label. Keep names unique because saved position, volume, active startup state, and output routing are keyed by name.
- `url`: direct audio stream URL. The stream must be playable by Windows Media Foundation.
- `authUsername`: optional HTTP Basic Auth username for protected streams.
- `authPassword`: optional HTTP Basic Auth password for protected streams.
- `idleColor`: optional active-idle chip color in hex. If omitted, the standard selected-resource blue is used.

Web stream chips start disabled after console load. Click the chip to start or stop local playback.

If **Restore Selected Channels On Startup** is enabled, active web streams are saved on shutdown and restarted on the next console launch.

Basic Auth credentials are stored in the codeplug file. Protect the file if protected stream credentials are configured.

When clicked on, the chip turns amber while connecting. The console tries up to three connection attempts with a short delay between attempts before marking the stream down.

When active, the chip turns green when audio is detected. If the stream URL is unreachable or cannot be decoded, the chip turns red and shows `Down`. Click a down stream once to return it to the off state.

Web streams are local monitor widgets only. They are not patch or multi-select members.

---

# Groups

Groups define tabs in the Groups window.

Example:

```yaml
groups:
  - name: "Patch 1"
    type: "patch"
  - name: "Multi Select 1"
    type: "multiselect"
```

Fields:

- `name`: group tab label.
- `type`: `patch` or `multiselect`.

Group memberships are assigned in the Groups window, not in the codeplug.

Patch group members persist across restart. Patch active state is separate and only persists when **Settings > Retain Patch State on Startup** is enabled.

Legacy `patchGroups` entries are treated as patch groups for compatibility.

---

# Patch Source ID Passthrough

`patchSourceIdPassthrough` controls source IDs on forwarded patch traffic.

```yaml
patchSourceIdPassthrough: false
```

When `false`, forwarded patch traffic uses the configured console RID for the destination system.

When `true`, the console attempts to pass through the inbound source ID while forwarding patch audio.

---

# Key File

Use `keyFile` to reference a YAML key file:

```yaml
keyFile: "Full/Path/To/Keyfile.clear"
```

See **Encryption Keys** for the key file format and runtime behavior.

---

# Example Codeplug

```yaml
keyFile: "C:/Example/keys.clear"

systems:
  - name: "System 1"
    identity: "Console 1"
    address: "fne.example.local"
    port: 62031
    peerId: 1000001
    rid: "1001"
    password: "RPT_PASSWORD"
    encrypted: false
    presharedKey: "123ABC1234"
    aliasPath: "C:/Example/alias.yml"

groups:
  - name: "Patch 1"
    type: "patch"
  - name: "Multi Select 1"
    type: "multiselect"

patchSourceIdPassthrough: false

zones:
  - name: "Primary"
    tabColor: "#E57373"
    tabTextColor: "#000000"
    channels:
      - name: "Channel 1"
        system: "System 1"
        tgid: "2001"
        mode: "p25"
        keyId: 0x50
        algo: "aes"
        resourceColor: "#150282"
        card_size: normal

      - name: "Channel 2"
        system: "System 1"
        tgid: "2002"
        mode: "p25"
        resourceColor: "#150282"
        card_size: small
    web_streams:
      - name: "Stream 1"
        url: "https://streams.example.local/stream-1"
        # Optional HTTP Basic Auth credentials for protected streams.
        #authUsername: "stream-user"
        #authPassword: "stream-password"
        idleColor: "#150282"

  - name: "DMR"
    tabColor: "#81C784"
    tabTextColor: "#000000"
    channels:
      - name: "Channel 3"
        system: "System 1"
        tgid: "3001"
        mode: "dmr"
```

---

# Recommended Practices

- Keep system names stable after deployment because channels reference them.
- Use clear channel names because saved positions and volume are keyed by channel name.
- Use clear group names, such as `Patch 1` or `Multi Select 1`.
- Keep zone tabs short enough for operators to scan quickly.
- Verify each channel references a valid system.
- Verify each group has a supported `type`.
- Keep web stream names stable so saved chip position and volume remain associated with the correct stream.
- Confirm TGs exist in FNE talkgroup rules before relying on them operationally.
