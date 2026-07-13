# AvaPhone

[日本語](README.md) | **English**

Control a smartphone gimmick on your VRChat avatar from your real phone.
Switch a screen on your physical phone and the phone on your avatar switches too — battery level included.

> Formerly known as "Avatar Smartphone Link" (working title in spec v0.1)

```
Real smartphone (Flutter app)
      │  WebSocket / JSON / same LAN
      ▼
PC relay app: VrcPhoneRelay (C# / .NET 8)
      │  OSC / OSCQuery (loopback only)
      ▼
VRChat (PC) ── Avatar Parameters ──▶ Avatar FX Animator ──▶ Phone gimmick
```

## Features

| Feature | Status |
|---|---|
| Show / stow the phone on the avatar from your real phone | ✅ Done |
| Screen switching (8 screens: Lock / Home / Notifications / Call / Camera / Media / Settings / Connection Error) | ✅ Done |
| Hold poses (6 poses: stowed on hip / right hand / left hand / at ear / selfie / both hands, 0.2 s blend) | ✅ Done |
| Real battery level mirrored on the avatar in 11 steps | ✅ Done (mobile app in progress) |
| Notification pop, incoming call / in-call / media playback effects | ✅ Done |
| Changes made in VRChat (Expression Menu) sync back to the phone | ✅ Done |
| State visible to other players (synced parameters, 52 bits) | ✅ Done |
| Safe behavior on disconnect (detected within 6 s, transient states reset) | ✅ Done |
| Smartphone app (iOS / Android) | 🚧 In progress |

### Non-goals (by design)

No screen mirroring, no video/photo/arbitrary-text transfer, no notification contents, no call audio.
VRChat avatar parameters only carry numbers and booleans, so AvaPhone switches between
pre-made screen materials by index. No personal data (contacts, message bodies, location, etc.) is ever transmitted.

## Repository layout

| Directory | Contents | Tech |
|---|---|---|
| [`docs/`](docs/) | Spec v0.1 (Japanese), [protocol definition (single source of truth)](docs/protocol.md), [spec errata](docs/spec-errata.md) | - |
| [`relay-app/`](relay-app/) | PC relay app **VrcPhoneRelay** — WebSocket server + OSC/OSCQuery client | C# / .NET 8 / Kestrel |
| [`mobile-app/`](mobile-app/) | Smartphone app **AvaPhone** (in progress) | Flutter / Riverpod 3 |
| [`unity-avatar/`](unity-avatar/) | Avatar gimmick generator **AvaPhone Gimmick** (VPM package, editor extension) | Unity 2022.3 / VRChat SDK 3.7+ |

## Requirements

- **PC**: Windows, VRChat (PC version) with OSC enabled (Action Menu → OSC → Enabled), plus the relay app
- **Avatar**: Avatars 3.0 with at least 52 bits of free Expression Parameter memory
- **Unity**: 2022.3.22f1 + VRChat SDK (com.vrchat.avatars >= 3.7.0) + VCC
- **Phone**: iOS / Android on the same LAN as the PC

## Setup

### 1. Install the gimmick on your avatar (Unity)

1. In VCC: Settings → Packages → **Add Local Package**, select `unity-avatar/Packages/net.transit.phone-gimmick`, then add it to your avatar project
2. In Unity, open **Tools → Phone Gimmick → Setup**, pick your avatar and click generate.
   This creates the Expression Parameters (52 bits), 9 FX layers, a "Phone" Expressions Menu,
   a placeholder phone model and 5 pose anchors — non-destructively by default (existing assets are copied before editing)
3. Adjust the `PhoneAnchor_*` transforms to fit your avatar, then upload

Details and Av3Emulator test procedure: [unity-avatar/docs/setup-guide.md](unity-avatar/docs/setup-guide.md)

### 2. PC relay app

