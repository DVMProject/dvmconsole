# RID Aliases

RID aliases allow the console to display human-readable names for radio IDs.

When an alias file is configured, the console will replace numeric Radio IDs (RIDs) with the defined alias when displaying:

- Call history
- Active transmissions
- Console status information

This makes it easier to identify users, radios, or dispatch positions during operation.

---

# Alias File Location

Alias files are referenced from the system configuration in the codeplug.

Example:

```yaml
aliasPath: "Full/Path/To/alias.yml"
```

This path should point to a YAML file containing RID alias definitions.

---

# Alias File Format

Alias files use a simple YAML list format.

Each entry contains:

- `alias`
- `rid`

Example:

```yaml
- alias: "Radio 1"
  rid: 1
```

This tells the console to display **"Radio 1"** whenever RID **1** appears.

Example alias file: :contentReference[oaicite:1]{index=1}

---

# Multiple Aliases

Alias files typically contain many entries.

Example:

```yaml
- alias: "Dispatch"
  rid: 1001

- alias: "Supervisor"
  rid: 1002

- alias: "Radio 12"
  rid: 1012
```

Each RID should only appear once in the alias file.

---

# How Aliases Are Used

When the console receives traffic from a radio:

```
RID: 1001
```

The console will display:

```
Dispatch
```

If no alias is defined, the numeric RID will be shown instead.

---

# Best Practices

- Keep alias names short and readable
- Use consistent naming conventions
- Avoid duplicate RIDs
- Store alias files in a predictable location
- Keep aliases updated as radios are added or reassigned

---

# Example Alias File

```yaml
- alias: "Radio 1"
  rid: 1
```

---

# Troubleshooting

### Alias not appearing

Check the following:

- The alias file path in the codeplug is correct
- The YAML formatting is valid
- The RID in the alias file matches the transmitting RID
- The console has been restarted after updating the alias file