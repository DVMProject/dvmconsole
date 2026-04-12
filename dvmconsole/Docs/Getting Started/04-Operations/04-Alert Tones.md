# Alert Tones

The console supports built-in and custom alert tones.

Alert tones are used for operator alerting workflows such as page/alert tones and channel hold tone behavior.

---

# Sending Alert Tones

Toolbar alert tone buttons send configured alert audio to the appropriate selected or primary resource depending on the current workflow.

Alert tone transmit still uses the configured resource, system, talkgroup, mode, and validation rules.

If the target TG is unavailable on the connected FNE, the console blocks the action and shows:

```
Target TG unavailable on FNE
```

---

# Alert Tone Manager

Open from:

```
Settings > Alerts > Manage Alert Tones
```

The Alert Tone Manager allows operators or admins to:

- view custom alert tones
- add a new alert tone
- rename an alert tone
- replace the backing audio file
- assign a tone to a tab
- delete a custom alert tone
- save changes without closing the manager

Changes are saved through the normal settings system.

---

# Audio File Requirements

Alert tone audio files must be compatible with the console audio pipeline.

The console validates alert tone files when sending and expects:

- PCM
- 16-bit
- mono
- 8000 Hz

If a file does not match, the console displays an error.

---

# Deleting Alert Tones

Deleting a custom alert tone removes it from:

- the manager list
- the UI
- saved settings
- saved tab assignment and related custom tone state

Use the confirmation prompt to avoid accidental removal.

---

# Tab Assignment

Custom alert tones can be assigned to a tab/resource tab when supported by the current codeplug layout.

This keeps custom tones near the resources operators use with them.

---

# Operational Notes

- Alert tone playback is local and transmitted over the selected target path when sent.
- Alert tone sends do not bypass talkgroup validation.
- If an alert tone is renamed, the display name shown in the UI updates and persists after restart.
- If the backing file is replaced, verify the new file uses the required audio format.
