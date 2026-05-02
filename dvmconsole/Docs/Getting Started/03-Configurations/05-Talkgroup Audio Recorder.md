# Talkgroup Audio Recorder

This page covers the built-in **Talkgroup Audio Recorder (TAR)** feature.

TAR records selected talkgroups to local `.wav` files and stores a matching `.json` metadata sidecar for each recording.

---

# Open TAR

Open TAR from:

```
Tools > Talkgroup Audio Recorder > Viewer
Tools > Talkgroup Audio Recorder > Configuration
```

Use **Viewer** to review and play recordings.

Use **Configuration** to choose the recording folder and decide which talkgroups TAR records.

---

# What TAR Records

TAR records per-call audio, not one long continuous file.

It can record:

- received call audio on TAR-enabled talkgroups
- console-originated transmit audio on TAR-enabled talkgroups

TAR only records when both of these are true:

- TAR is enabled for the talkgroup in TAR Configuration
- the resource is selected in the main console so the console is actively monitoring or using that path

If a talkgroup is TAR-enabled but the resource is not selected, TAR does not capture live call audio for that resource.

---

# Recording Folder

TAR requires a valid recording folder.

Default location:

```
Documents\DVMConsole\TAR
```

You can change this in:

```
Tools > Talkgroup Audio Recorder > Configuration
```

If the folder does not exist, the console creates it when TAR settings are saved.

---

# Enable Recording

In the TAR Configuration window:

- channels are grouped by console tab
- each row is keyed by TGID
- check **Record** to enable TAR on that talkgroup
- set **Keep Days** for retention
- optionally enter ignored subscriber IDs in **Ignore RIDs**

After TAR is enabled for a talkgroup, the operator must still select that resource on the main console for TAR to record live activity on it.

Ignored subscriber IDs are useful for excluding known announcement or non-essential sources from TAR on a specific talkgroup.

Enter multiple ignored subscriber IDs separated by commas, spaces, or semicolons.

Example:

```text
1001, 1002 1003;1004
```

---

# Channel Indicator

When TAR is enabled for a talkgroup, the resource card shows a small purple:

```text
TAR
```

label inside the card.

If TAR is not enabled for that talkgroup, nothing is shown.

---

# Viewer Basics

The TAR Viewer opens with the newest recordings first.

Default columns:

- Time
- Duration
- Channel
- TG
- Source ID
- Alias

Use the **Columns** button in the TAR Viewer to show or hide additional fields such as:

- Direction
- Protocol
- System
- Encryption

Viewer actions:

- Refresh
- Play
- Stop
- Open Folder
- Delete

---

# Advanced Filters

Expand **Advanced Filters** in the TAR Viewer to narrow results.

Available filters include:

- free text search across key fields
- Direction
- Protocol
- Encryption
- System
- Channel
- Talkgroup ID
- Subscriber ID
- Alias
- Start Date
- End Date

Use **Clear Filters** to reset the current filter set.

---

# Retention

TAR retention is real file cleanup.

For each TAR-enabled talkgroup, **Keep Days** controls how long recordings are kept on disk.

Behavior:

- TAR scans saved recording metadata
- TAR checks the current configured retention for that TGID
- if a recording is older than the allowed age, TAR deletes:
  - the `.wav` audio file
  - the matching `.json` metadata file

Important notes:

- `0` days means keep recordings indefinitely
- retention is based on the recording end time in UTC
- cleanup uses the **current** TAR config for that TGID at cleanup time
- if you later shorten retention for a talkgroup, older existing recordings for that talkgroup can be deleted on the next cleanup pass

Cleanup runs when:

- TAR configuration is saved
- the console loads a codeplug and TAR initializes

---

# Recording Folder Structure

TAR stores recordings in UTC-organized folders for easier browsing.

Folder layout:

```text
<TAR Root>\
  YYYY-MM-DD\
    <TalkgroupName>_TG<id>\
      HH\
        <recording>.wav
        <recording>.json
```

Example structure:

```text
TAR\
  2026-05-02\
    Dispatch 1_TG3100\
      22\
        20260502T221530.125Z_RX_System1_Dispatch1_TG3100_SRC1001_ab12cd34.wav
        20260502T221530.125Z_RX_System1_Dispatch1_TG3100_SRC1001_ab12cd34.json
```

The Viewer shows timestamps in the local system timezone, but TAR filenames and metadata timestamps are stored in UTC.

---

# Metadata

Each recording writes a `.json` file alongside the `.wav`.

Metadata includes:

- recording direction (`RX` or `TX`)
- protocol
- UTC start time
- UTC end time
- duration
- system name
- channel name
- talkgroup ID
- talkgroup name
- subscriber ID
- subscriber alias
- console ID
- console name
- encryption status
- encryption algorithm, when known
- encryption key ID, when known
- stream ID
- file size
- sample rate / bit depth / channel count

This metadata is used by the TAR Viewer and also makes the raw archive easier to search or process outside the app if needed.

---

# Notes

- TAR records to `.wav`
- TAR writes a `.json` sidecar for each recording
- TAR trims leading and trailing silence before finalizing the saved file
- TAR viewer playback uses local system audio output
- deleting a recording from the Viewer removes both the `.wav` and `.json`
