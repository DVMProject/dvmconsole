# RID Aliases

RID aliases allow the console to display human-readable names for radio IDs.

Aliases can appear in places such as:

- source alias on channel cards
- call history
- receive activity display
- logs or status views that resolve subscriber IDs

---

# Alias File Location

Reference an alias file from a system entry in the codeplug:

```yaml
systems:
  - name: "System 1"
    aliasPath: "Full/Path/To/alias.yml"
```

If no alias file is configured or the file is unavailable, the console falls back to numeric RIDs.

---

# Alias File Format

Alias files use a simple YAML list.

```yaml
- alias: "User 1"
  rid: 101

- alias: "User 2"
  rid: 102

- alias: "User 3"
  rid: 103
```

Fields:

- `alias`: display name.
- `rid`: radio ID.

Each RID should appear only once in the file.

---

# Operational Notes

- Alias files are configured per system.
- A RID may have different meanings on different systems, so keep alias files system-specific when needed.
- If source aliases stop appearing but calls still log correctly, verify the alias file path and format.
- Alias display does not change the actual source ID sent over the network.
