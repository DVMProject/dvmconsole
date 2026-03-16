# Groups and Patching

This page explains how **patch groups** and **multi-select groups** work in the **Digital Voice Modem Desktop Dispatch Console**.

These groups are managed from the **Groups** window and are intended to help operators talk to multiple resources at once.

---

# Patch Groups

A **patch group** links multiple channels together so audio from one member can be forwarded to the others.

Use a patch group when you want traffic from one selected member to be heard by the other members in the group.

## Two-Way Patch

When **Enable One-Way Patch** is off:

- Patch Mode: Two-Way
- All members can transmit and receive.

In this mode, any listed member can become the active patch source.

## One-Way Patch

When **Enable One-Way Patch** is on:

- Patch Mode: One-Way
- First listed member is the source.
- All following members receive audio.

Important:

- Member order matters in one-way mode.
- Member `1` is treated as the source.
- Members `2+` are treated as destinations.

---

# Multi-Select Groups

A **multi-select group** lets an operator transmit to multiple channels at the same time using the group transmit button.

Unlike a patch group, a multi-select group does not create a forwarded audio relationship between members. It is an operator transmit tool.

Use a multi-select group when you want to key up several channels together from the console.

---

# Editing Group Members

To change the members of a patch group or multi-select group:

1. Open the **Groups** window.
2. Select the group tab you want to change.
3. Click **Edit Members**.
4. Drag channels from the main console into the member list.
5. Use **Remove** next to a listed member if you want to take it out.
6. Click **Stop Editing** when you are done.

Notes:

- Editing applies to the currently selected group tab.
- Switching tabs exits edit mode for the current tab.
- Group memberships are currently **session-only** and do **not** persist across console restart.

---

# Group PTT

Each group has a transmit button:

- **Patch PTT** for patch groups
- **Multi-Select PTT** for multi-select groups

Clicking this button starts transmitting to the members of that group.

Click it again to stop.

This is separate from **Edit Members**:

- **Edit Members** changes who belongs to the group
- **Patch PTT** or **Multi-Select PTT** transmits to the current group members

---

# Current Operator Notes

- Group members are added from the main console by drag and drop.
- Patch and multi-select memberships are not loaded back after restart.
- One-way patch direction is determined by the current member order.
- If you need a different source channel in one-way mode, reorder the group by removing and re-adding members in the order you want.

