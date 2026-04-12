# Console Operation

This page covers common operator workflows on the main console.

---

# Selecting Resources

Click a resource card to select or deselect it.

Selected resources are monitored locally. If a receiving resource is deselected while active, local monitoring for that resource stops and the card RX state is cleared.

Use **Select All/Unselect All** from the toolbar to quickly toggle selected resources.

---

# PTT

Use a resource card PTT button to transmit on that resource.

The console supports normal press-and-hold PTT and toggle PTT mode.

Toggle PTT mode is controlled by:

```
Settings > Toggle Push To Talk Mode
```

Toggle PTT is off by default. If changed, the preference is saved.

---

# Global PTT

Global PTT keys the current primary selected channel.

If no primary channel is available, legacy all-channel global PTT behavior is not exposed in the menu. Use multi-select groups instead when an operator needs to transmit to multiple resources.

---

# Transmit Tail

When PTT is released, the console briefly holds transmit before sending call-end signaling.

This short de-key tail helps prevent clipped final syllables and final voice frames.

The tail affects the real transmit path, not only the UI.

---

# Talkgroup Validation

When a user tries to transmit or use a talkgroup, the console checks the active talkgroup rules received from the connected FNE.

If the talkgroup is unavailable on that FNE, the action is blocked and this warning is shown:

```
Target TG unavailable on FNE
```

This validation is per system and applies to P25 and DMR resources.

---

# RX Activity

When selected resources receive traffic, the card shows RX activity and source information.

Tabs show an audio activity icon when a resource on that tab is receiving. Long tab names are trimmed so the activity icon remains visible.

---

# Sticky Selected Channels

Controlled by:

```
Settings > Restore Selected Channels On Startup
```

When enabled:

- selected resources are restored on startup
- saved volume for restored resources is restored
- encrypted restored resources request keys after FNE connection and a short delay

When disabled:

- resources start unselected
- per-resource volumes start at default

---

# Card Indicator Icons

The top-right card indicator can show membership state.

Common meanings:

- active patch member
- disabled patch member
- multi-select member

Multi-select membership takes visual priority when both patch and multi-select memberships apply.
