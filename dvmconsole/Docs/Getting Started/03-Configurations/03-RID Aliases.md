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

If neither a downloaded alias nor a local alias is available, the console falls back to numeric RIDs.

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

# Downloading Aliases from the FNE

For an FNE that supports radio alias sync, add this option to its existing system entry:

```yaml
    syncRadioAliases: true
```

The option defaults to `false`. Enable it only for systems with the new FNE radio alias-sync support; the general R06A00 minimum does not guarantee this feature is present.

- The console requests a complete alias list after connecting or reconnecting.
- Use **Tools > FNE Connection Manager > Sync Aliases** to refresh it manually. The **RID Aliases** column shows progress, the downloaded count, or failure status.
- Downloaded aliases take priority for that system. RIDs absent from the downloaded list still use its local `aliasPath` YAML file when available.
- Complete downloads are cached per system under `%AppData%\DVMProject\dvmconsole\RadioAliases`. An alternate `--userprofile` directory gets its own cache.
- Missing packets, rejected requests, invalid data, and timeouts keep the previous aliases. An alias-sync failure does not restart the FNE connection. Requests time out after 30 seconds.
- New calls use the updated names in channel cards, history, and TAR. Existing history rows and recordings are not renamed.

## FNE Requirements

The FNE sends the file configured under `system.radio_alias.file`, not its RID access-control list. The file must exist, be readable, and contain data. The currently supported FNE handler does not safely handle a missing or zero-byte file; a comments-only file can represent an empty list.

The FNE file uses comma-separated entries, not the console's local YAML format:

```text
1001,Dispatch,
1002,Portable 2,
```

Blank lines and lines starting with `#` are allowed. Use UTF-8 text and avoid commas inside alias names.

The request uses the existing logged-in peer through FNECore. Allow UDP to the FNE metadata port (configured traffic port plus one); responses arrive on the existing traffic connection. If syncing fails, check **Help > Debug Logs**, FNE support, the configured alias file, and metadata-port reachability.

---

# Operational Notes

- Alias files are configured per system.
- A RID may have different meanings on different systems, so keep alias files system-specific when needed.
- If source aliases stop appearing but calls still log correctly, verify the alias file path and format.
- Alias display does not change the actual source ID sent over the network.
