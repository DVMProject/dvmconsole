# Codeplug Creation

This page explains how to create and structure a codeplug for the **Digital Voice Modem Desktop Dispatch Console**.

A codeplug is a YAML configuration file that defines:

- Systems the console connects to
- Zones (tabs) shown in the UI
- Channels displayed in each zone
- Optional encryption and visual settings

The console loads this configuration file at startup.

An example codeplug is included with the project.

---

# Basic Structure

A console codeplug is composed of two primary sections:

```
systems
zones
```

Optional sections such as encryption key files may also be defined.

Example structure:

```yaml
systems:
  - ...

zones:
  - ...
```

---

# Systems

The `systems` section defines the FNE systems the console can connect to.

Each system entry defines connection parameters and authentication information.

Example:

```yaml
systems:
  - name: "System 1"
    identity: "CONS OP1"
    address: "127.0.0.1"
    port: 62031
    peerId: 1234567
    rid: "12345"
    password: "RPT_PASSWORD"
    encrypted: false
    presharedKey: "123ABC1234"
```

Fields:

### name

Internal system name used throughout the codeplug.

Channels reference this value using the `system` field.

### identity

Human-readable name used to identify the console peer on the FNE.

### address

Hostname or IP address of the FNE master.

### port

Network port used for the FNE connection.

### peerId

Peer ID used when connecting to the FNE.

### rid

Radio ID used when the console transmits traffic.

### password

Authentication password used for the FNE connection.

### encrypted

Indicates whether the FNE connection uses encryption.

### presharedKey

Pre-shared encryption key used when `encrypted` is enabled.

### aliasPath (optional)

```yaml
aliasPath: "Full/Path/To/alias.yml"
```

Path to a radio alias file used for displaying subscriber names.

---

# Key File (Optional)

A key file can be defined to provide encryption keys.

Example:

```yaml
keyFile: "Full/Path/To/Keyfile.clear"
```

The referenced file contains encryption key definitions used by channels.

---

# Zones

Zones define the tabs displayed across the top of the console interface.

Each zone contains a list of channels that will appear on that tab.

Example:

```yaml
zones:
  - name: "Primary"
    tabColor: "#E57373"
    tabTextColor: "#000000"
```

Fields:

### name

Name of the tab displayed in the console.

### tabColor

Background color of the tab in hexadecimal format.

Example:

```
#E57373
```

### tabTextColor

Color of the tab label text.

Example:

```
#000000
```

### channels

List of channel resources displayed within the zone.

---

# Channels

Channels represent individual dispatch resources.

Each channel corresponds to a talkgroup on a system.

Example:

```yaml
channels:
  - name: "Channel 1"
    system: "System 1"
    tgid: "2001"
```

Fields:

### name

Display name of the resource.

### system

System name defined in the `systems` section.

### tgid

Talkgroup ID used for transmit and receive.

---

# Optional Channel Settings

Channels may include additional optional configuration fields.

### mode

Voice mode for the channel.

Example:

```yaml
mode: "p25"
```

Supported values include:

```
p25
dmr
```

### keyId

Encryption key ID used for the channel.

Example:

```yaml
keyId: 0x50
```

### algo

Encryption algorithm used by the channel.

Example:

```yaml
algo: "aes"
```

Supported algorithms:

```
aes
des
arc4
none
```

### encryptionKey

Currently unused in most deployments. Future versions may allow this field to override FNE key management.

Example:

```yaml
encryptionKey: null
```

### resourceColor

Color of the channel widget in the console.

Example:

```yaml
resourceColor: "#150282"
```

---

# Example Minimal Codeplug

```yaml
systems:
  - name: "System 1"
    identity: "CONS OP1"
    address: "127.0.0.1"
    port: 62031
    peerId: 1234567
    rid: "12345"
    password: "RPT_PASSWORD"
    encrypted: false
    presharedKey: "123ABC1234"

zones:
  - name: "Primary"
    tabColor: "#E57373"
    tabTextColor: "#000000"

    channels:
      - name: "Channel 1"
        system: "System 1"
        tgid: "2001"
        mode: "p25"
```

---

# Recommended Practices

- Keep system names short and consistent
- Use clear zone names for tab organization
- Group channels logically by purpose
- Verify that each channel references a valid system
- Test codeplug changes before deploying large configurations