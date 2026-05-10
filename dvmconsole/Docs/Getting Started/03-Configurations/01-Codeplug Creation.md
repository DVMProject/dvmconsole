# Codeplug Creation

A codeplug is a YAML configuration file that defines the systems, tabs, resources, and group tabs used by the console.

At minimum, a codeplug defines:

- `systems`
- `zones`

Common optional sections include:

- `groups`
- `keyFile`
- `patchSourceIdPassthrough`

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
- `password`: FNE password.
- `encrypted`: whether the FNE connection uses transport encryption.
- `presharedKey`: key used when `encrypted` is enabled.
- `aliasPath`: optional RID alias YAML file.

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
    resourceColor: "#150282"
    rx_only: false
```

Fields:

- `name`: resource/card name.
- `system`: system name from the `systems` section.
- `tgid`: target talkgroup ID.
- `mode`: `p25` or `dmr`. If omitted, P25 is used.
- `keyId`: optional encryption key ID.
- `algo`: optional encryption algorithm, such as `aes`, `des`, `arc4`, or `none`.
- `resourceColor`: optional resource card color in hex.
- `rx_only`: optional receive-only flag. When `true`, the resource card hides PTT, alert tone select, and channel marker/hold controls, and the resource is skipped by global, patch/group, and alert-tone transmit target paths.
- `slot`: optional DMR slot field if used by the deployment.

The console validates target TGs against active talkgroup rules received from the connected FNE when a user attempts to transmit or otherwise use the TG.

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

      - name: "Channel 2"
        system: "System 1"
        tgid: "2002"
        mode: "p25"
        resourceColor: "#150282"

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
- Confirm TGs exist in FNE talkgroup rules before relying on them operationally.
