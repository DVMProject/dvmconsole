# Troubleshooting Tools

This page covers built-in tools used for diagnostics and connection management.

---

# FNE Connection Manager

Open from:

```
Tools > FNE Connection Manager
```

The FNE Connection Manager shows all configured FNE systems and their current connection state.

For each system, it provides:

- system name
- connected/disconnected status
- Start or Stop button
- Restart button

## Start

Starts a disconnected system connection.

## Stop

Cleanly disconnects an active system connection.

## Restart

Stops the connection if active, then starts it again.

Use Restart after changing FNE-side configuration such as talkgroup rules if the active connection needs to be refreshed.

---

# Debug Log Viewer

Open from:

```
Help > About > View Debug Logs
```

The Debug Logs window displays live log output from the existing app logging system.

Behavior:

- read-only
- rolling buffer of the most recent 500 lines
- updates while the app is running
- clear button for the viewer
- pause auto-scroll option

## Pause Auto-Scroll

When enabled, the view keeps the current scroll position while new log entries continue to be buffered.

When disabled, the view resumes following the newest log entries.

---

# Useful Troubleshooting Signals

## Target TG unavailable on FNE

The console blocked an action because the target talkgroup is not currently available according to active FNE talkgroup rules.

Check:

- the channel TGID
- the correct system/FNE
- FNE talkgroup rules
- whether rules were recently updated and the connection refreshed

## Key or encryption issues

Check:

- key file path
- channel `keyId`
- channel `algo`
- FNE connection status
- Key Status window

## No local RX audio

Check:

- resource is selected
- master output device
- per-resource output override
- Windows output device
- whether **Mute RX Audio While Transmitting** is enabled while TX is active

## Debug logs do not auto-scroll

Check whether **Pause Auto-Scroll** is enabled in the Debug Logs window.

---

# When to Use Logs

Use Debug Logs when you need to confirm:

- FNE connection lifecycle
- TG rule updates
- call start/call end events
- key requests or responses
- patch forwarding behavior
- unexpected transmit or receive state
