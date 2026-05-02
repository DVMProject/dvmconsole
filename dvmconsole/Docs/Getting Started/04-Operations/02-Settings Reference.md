# Settings Reference

This page summarizes user-facing settings in the console.

---

# Settings Menu

## Toggle Push To Talk Mode

When enabled, clicking PTT toggles transmit on or off.

When disabled, PTT behaves as press-and-hold.

Default:

```
Off
```

This setting is saved.

## Talk Permit Tone

When enabled, the console plays a short local tone when transmit begins.

This is local operator feedback only.

## Mute RX Audio While Transmitting

When enabled, local RX speaker playback is muted while the console is transmitting.

This does not block received network traffic, logs, patch forwarding, or RX visual state. It only suppresses local speaker playback during TX.

Use this when operators use speakers and you want to reduce the chance of received audio feeding back into the microphone.

## Retain Patch State on Startup

When enabled, patch active/on-off state is restored on startup.

Patch members always persist. This setting only controls whether enabled patches come back enabled after restart.

Default:

```
Off
```

## Restore Selected Channels On Startup

When enabled, selected resources are restored on startup.

Restored selected encrypted resources request keys after the relevant FNE connection is established and a short delay has passed.

When disabled, selected resources and per-resource volume do not come back sticky on startup.

## Audio Settings

Opens the Audio Settings window.

See **Audio Settings** for details.

## Reset Settings

Clears saved user settings.

Use with care. This can remove saved window layout, widget positions, audio routing, selected channel state, and other preferences.

## Select Widgets to Display

Controls whether major widget categories are shown.

## Alerts > Manage Alert Tones

Opens the Alert Tone Manager.

## Keyboard Shortcuts > Set Global PTT Keybind

Prompts for a key and stores it as the global PTT shortcut.

---

# View Menu

## Select User Background

Chooses a custom background image for the main console.

## Dark Mode

Toggles the app theme.

## Lock Widgets

Prevents resource and status widgets from being moved.

## Reset Tab Layout

Snaps channel cards back to a grid-style layout.

## Fit Channel Display to Window Size

Resizes the channel display area to the current window.

## Snap Call History To Window

Keeps the Call History window aligned next to the main console when shown.

## Groups

Opens the Groups window.

## Always on Top

Keeps the console above other windows.

---

# Tools Menu

## Talkgroup Audio Recorder

Open from:

```
Tools > Talkgroup Audio Recorder
```

Sub-items:

- Viewer
- Configuration

See **Configurations > Talkgroup Audio Recorder** for TAR recording, retention, playback, filtering, and retention details.

## FNE Connection Manager

Opens manual connection controls for configured FNE systems.

---

# Help Menu

## Documentation

Opens this documentation viewer.

## About

Shows version information and includes the **View Debug Logs** button.
