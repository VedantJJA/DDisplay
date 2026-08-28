# Implementation Plan: Android-as-Second-Monitor for Windows

**Project codename:** `DDisplay` (rename as desired)
**Audience:** AI coding agents / dev team implementing this end-to-end
**Stack:** Windows host = .NET (C#, WPF/WinUI 3); Android client = Android Studio (Kotlin); Display virtualization = [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (IddCx-based)
**Transports:** USB (primary, via ADB or USB tethering) → falls back to Wi‑Fi (same LAN)

---

## 1. What we're building

A Windows service/app creates a real virtual monitor via the VDD (Indirect Display Driver). Windows treats it exactly like a physical second monitor — you can drag windows onto it, set it as extended display, pick its resolution/refresh rate. A Windows companion app captures that virtual monitor's framebuffer, encodes it to H.264/H.265, and streams it to an Android app, which decodes and renders it full-screen. Touch input on the Android device is sent back and injected into Windows at the correct virtual-display coordinates, so the phone/tablet behaves like a touchscreen second monitor.

Two transports, selected automatically:
1. **USB (primary)** — either an ADB reverse TCP tunnel (uses existing USB debugging, no extra setup) or USB tethering (RNDIS network adapter, no ADB needed). Lower latency, no Wi‑Fi dependency, doesn't fight for household bandwidth.
2. **Wi‑Fi (fallback)** — plain TCP/UDP over LAN, discovered via mDNS/NSD, used when no cable is present or USB negotiation fails.

---

## 2. High-level architecture

```
┌─────────────────────────── Windows Host (.NET) ───────────────────────────┐
│                                                                             │
│  ┌────────────────┐   ┌──────────────────┐   ┌───────────────────────┐    │
│  │ VDD Controller  │   │ Capture Engine    │   │ Encoder (Media        │    │
│  │ (manages virtual│──▶│ (Desktop          │──▶│ Foundation HW H.264/  │    │
│  │ display via     │   │ Duplication API   │   │ H.265, NVENC/QSV/AMF, │    │
│  │ vdd_settings.xml│   │ targeting the     │   │ or OpenH264 fallback) │    │
│  │ + VDC install)  │   │ virtual monitor)  │   └───────────┬───────────┘    │
│  └────────────────┘   └──────────────────┘               │                │
│                                                             ▼                │
│  ┌────────────────┐   ┌──────────────────┐   ┌───────────────────────┐    │
│  │ Input Injector  │◀──│ Control Channel   │◀──│ Transport Manager     │    │
│  │ (SendInput /    │   │ (JSON messages:   │   │ (USB-ADB / USB-       │    │
│  │ mouse_event /   │   │ handshake, touch, │   │ tether / Wi-Fi;       │    │
│  │ SetCursorPos)   │   │ pairing, control) │   │ auto-selects + fails  │    │
│  └────────────────┘   └──────────────────┘   │  over)                │    │
│                                                └───────────┬───────────┘    │
└────────────────────────────────────────────────────────────┼──────────────┘
                                                               │
                                          USB (ADB tunnel / RNDIS) or Wi-Fi LAN
                                                               │
┌───────────────────────────── Android Client ─────────────────────────────┐
│                                                                             │
│  ┌────────────────┐   ┌──────────────────┐   ┌───────────────────────┐   │
│  │ Transport Layer │──▶│ Decoder           │──▶│ Renderer (SurfaceView/│   │
│  │ (Socket over    │   │ (MediaCodec HW    │   │ TextureView, full-    │   │
│  │ localhost:PORT  │   │ H.264/H.265       │   │ screen immersive)     │   │
│  │ via adb reverse,│   │ decode)           │   └───────────────────────┘   │
│  │ or TCP/mDNS)    │   └──────────────────┘                                │
│  │                 │   ┌──────────────────┐                                │
│  │                 │──▶│ Touch Capture →   │                                │
│  │                 │   │ Control Channel   │                                │
│  └────────────────┘   └──────────────────┘                                │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Key facts about the Virtual Display Driver (verified from the repo)

- It's a fork of `IddSampleDriver`, an IddCx (Indirect Display Driver) — the virtual monitor is a *real* Windows display device, not a mirrored/software overlay, so normal capture APIs work on it like any monitor.
- Distributed as a signed installer plus a companion GUI app called **Virtual Driver Control (VDC)** (also its own repo: `VirtualDrivers/Virtual-Driver-Control`). VDC installs the driver and lets you add/remove virtual monitors and resolutions.
- **Configuration is file-based**: settings (including custom resolutions/refresh rates, EDID, per-monitor options) live in `C:\VirtualDisplayDriver\vdd_settings.xml`. This is the automation hook — the Windows host app can edit this XML programmatically (add a monitor entry with the exact resolution/refresh rate reported by the connected Android device) instead of requiring the user to click through VDC each time.
- Also installable via winget: `winget install --id=VirtualDrivers.Virtual-Display-Driver -e`.
- Requires the Microsoft Visual C++ Redistributable.
- ARM64 Windows may require test-signing to be enabled for the driver.
- No documented local RPC/IPC API beyond the settings file + driver reload — **Phase 0 must confirm** whether toggling a monitor on/off at runtime requires (a) a device-restart/re-enable via `pnputil`/Device Manager COM calls, (b) a signal file VDC already uses that can be reused, or (c) contacting the maintainers / reading the VDC source for its automation entry point. Do not assume; verify empirically before building the rest of the pipeline on top of it.

---

## 4. Repository / solution layout

```
/second-screen/
├── windows/
│   ├── SecondScreen.sln
│   ├── SecondScreen.App/            # WPF/WinUI 3 UI + tray app
│   ├── SecondScreen.Core/           # capture, encode, transport, input-inject (class library)
│   ├── SecondScreen.VddControl/     # VDD/vdd_settings.xml automation wrapper
│   └── SecondScreen.Tests/
├── android/
│   ├── settings.gradle.kts
│   ├── app/                         # single-module Kotlin app
│   │   └── src/main/java/.../{transport, decode, render, input, ui}/
│   └── ...
├── protocol/
│   └── SPEC.md                      # wire protocol, versioned
└── docs/
    └── (this file, plus per-phase notes)
```

---

## 5. Wire protocol (v1)

Keep it simple and TCP-based for v1 so the exact same protocol works over the ADB tunnel, USB tethering, and Wi‑Fi without branching logic — optimize to UDP/RTP later if latency demands it.

**Two logical streams, one TCP connection (length-prefixed framing), or two ports if simpler to implement first:**

- **Control channel** — JSON messages, newline or length-prefixed:
  - `hello` (from Android): device model, screen size (px), density (dpi), supported codecs, max decode resolution.
  - `hello-ack` (from Windows): chosen resolution/refresh rate, codec, session id.
  - `pair-request` / `pair-confirm`: pairing code shown on Windows, entered on Android (Wi‑Fi path only; ADB/USB path treats USB-debugging authorization as the trust boundary).
  - `touch`: `{type: down|move|up, x, y, pointerId, ts}` (normalized 0–1 coordinates, Windows maps to the virtual display's pixel space).
  - `key` (optional v2): keyboard passthrough if Android soft/hard keyboard input is desired.
  - `heartbeat` / `heartbeat-ack`.
  - `bye`: graceful disconnect.
- **Media channel** — binary frames: `[4-byte length][1-byte flags: keyframe?][payload: H.264/H.265 NAL units or Annex-B stream]`.

**Versioning:** put a `protocolVersion` field in `hello`/`hello-ack` from day one so client/host can reject/upgrade gracefully.

---

## 6. Transport details

### 6.1 USB — Option A: ADB reverse tunnel (primary recommendation, ship first)

- Requires: USB debugging enabled on the phone, device authorized once (standard ADB prompt), and `adb.exe` available to the Windows app (bundle Google's `platform-tools` binaries in the app, or shell out to a system-installed ADB).
- Windows host, on detecting a device via `adb devices`, runs:
  `adb -s <serial> reverse tcp:7878 tcp:7878`
  This maps the Android device's `localhost:7878` to the Windows host's `localhost:7878` through the existing USB/ADB link — no network config, no IP addresses, works even with no internet/LAN at all.
- Android app just connects a `Socket` to `127.0.0.1:7878`.
- Windows app runs the TCP server on `127.0.0.1:7878` (and optionally `0.0.0.0` for the Wi‑Fi path — see below).
- Pros: zero user network configuration, secure by construction (ADB auth), works with mobile data off.
- Cons: requires ADB installed/bundled, requires "USB debugging" toggle which some users find intimidating, requires device authorization dialog on first connect.

### 6.2 USB — Option B: USB tethering (ship as alternative/fallback within "USB" mode)

- User enables "USB tethering" in Android settings. This brings up an RNDIS (or NCM) virtual network adapter on the Windows side and assigns the phone an IP on that point-to-point subnet.
- No ADB required. Windows host detects the new network adapter (watch for network interface change events / a new adapter matching known Android RNDIS vendor IDs), then either:
  - broadcasts/announces itself on that subnet the same way it does for Wi‑Fi (see 6.3), or
  - the Android app simply tries `adb`-free discovery: since it's a point-to-point link, the gateway IP is fixed/predictable and can be probed directly.
- This path reuses the exact same TCP client/server code as Wi‑Fi mode — the only difference is *which interface* the connection happens over. Treat "USB tethering" as "Wi‑Fi transport, but the LAN happens to be a USB-provided one."

### 6.3 Wi‑Fi (fallback)

- Both devices on the same LAN/subnet (same router or hotspot).
- **Discovery:** Windows advertises via mDNS/DNS-SD (e.g., `_secondscreen._tcp.local`) using a library such as `Zeroconf`/`Makaretu.Dns` on .NET, or Windows' built-in mDNS support; Android discovers via `NsdManager` (Android's built-in NSD/mDNS API).
- **Fallback-fallback:** manual entry of IP + pairing code if mDNS is blocked (common on guest Wi‑Fi/enterprise networks with client isolation — call this out to the user).
- **Pairing:** since Wi‑Fi has no inherent trust boundary like USB debugging authorization does, require a pairing code (4–6 digits, shown on the Windows app, entered once per device on Android) before accepting the media stream. Persist trusted device certs/keys after first pairing so reconnects are automatic.
- **Security:** wrap the TCP socket in TLS with a self-signed cert generated per-install, pinned on first pairing (simple TOFU model) so a rogue device on the LAN can't casually MITM the stream.

### 6.4 Auto-selection & handover

- Priority order: **ADB-USB → USB-tethering → Wi‑Fi**.
- Windows app runs a `TransportManager` that continuously watches: (a) `adb devices` output, (b) new network adapters matching Android RNDIS signatures, (c) mDNS browse results. It picks the highest-priority available transport, and re-evaluates on device add/remove events (e.g., cable unplugged → attempt seamless handover to Wi‑Fi if the same paired device is visible there, otherwise pause the session and show "reconnect" in the UI).
- Design the `ITransport` interface once (`Connect`, `Disconnect`, `Send(ControlMessage)`, `SendFrame(byte[])`, events for `Connected`/`Disconnected`/`DataReceived`) so USB and Wi‑Fi are interchangeable implementations and the capture/encode/render pipelines never know which one is active.

---

## 7. Windows app components (detailed)

### 7.1 VDD Controller (`SecondScreen.VddControl`)
- Detect whether VDD/VDC is installed (check for `C:\VirtualDisplayDriver\vdd_settings.xml` and the driver in Device Manager via WMI `Win32_PnPEntity`).
- If missing: prompt to download/run the VDC installer (either bundle it, per its MIT license, or fetch the latest release from GitHub at first-run — confirm license terms allow bundling before choosing).
- On session start: read `vdd_settings.xml`, add/update a monitor entry matching the connected Android device's resolution and aspect ratio (from the `hello` message) and a sensible refresh rate (e.g., 60 Hz, or match device's if reported), write the file back, and trigger the driver to pick up the change (Phase 0 must determine the correct trigger — likely a driver re-enable via `pnputil` or pushing a signal VDC itself uses; inspect VDC's binary/behavior or ask in the project's Discussions/Wiki if undocumented).
- On session end: optionally remove/disable the virtual monitor to avoid leaving a dangling display, or leave it (configurable) so the user doesn't lose window layouts on the "second monitor" between sessions.
- Expose an `IVirtualDisplayService` interface so this can be mocked/stubbed while other components are developed in parallel (important — this is the riskiest/least-documented dependency, don't let it block everything else).

### 7.2 Capture Engine
- Use **DXGI Desktop Duplication API** (via `Vortice.Windows` or `SharpDX` bindings) targeting specifically the output that corresponds to the new virtual monitor (`IDXGIOutput` matching its device name/handle).
- Alternative/newer option: **Windows.Graphics.Capture** (WinRT capture API) — simpler API, but confirm it can target a specific non-primary, possibly-headless virtual output; Desktop Duplication is the more proven route for a headless/virtual adapter.
- Capture loop pushes frames (as GPU textures, ideally without CPU readback) directly into the encoder to avoid extra copies.

### 7.3 Encoder
- Preferred: hardware encode via **Media Foundation Transform (MFT)** for H.264 (broad Android decode support) with H.265 as an opt-in for bandwidth savings on devices that support it (check `hello`'s `supportedCodecs`).
- Use whatever GPU vendor encoder is present (NVENC/QuickSync/AMF) through Media Foundation's hardware MFTs — avoid vendor-specific SDKs for v1 to keep it hardware-agnostic.
- Fallback: software encoder (e.g., OpenH264) if no hardware MFT is available, with a lower default resolution/bitrate.
- Expose tunables: resolution, target bitrate, keyframe interval, encoder preset (latency-optimized) — surfaced in the settings UI.

### 7.4 Transport Manager & servers
- Implements `ITransport` for `AdbUsbTransport`, `UsbTetherTransport` (can likely share a `TcpLanTransport` base with `WifiTransport`), and `WifiTransport`.
- Bundles or locates `adb.exe`; wraps `adb devices`, `adb reverse`, `adb -s <serial> reverse --remove` as `Process` calls with proper error handling (ADB not found, device unauthorized, multiple devices).

### 7.5 Input Injector
- Receives `touch` messages, maps normalized coordinates to the virtual display's pixel rectangle, converts to *absolute* coordinates in the virtual desktop's coordinate space, and injects via `SendInput` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK` (or `SetCursorPos` + `mouse_event` for simple click emulation in v1). Multi-touch/gesture passthrough is a v2 stretch goal (Windows pointer injection APIs, `InjectTouchInput`, support multiple contacts if needed later).

### 7.6 UI/Tray app
- Pairing screen (QR code or numeric code) for Wi‑Fi pairing.
- Device list, connection status per transport, manual "force Wi‑Fi"/"force USB" override for debugging.
- Resolution/bitrate/codec settings.
- Start-on-boot, minimize-to-tray, run-as-background-service considerations.

---

## 8. Android app components (detailed)

### 8.1 Transport Layer
- `TransportManager` mirrors the Windows side conceptually: tries `127.0.0.1:PORT` first (works automatically once Windows has set up `adb reverse` — the phone doesn't need to know or care that it's USB), then falls back to NSD-discovered Wi‑Fi hosts, then manual IP entry.
- Use Kotlin coroutines + `Socket`/`SSLSocket` for I/O; a dedicated thread/coroutine for the media channel to avoid blocking control messages.

### 8.2 Decoder & Renderer
- `MediaCodec` in "surface mode" (`configure()` with a `Surface` from a `SurfaceView`/`TextureView`) for zero-copy hardware decode straight to the screen — this is the standard low-latency Android video pipeline (same approach used by remote-desktop/game-streaming apps).
- Handle codec reconfiguration when the host changes resolution/orientation mid-session (tear down and recreate the codec cleanly).
- Full-screen immersive mode (hide system bars), keep-screen-on while streaming, handle orientation lock (a "second monitor" typically shouldn't rotate with the phone unless the user wants that — make it a toggle).

### 8.3 Input Capture
- Capture raw touch events (`onTouchEvent`) on the rendering surface, normalize to 0–1 coordinates relative to the surface size, send as `touch` control messages with minimal latency (don't batch more than a frame's worth).

### 8.4 Pairing/Discovery UI
- NSD browse results list ("Wi‑Fi" tab) + a persistent "connected via USB" indicator that appears automatically when the ADB tunnel is live (no user action needed beyond the one-time USB-debugging authorization).
- QR-scan or numeric-code entry for first-time Wi‑Fi pairing; store trust for future auto-reconnect.

### 8.5 Background behavior
- Foreground service while streaming (required for sustained decode/network work on modern Android), with a persistent notification ("Second Screen active") and a clean stop action.

---

## 9. Build phases & tasks (with acceptance criteria)

> Each phase should end in something demonstrable. Agents should not start a phase whose prerequisites aren't demonstrably working.

**Phase 0 — De-risk the unknowns (1–3 days)**
- Install VDD + VDC manually; confirm a virtual monitor appears in Display Settings and can be extended-desktop'd.
- Manually edit `vdd_settings.xml`, confirm a resolution/refresh-rate change takes effect (and find the exact reload mechanism — restart driver via Device Manager disable/enable, `pnputil`, or a VDC-exposed method).
- Confirm Desktop Duplication API can enumerate and capture specifically the virtual output (write a 20-line console app that dumps N frames to PNG).
- Confirm `adb reverse` tunnel works end-to-end with a trivial echo server/client (Windows `TcpListener` ↔ Android `Socket`) over a real USB cable.
- **Acceptance:** a short write-up in `/docs/phase0-findings.md` confirming/adjusting every assumption in section 3 and 6.1, before writing any pipeline code.

**Phase 1 — Windows: virtual display lifecycle**
- `IVirtualDisplayService` + `VddXmlControlService` implementation: create/update/remove a monitor entry programmatically; verify against Phase 0 findings.
- **Acceptance:** running a CLI command in the dev build adds a 1280×720@60 virtual monitor that shows up in Windows Display Settings, and a remove command cleans it up.

**Phase 2 — Windows: capture pipeline**
- Capture engine grabs frames from the virtual monitor continuously; render them into a debug `WPF Image`/preview window (no network yet).
- **Acceptance:** live preview of the virtual monitor's contents inside the debug app, ≥30fps, dragging a window onto the virtual monitor is visible in the preview.

**Phase 3 — Windows: encode + Android: decode (loopback test first)**
- Encoder wraps captured frames as H.264; write raw stream to a file; confirm Android `MediaCodec` can decode that file to a `SurfaceView` in isolation (no networking yet — sneakernet the file via adb push).
- **Acceptance:** a recorded clip of the virtual monitor plays back correctly on the Android device.

**Phase 4 — Wi‑Fi transport MVP (build this before USB — it's the simpler transport to get right first)**
- TCP server (Windows) / client (Android) with the v1 protocol (hello/hello-ack + raw media frames), manual IP entry (skip mDNS for this milestone).
- **Acceptance:** live end-to-end streaming from Windows virtual monitor to Android screen over Wi‑Fi, with visible input lag measured (<200ms round-trip target for v1).

**Phase 5 — USB transport (ADB reverse)**
- `AdbUsbTransport` wraps adb process calls; same protocol as Phase 4 but tunneled.
- **Acceptance:** same live streaming demo, now over a USB cable with Wi‑Fi off on the phone, and confirmed lower latency than Phase 4.

**Phase 6 — USB tethering transport**
- Detect RNDIS adapter; reuse `TcpLanTransport`.
- **Acceptance:** streaming works with USB tethering enabled and ADB debugging disabled.

**Phase 7 — Input back-channel**
- Touch events flow Android → Windows → `SendInput`, mapped correctly to virtual-display coordinates.
- **Acceptance:** tapping/dragging on the Android screen moves the Windows cursor and can click/drag windows on the virtual monitor.

**Phase 8 — Discovery, pairing, auto-selection**
- mDNS discovery for Wi‑Fi, pairing-code flow, `TransportManager` priority + handover logic across all three transports.
- **Acceptance:** unplug the cable mid-session and the app either seamlessly switches to Wi‑Fi (if available) or gracefully pauses with a clear reconnect prompt.

**Phase 9 — Performance & UX polish**
- Adaptive bitrate based on measured throughput/latency, configurable resolution/refresh presets, battery/thermal considerations on Android, reconnect-on-sleep/wake on Windows.
- **Acceptance:** sustained 1080p60 session for 30+ minutes without crashes, memory leaks, or runaway CPU/GPU usage on either side.

**Phase 10 — Packaging**
- Windows: installer (MSIX or Inno Setup) that also bundles/triggers the VDD/VDC installer and ADB platform-tools; code-signing considerations.
- Android: signed APK (or Play Store internal testing track); permissions review (network state, USB, notification for foreground service).
- **Acceptance:** clean-machine install-to-first-stream in under 5 minutes for a non-technical tester, for both the USB and Wi‑Fi paths.

---

## 10. Risk register

| Risk | Impact | Mitigation |
|---|---|---|
| VDD's runtime reconfiguration mechanism is undocumented/unstable | Blocks core feature | Phase 0 spike; fall back to "fixed resolution list, restart driver on change" if live reconfig proves unreliable |
| ADB not installed / conflicts with other tools (Android Studio's own adb server) | USB transport fails | Bundle a pinned `platform-tools` version; use `adb start-server` defensively; detect and reuse an already-running adb server instead of spawning a second one |
| Desktop Duplication can't target a headless/no-physical-output adapter reliably | Capture fails | Validate in Phase 0; fallback to Windows.Graphics.Capture or GDI BitBlt of that display's rectangle as a slower fallback |
| Latency over Wi‑Fi/TCP too high for "second monitor" feel | Poor UX | Move media channel to UDP with a simple ARQ/FEC-lite scheme in a later version; keep TCP for v1 correctness first |
| Driver signing / test-mode requirements on some Windows configs (esp. ARM64) | Install friction | Rely on the project's already-signed releases (SignPath-signed per the repo); document ARM64 test-signing requirement clearly in the installer |
| Android background/foreground-service restrictions kill the stream | Dropped connection | Foreground service with correct type declared, persistent notification, handle Doze/App-Standby exemptions where appropriate |
| Security: unauthenticated Wi‑Fi connections | Unauthorized screen access | Mandatory pairing code + TLS + TOFU cert pinning before Phase 8 is considered done |

---

## 11. Suggested task breakdown for parallel agents

- **Agent A (Windows/driver):** Phase 0 (driver half) → Phase 1 → Phase 2.
- **Agent B (Windows/media):** Phase 3 (encoder half) → contributes to Phase 4/5/6 transport plumbing on the Windows side.
- **Agent C (Android):** Phase 3 (decoder half) → Phase 4/5/6 transport plumbing on the Android side → Phase 8 (Android discovery/pairing UI).
- **Agent D (protocol/infra):** owns `protocol/SPEC.md`, the `ITransport` interface contract, and Phase 9 adaptive-bitrate logic once A–C converge.

Sync points: end of Phase 0 (unblocks everyone), end of Phase 3 (first real audio-free "picture on the phone" milestone), end of Phase 7 (feature-complete for a v1 demo).