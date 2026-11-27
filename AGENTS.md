# AGENTS.md

This file applies to the entire Console repository unless a more specific
`AGENTS.md` applies below it. Treat `fnecore/` as a separately maintained
submodule, not as ordinary Console source.

## Project priorities

DVMConsole is a Windows WPF dispatch application using C#, .NET, NAudio, and
FNECore. Correct call state, audio routing, protocol compatibility, and cleanup
matter more than stylistic modernization.

- Inspect the relevant source, callers, examples, and documentation first.
- Preserve existing behavior unless the requested change requires otherwise.
- Keep patches focused. Do not mix a bug fix with broad formatting or renaming.
- Reproduce reported failures before changing unrelated settings or code.
- Do not invent protocol fields, algorithms, configuration keys, or capabilities.
- Follow the hobbyist-use and safety restrictions in `README.md`.

## Repository layout

- `dvmconsole/`: WPF application, windows, controls, audio, and call handling.
- `dvmconsole/MainWindow.*.cs`: partial classes grouped by mode or feature.
- `dvmconsole/FneSystemBase.*.cs`, `PeerSystem.cs`, and `FneSystemManager.cs`:
  Console integration with FNECore.
- `dvmconsole/Controls/`: resource cards and reusable UI controls.
- `dvmconsole/Codeplug.cs`, `SettingsManager.cs`, and `ResourceIdentity.cs`:
  configuration, saved user state, and resource identity.
- `dvmconsole/Docs/`: user documentation also displayed inside the application.
- `configs/`: annotated example configurations.
- `fnecore/`: Git submodule containing shared network and protocol handling.
- `.github/`: issue and pull-request templates.

Do not edit generated files, build outputs, logs, native binaries, or local
runtime configurations as a substitute for fixing source. Local test folders
may exist without being tracked; do not assume they are a repository test suite
or add them wholesale to a commit.

## Ownership and compatibility

- Shared packet framing, signaling, codec, and protocol-crypto behavior belongs
  in FNECore. Console owns UI, audio-device integration, operator actions, and
  application state. Extend existing APIs rather than duplicating wire logic.
- Inspect both repositories' Git status before investigating an integration bug.
  Preserve existing changes and record the exact Console and FNECore revisions
  used for comparisons.
- Do not edit, update, or stage the FNECore submodule unless the task authorizes
  it. Use an isolated checkout when reviewing an upstream change. Keep requested
  pin updates separate from unrelated Console changes.
- Use `ResourceIdentity` and existing lookup helpers. Display names are not
  identities, and TGID alone is not unique across systems. Preserve the current
  system/talkgroup key contract; include mode, slot, or stream context where the
  owning API requires it rather than inventing a competing key format.
- Preserve configuration names, defaults, saved settings, and migration paths.
  Do not silently discard existing AppData settings to accommodate a new feature.

## C# and XAML style

- Match the surrounding file. Most classes use four-space indentation,
  next-line braces, PascalCase members, and camelCase private state. Some newer
  files use file-scoped namespaces; do not convert unrelated files.
- Preserve existing `using` groups, partial-class boundaries, and section
  comments. Put mode-specific changes in their existing mode-specific files.
- Match local use of explicit types, `var`, nullable annotations, and constants.
  Do not enable nullable checking or change the target framework project-wide
  as part of an unrelated fix.
- Follow existing XML documentation for types and methods. Document meaningful
  parameters, return values, units, ownership, and non-obvious constraints; do
  not add empty summaries or comments that merely repeat the code.
- Preserve SPDX, license, and attribution headers. Do not remove or rewrite
  existing contributors. For new files, follow the established Console header;
  use the generic DVMProject authors credit when no individual attribution is
  specified rather than inventing a name.
- Match current XAML bindings, resources, themes, sizing, and event patterns.
  Do not introduce a new UI framework or rewrite the application into another
  architecture for a local feature.
- Check `.editorconfig`, but do not treat its suppressed warnings as proof of
  correctness. Observe failures from background tasks even where CS4014 is
  suppressed.

## Threading, audio, and call state

- Marshal WPF state changes through the owning Dispatcher. Keep network waits,
  audio processing, and expensive work off the UI thread.
- Avoid synchronous Dispatcher calls while holding locks needed by UI handlers.
  Keep lock ordering and state ownership explicit.
- Prefer awaitable task lifecycles. Event handlers may require `async void`, but
  new background workers should expose completion and cancellation.
- Treat disconnect, timeout, cancellation, and window shutdown as normal paths.
  Cleanup must still run if sending a terminator fails. Do not leave PTT/page
  indicators, streams, tone sessions, or audio devices stuck.
- Guard against stale callbacks affecting a replacement call or connection.
  Validate resource and stream identity before applying asynchronous results.
