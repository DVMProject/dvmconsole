# DMR

DMR resources use conventional clear group voice through the FNE and can share a codeplug with P25, NXDN, and analog resources.

## Codeplug

RID and TGID must be between 1 and 16777215. Set `slot` to 1 or 2 and provision that talkgroup/timeslot in the FNE rules.

```yaml
channels:
  - name: "DMR TS1"
    system: "System 1"
    tgid: "3010"
    mode: "dmr"
    slot: 1
    algo: "none"

  - name: "DMR TS2"
    system: "System 1"
    tgid: "3011"
    mode: "dmr"
    slot: 2
    algo: "none"
```

Use distinct talkgroup IDs for resources within a system: console settings, patches, and audio routing identify resources by system/talkgroup.

## Operation

Normal PTT, multi-select PTT, patches, tones, audio routing, RID aliases, history, and TAR use the existing console controls.

FNECore owns call state, framing, embedded signaling, and clear-voice extraction. Console supplies the vocoder, audio devices, packet pacing, UI, and recording. This follows the same library/application split as P25 and NXDN.

Each voice packet carries three 20 ms AMBE codewords. Call end completes the current six-burst superframe with at most five silence packets, then sends one terminator. Cancelled audio is discarded rather than transmitted later.

A receiver joining mid-call waits for valid embedded call information before playing audio.

## Current Limits

Encrypted DMR is temporarily unavailable while its replacement is developed. Channels with a non-clear `algo` cannot transmit, even if SELECT was previously set to clear. Explicitly configure a separate `algo: "none"` channel for clear testing.

DMR keys are not requested from the FNE or loaded from local key files. Detected encrypted or unsupported protected signaling is muted, never sent to the vocoder as clear audio. P25 and NXDN encryption are unchanged.

Individual calls, packet data, OTAR, and trunking control-channel services are not implemented by this console path. Console-to-console and radio interoperability require separate live testing.
