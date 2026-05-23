# Console Operation

This page covers common operator workflows on the main console.

---

# Selecting Resources

Click a resource card to select or deselect it.

Selected resources are monitored locally. If a receiving resource is deselected while active, local monitoring for that resource stops and the card RX state is cleared.

Use **Select All/Unselect All** from the toolbar to quickly toggle selected resources.

---

# Resource Card Sizes

Resource card size is defined in the codeplug with `card_size`.

Supported sizes:

- `small`: compact status/PTT card. Volume, alert tone select, channel marker, and call history buttons are hidden.
- `normal`: default card size.
- `large`: larger card with larger text and controls.

If `card_size` is omitted, the console uses `normal`.

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

# Selectable Encryption

Some P25 secure-capable resources may show **SELECT** in the card text area.

Click **SELECT** to toggle that resource between encrypted and clear console transmit. The choice is saved by system/talkgroup and restored on the next startup.

If the resource does not show **SELECT**, encryption behavior is fixed by the codeplug.

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

# Web Stream Chips

Codeplug-defined web stream chips appear on the zone tab where they are configured.

Click a stream chip to start or stop playback. Streams load in the off state unless **Restore Selected Channels On Startup** is enabled and the stream was active at shutdown.

Stream chips use a compact volume slider. User volume changes are saved by stream name, and the chip turns green when audio is detected.

When a stream is starting, the chip turns amber while connecting. The console tries up to three connection attempts before marking the stream down.

If a stream URL is unreachable or cannot be decoded, the chip turns red and shows `Down`. Click a down stream once to return it to the off state.

Protected streams can use HTTP Basic Auth through `authUsername` and `authPassword` fields in the codeplug.

Web streams are local monitor widgets. They are not patch or multi-select members.

The reset tab layout action also moves web stream chips on the active tab and saves their new positions.

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
