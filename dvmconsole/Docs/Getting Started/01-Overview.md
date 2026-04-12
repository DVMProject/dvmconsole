# Overview

The Digital Voice Modem Desktop Dispatch Console is a WPF desktop console for monitoring and transmitting on DVM FNE talkgroups from one operator position.

The console is organized around resources. A resource is a channel card that maps to a system and talkgroup from the loaded codeplug.

Common operator tasks include:

- Monitor selected resources
- Transmit with resource PTT or Global PTT
- Send alert tones or hold tones
- Build patch groups and multi-select groups
- Manage audio input and output routing
- View call history, key status, connection status, and debug logs

---

# Main Concepts

## Systems

A system is an FNE connection defined in the codeplug.

Each system has its own address, port, peer ID, console RID, password, and optional alias file.

## Zones

Zones become tabs across the top of the main console.

Use zones to group resources by dispatch area, agency, site, or operational role.

## Resources

Resources are channel cards inside a zone.

Each resource maps to:

- a system
- a talkgroup ID
- a mode such as P25 or DMR
- optional encryption information
- optional visual color

## Groups

Groups are configured in the codeplug and managed at runtime in the Groups window.

Two group types are supported:

- Patch groups
- Multi-select groups

Patch group membership can persist across restart. Patch active state is controlled separately.

---

# Important Windows

## Main Console

The main console contains resource cards, system status widgets, toolbar buttons, menus, and tabs.

## Groups

Open from:

```
View > Groups
```

Used to edit patch and multi-select memberships, enable or disable patch groups, and use group PTT.

## Audio Settings

Open from:

```
Settings > Audio Settings
```

Used to select the global microphone input, master output device, per-resource output overrides, and AGC behavior.

## Alert Tone Manager

Open from:

```
Settings > Alerts > Manage Alert Tones
```

Used to add, rename, delete, replace, and assign custom alert tones to tabs.

## FNE Connection Manager

Open from:

```
Tools > FNE Connection Manager
```

Used to manually start, stop, or restart configured FNE system connections.

## Debug Logs

Open from:

```
Help > About > View Debug Logs
```

Used to view recent live FNECore/debug log output.

---

# Startup Behavior

The console can restore selected channels on startup if enabled in Settings.

When this is enabled:

- selected channel state is restored
- saved per-resource volume is restored for those restored channels
- encrypted restored channels request keys after the relevant FNE connection is established and a short post-connect delay has passed

When restore selected channels is disabled:

- resources start unselected
- per-resource volumes start at default

Patch group members are sticky across restart. Patch active state only restores if **Retain Patch State on Startup** is enabled.

---

# Talkgroup Availability

The console validates target talkgroups using active talkgroup rules received from the connected FNE.

If a resource targets a talkgroup that is not currently available on that FNE, transmit actions are blocked and the console displays:

```
Target TG unavailable on FNE
```

This validation is per system. A talkgroup can be valid on one FNE and unavailable on another.

---

# Project Notes

- The console does not interface directly with base station or mobile radios.
- For a DVM-compatible console that supports base/mobile radio interfacing, see RadioConsole2 and rc2-dvm.
- This software is intended for amateur and educational use. Any other use is at the user's discretion and risk.

---

# License

This project is licensed under the AGPLv3 License. See the repository `LICENSE` file for details.
