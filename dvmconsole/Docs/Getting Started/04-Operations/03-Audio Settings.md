# Audio Settings

Open Audio Settings from:

```
Settings > Audio Settings
```

Audio Settings controls microphone input and local speaker output routing.

---

# Global Input Device

The global input device is the microphone used for console transmit audio.

Options include:

- Windows Default Input
- specific installed input devices

If Windows Default Input is selected, the console follows the current Windows default microphone.

If a specific device is selected, the console uses that device instead.

If a saved device is missing, the console falls back to Windows default.

---

# Master Output Device

The master output device is the default speaker/output device for resources.

Options include:

- Windows Default Output
- specific installed output devices

If Windows Default Output is selected, the console follows the current Windows default playback device.

---

# Per-Resource Output Overrides

Audio Settings is organized by the same zones/tabs used on the main console.

Each channel resource and web stream chip can use:

```
Default (Master Output)
```

or a specific output device override.

Use default for most resources. Use overrides when a specific talkgroup must always play through a different speaker or audio interface.

Web stream output overrides are keyed by stream name. Keep stream names stable if you want saved routing, volume, active startup state, and position to continue applying to the same stream.

---

# AGC

The **Enable AGC for console microphone audio** checkbox controls console microphone automatic gain behavior.

When enabled, the console applies its microphone AGC path before transmit.

When disabled, the console uses the raw microphone behavior.

This setting is saved.

---

# Mute RX Audio While Transmitting

This setting is in the Settings menu, not the Audio Settings window:

```
Settings > Mute RX Audio While Transmitting
```

When enabled, local RX speaker playback is suppressed while the console is transmitting.

This does not affect:

- received network traffic
- logs
- RX card visual state
- patch forwarding
- transmit audio

It only mutes local playback while TX is active.

---

# Stale Routing Cleanup

When a codeplug is loaded, the console prunes saved per-resource settings that clearly refer to resources or talkgroups no longer present in the loaded codeplug.

This helps keep the AppData settings JSON from accumulating stale audio routing, volume, and position entries.

---

# Tips

- Use Windows Default devices unless a deployment needs fixed hardware routing.
- Use the master output for the normal speaker path.
- Use per-resource overrides sparingly so future troubleshooting is easier.
- If audio is playing from the wrong device, check both the master output and the resource override.
