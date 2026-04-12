# Groups and Patching

Patch groups and multi-select groups are managed from the Groups window.

Open it from:

```
View > Groups
```

Groups are defined in the codeplug. Members are assigned in the app by the operator.

---

# Patch Groups

A patch group forwards audio between member resources.

Use a patch group when traffic received on one member should be repeated to other members in the group.

Patch groups have two separate pieces of state:

- membership
- active/on-off state

Membership can exist while the patch is disabled.

---

# Patch Enable/Disable

Each patch group has an enable/disable control in the Groups window.

When a patch is disabled:

- members stay assigned
- patch forwarding is inactive
- channel cards can still show that the resource belongs to a patch

When a patch is enabled:

- patch forwarding can occur between members
- member card indicators show the active patch icon

Patch members are always sticky across restart.

Patch active state only restores on startup when:

```
Settings > Retain Patch State on Startup
```

is enabled.

If that setting is off, patches start disabled after restart even though members remain assigned.

---

# Two-Way Patch

When **Enable One-Way Patch** is off:

- Patch Mode: Two-Way
- All members can transmit and receive.

In this mode, any listed member can become the active patch source. Audio received from the active source can be forwarded to the other members.

---

# One-Way Patch

When **Enable One-Way Patch** is on:

- Patch Mode: One-Way
- First listed member is the source.
- All following members receive audio.

Member order matters.

```
Member 1 = Source
Members 2+ = Destinations
```

If the wrong member is acting as the source, remove and re-add members in the desired order.

---

# Multi-Select Groups

A multi-select group is an operator transmit tool.

It lets the console transmit to multiple member resources at the same time using **Multi-Select PTT**.

Unlike a patch group, a multi-select group does not forward received audio between members.

Use multi-select when an operator wants to key several resources together from the console.

---

# Editing Members

To edit a group:

1. Open **View > Groups**.
2. Select a patch or multi-select group tab.
3. Click **Edit Members**.
4. Drag resources from the main console into the group member list.
5. Remove members from the list if needed.
6. Click **Stop Editing**.

Patch edit mode is temporary.

Important behavior:

- Closing the Groups window automatically stops edit mode.
- PTT is blocked while patch edit mode is active to avoid conflicting transmit state.
- Switching group tabs exits edit mode for the previous group.

---

# Group PTT

Each group has a transmit button:

- **Patch PTT**
- **Multi-Select PTT**

Click the button to start group transmit. Click again to stop.

The console includes a short transmit tail after de-key so final audio frames are not clipped before call end signaling is sent.

---

# Card Icons

Resource cards show top-right indicators for patch or multi-select membership.

Common meanings:

- `patch_edit_off.png`: resource belongs to a disabled patch
- `patch_edit_on.png`: resource belongs to an enabled patch
- `msel_inactive.png`: resource belongs to a multi-select group

If a resource belongs to both a patch and a multi-select group, the multi-select indicator takes priority in the card indicator area.

---

# Persistence Summary

| Item | Persists by default | Notes |
| --- | --- | --- |
| Patch members | Yes | Always sticky |
| Patch enabled state | No | Only sticky when Retain Patch State on Startup is enabled |
| Multi-select members | Yes | Managed from the Groups window |
| Edit mode | No | Clears when editing stops or the Groups window closes |

---

# Operator Tips

- Use patch groups for cross-resource receive forwarding.
- Use multi-select groups for console-originated group transmit.
- Keep one-way patch member order obvious.
- Disable a patch instead of removing members when you want to keep the setup for later.