- Preserve audio format, frame size, sequence, pacing, and call-end behavior.
  For analog, inspect `AnalogTxCall` and FNECore's packet codec before changing
  buffering. Do not hide a backlog problem by merely increasing queue size.
- Keep RX playback, TX capture, TAR, patch forwarding, and local alert audio
  distinct. Local warning tones must not enter transmitted audio.
- Exercise individual, MSEL, and patch-group actions separately. Disabled or
  RX-only resources must not acquire transmit state through a group shortcut.
- Preserve the operator-only scope of the configured transmit timeout unless
  the task explicitly changes it. Check simultaneous PTT and release paths.

## Configuration and secrets

The README and example YAML include DVMProject's AI/LLM configuration notice.
Before generating or modifying a user configuration, explain that DVMProject
support will not troubleshoot or validate AI-generated or AI-modified configs.
Preserve the notice; do not assume Host's configuration generator supports
Console codeplugs.

- Use `Codeplug.cs` and `configs/codeplug.example.yml` as the configuration
  contract. Preserve mixed key naming exactly; do not normalize YAML names.
- Match existing YAML indentation and comments. Document units, valid values,
  defaults, and interactions when adding an option.
- Update relevant loaders, examples, and built-in docs together. Validate actual
  loading, not just YAML syntax. Do not change IDs, endpoints, or credentials
  incidentally while editing examples.
- Preserve codeplug-relative path resolution and Windows absolute paths. Use
  existing path helpers rather than concatenating separators or relying on the
  process working directory.
- Local keys use encrypted `.ekc` containers. Do not restore `keys.clear` support
  or save container passwords in plaintext. Preserve the existing Windows
  credential-protection behavior.
- Keep key requests and cached keys scoped to the proper system. Do not infer
  secure RX eligibility solely from the channel's configured TX key ID.
- Do not present analog inversion as cryptographic encryption or assume it uses
  the digital key cache. Unsupported mode/algorithm combinations must fail
  explicitly rather than silently transmit clear.
- Use existing logging facilities. Include useful system/resource/stream context,
  but never log keys, passwords, preshared keys, decrypted container contents,
  or sensitive packet payloads, including in debug output.

## Build and packaging

Use Windows with the .NET 10 SDK and Windows desktop tooling. The project
currently targets `net10.0-windows7.0` with x86 as its default architecture;
verify the project and README before assuming those requirements are unchanged.

From the repository root:

```powershell
dotnet build .\dvmconsole\DVMConsole.csproj -c Release -p:Platform=x86
```

For a fresh clone, initialize submodules at their recorded revisions. Do not
use `git submodule update --remote` as routine build setup. In a working checkout,
inspect submodule changes before any initialization or update command.

`libvocoder.DLL` is a native runtime dependency and must match the process
architecture. A successful managed build does not establish that it is present
or that audio works. Distribute the complete intended build/publish output,
including dependency DLLs, `.deps.json`, and `.runtimeconfig.json`, not just the
EXE. Exclude private configs, keys, profiles, recordings, and logs from packages.

## Testing and operational safety

- Use focused local tests, synthetic audio, mocks, or loopback first. A successful
  build or codec round trip alone does not prove interoperability or audio quality.
- Check current tracked test tooling before choosing commands. Do not claim
  `dotnet test` exercised behavior if there are no applicable test projects.
- Keep ad hoc harnesses outside tracked application source. Do not import local
  test artifacts into the repository without an explicit request.
- GUI tests must use an isolated profile and test codeplug. The application has
  a `--userprofile` directory override; verify it before constructing settings
  objects. Never overwrite the normal `UserSettings.json` to set up a test.
- Do not launch against remembered production systems by default. Get explicit
  approval before live FNE connections, RF tests, or transmissions. Do not launch
  a test instance until its endpoints, profile, and automatic behavior are known.
- SSH requires approval for each connection, identifying destination, account,
  and purpose. Connection approval does not authorize remote changes.
- For connection changes, test both traffic and metadata, failed reconnects,
  backoff, HA where applicable, and shutdown with work in flight.
- For call/audio changes, test RX and TX, release during active work, cancellation,
  later-call recovery, and same TGID on different systems. Verify UI state as well
  as emitted packets. Include raw-versus-descrambled listening for inversion work.
- For keys/configuration, test missing, wrong, valid, and changed credentials,
  request deduplication, saved-state compatibility, and cancellation as applicable.
- State exactly what ran and what remains manual, hardware-dependent, or blocked.
  Never label simulated tests as live FNE or on-air verification.

## Change completion

Review the final diff and Git status for unrelated edits, generated artifacts,
secrets, and accidental submodule changes. Report changed files, verification,
and remaining risks. Do not commit, push, open a PR, post a review, or merge
without explicit authorization. Preserve unrelated staged and unstaged work.
