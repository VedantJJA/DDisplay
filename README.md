# DDisplay

Use an Android phone or tablet as a real second monitor for Windows over USB or Wi-Fi.

DDisplay creates a genuine virtual monitor on Windows using the
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (VDD),
captures its framebuffer, encodes it to H.264/H.265, and streams it to the Android app.
Touch events on the Android device are sent back and injected into Windows at the correct
virtual-display coordinates, giving you a functioning touchscreen second monitor.

---

## How it works

```
Windows Host
  VDD (virtual monitor) --> DXGI capture --> H.264 encoder --> TCP stream
                                                                    |
                                               USB (ADB tunnel) or Wi-Fi LAN
                                                                    |
Android Client                                              TCP client
  H.264 decoder (MediaCodec) --> SurfaceView render
  onTouchEvent --> control channel --> Windows SendInput
```

Two transports, selected automatically by priority:

1. **USB -- ADB reverse tunnel** (primary): USB debugging on, zero network config.
2. **USB tethering** (secondary): no ADB needed, phone enables USB tethering in Settings.
3. **Wi-Fi** (fallback): same LAN, discovered via mDNS; requires one-time pairing code.

---

## Requirements

### Windows host
- Windows 10 21H2 or later (x64 or ARM64)
- [Virtual Display Driver + VDC](https://github.com/VirtualDrivers/Virtual-Display-Driver)
  installed (the app will prompt you on first run)
- Microsoft Visual C++ Redistributable (installed by VDC)
- .NET 8 runtime

### Android client
- Android 8.0 (API 26) or later
- For USB mode: Developer Options > USB Debugging enabled

---

## Repository layout

```
/
+-- windows/
|   +-- DDisplay.sln
|   +-- DDisplay.App/          # WPF tray application
|   +-- DDisplay.Core/         # Capture, encode, transport, input injection
|   +-- DDisplay.VddControl/   # VDD / vdd_settings.xml automation
|   +-- DDisplay.Tests/        # Unit tests
+-- android/
|   +-- app/                   # Single-module Kotlin app
|   +-- settings.gradle.kts
+-- protocol/
|   +-- SPEC.md                # Wire protocol v1 specification
+-- docs/
|   +-- phase0-findings.md     # Phase 0 empirical investigation results
+-- plan.md                    # Original implementation plan
```

---

## Build phases

| Phase | Goal |
|-------|------|
| 0 | De-risk: VDD reload mechanism, Desktop Duplication on virtual output, ADB tunnel |
| 1 | Virtual display lifecycle (add / remove via vdd_settings.xml) |
| 2 | Windows capture pipeline with local preview |
| 3 | Encode (Windows) + decode (Android) loopback test |
| 4 | Wi-Fi transport MVP end-to-end |
| 5 | USB / ADB transport |
| 6 | USB tethering transport |
| 7 | Touch input back-channel |
| 8 | mDNS discovery, pairing, transport auto-selection |
| 9 | Performance and UX polish |
| 10 | Packaging (MSIX / signed APK) |

See [plan.md](plan.md) for full details and acceptance criteria per phase.

---

## Building

### Windows

Requirements: Visual Studio 2022 with .NET 8 SDK and Desktop workload.

```
cd windows
dotnet restore DDisplay.sln
dotnet build DDisplay.sln
dotnet test DDisplay.Tests/DDisplay.Tests.csproj
```

### Android

Requirements: Android Studio Hedgehog or later, JDK 17.

```
cd android
./gradlew assembleDebug
```

---

## Wire protocol

See [protocol/SPEC.md](protocol/SPEC.md).

---

## License

MIT