```powershell
# Publish a single-file exe
cd relay-app
dotnet publish src/VrcPhoneRelay.Server -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish

# Run it (or for development: dotnet run --project src/VrcPhoneRelay.Server)
artifacts\publish\VrcPhoneRelay.Server.exe
```

On first launch (no paired devices) a pairing QR code is shown automatically.
VRChat is discovered via OSCQuery (mDNS), with automatic fallback to the fixed ports (9000/9001)
where mDNS is unavailable.

To connect from a real phone, allow the WebSocket port through the firewall:

```powershell
netsh advfirewall firewall add rule name="AvaPhone Relay" dir=in action=allow protocol=TCP localport=27810 profile=private
```

Configuration, console commands and VRChat-free testing (FakeVrchat): [relay-app/README.md](relay-app/README.md)

### 3. Smartphone app (in progress)

Pair by scanning the QR code; reconnection is automatic afterwards. Built with Flutter for iOS/Android.

## Protocol overview

The authoritative definition is [docs/protocol.md](docs/protocol.md) (Japanese); all three implementations follow it.

- **Transport**: WebSocket (`ws://<PC>:27810/ws`), UTF-8 JSON, 2 s heartbeat / 6 s disconnect detection
- **Auth**: first pairing uses a one-time token from the QR code (128-bit, 5 min TTL, single-use);
  reconnection uses the issued deviceId + secret (stored server-side as SHA-256 hash)
- **Source of truth**: the last value echoed by VRChat. A phone action flows
  `parameter.set` → OSC send → VRChat echo → `parameter.ack (applied)`, or `timeout` after 1.5 s
- **Avatar parameters** (prefix `Phone/`, 52 of 256 bits):

| Parameter | Type | Range | Meaning |
|---|---|---|---|
| `Phone/Visible` | Bool | - | Phone body visible |
| `Phone/Connected` | Bool | - | Real phone connected (written only by the relay) |
| `Phone/Locked` | Bool | - | Lock state |
| `Phone/Page` | Int | 0-7 | Current screen |
| `Phone/Pose` | Int | 0-5 | Hold pose |
| `Phone/Battery` | Int | 0-10 | Battery level step |
| `Phone/CallState` | Int | 0-4 | Call state |
| `Phone/MediaState` | Int | 0-4 | Media state |
| `Phone/NotifyType` | Int | 0-4 | Notification effect type |
| `Phone/EventToggle` | Bool | - | Notification trigger (flip-type, rate-limited to 1/s) |

## Development

```powershell
# Relay app: build + all tests (no VRChat needed — E2E runs against FakeVrchat)
cd relay-app
dotnet test        # 110 tests (unit + E2E integration)

# Manual testing with the VRChat impostor
dotnet run --project tools/FakeVrchat            # terminal 1
dotnet run --project src/VrcPhoneRelay.Server --Relay:OscMode=Fixed  # terminal 2
```

- The E2E suite spins up the whole server + FakeVrchat + a real WebSocket client and exercises
  pairing, acks, clamping, rate limiting, the disconnect policy and VRChat re-discovery
- The OSC codec is a small in-house implementation (i/f/s/T/F + bundle expansion) with
  known-byte-vector round-trip tests

## Status

- [x] Stage 0: repository & protocol definition
- [x] Stage 1: PC relay app (M0-M6, 110 tests)
- [x] Stage 2: Unity editor extension (code complete; in-editor verification happens in Stage 4)
- [ ] Stage 3: smartphone app (in progress)
- [ ] Stage 4: integration testing (real VRChat + published test avatar + real phone)

## Security & privacy

- LAN-only; no internet connectivity by design in the initial version
- Only paired devices can operate the avatar; pairing mode starts only on explicit user action
- OSC traffic never leaves the PC (loopback); undefined parameters and out-of-range values are not forwarded
- Tokens and secrets are never logged; the only device data transmitted is battery level and charging state

## License

TBD (listed as an open decision in the spec). Will be added once the distribution policy is decided.
