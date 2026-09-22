# Analog

Analog resources transport group voice as 8 kHz G.711 mu-law over the FNE. They can share a codeplug and FNE connection with P25, DMR, and NXDN resources. Console audio is sent in 20 ms frames; the FNE routes the call by talkgroup ID.

## Codeplug

```yaml
channels:
  - name: "Analog Clear"
    system: "System 1"
    tgid: "3005"
    mode: "analog"
    algo: "none"

  - name: "Analog Scrambled"
    system: "System 1"
    tgid: "3006"
    mode: "analog"
    algo: "none"
    scrambler_code: 2
    selectable_encryption: true
```

The system `rid` and channel `tgid` must each be between 1 and 16777215. Provision each talkgroup in the FNE rules and set `allowAnalogTraffic: true` on that FNE. Channel `keyId`, FNE key requests, DMR `slot`, and NXDN `ran` do not apply. Keep `algo: "none"` on analog channels.

`scrambler_code` is optional and defaults to `0` (off). Codes 2 through 16 select the voice-inversion frequencies reported for Kenwood radios: 2=3000, 3=3050, 4=3010, 5=4096, 6=2000, 7=2500, 8=2700, 9=3400, 10=4000, 11=2900, 12=3100, 13=2222, 14=3333, 15=2555, and 16=2333 Hz. Code 1 is unavailable because its default frequency is not established. Configure the same code on each receiving Console or radio; the code is not sent in the FNE packet. These values come from a community [Kenwood inversion-frequency list](https://forums.radioreference.com/threads/icom-kenwood-scrambler-codes.481868/) and require an over-the-air interoperability test. Voice inversion is only basic obfuscation, not secure encryption.

Set `selectable_encryption: true` on a channel with `scrambler_code` to show the card's **SELECT** control. It starts in scrambled mode and can be switched to clear mode between calls. The choice is saved for that system/talkgroup and controls both TX and local RX decoding; RX changes take effect on the next call. Analog frames have no scramble-state signaling, so each receiver must select the same mode as the transmitter. A clear/raw monitor resource on the same talkgroup can be used to compare undecoded scrambled audio; select only one of those resources at a time.

Use a distinct talkgroup ID for each protocol on the same system because Console resource settings, patches, and audio routing are keyed by system/talkgroup. Normal PTT, generated tones, audio routing, RID aliases, Event/Call History, and TAR use the same controls as digital resources.

This is network analog transport, not an RF channel configuration. Any RF-side analog gateway or hotspot must separately support the intended air interface.
