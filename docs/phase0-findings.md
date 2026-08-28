# Phase 0 Findings

**Status:** NOT STARTED  
**Goal:** Empirically confirm or adjust every assumption in plan.md sections 3 and 6.1
before writing any pipeline code.

---

## Investigation checklist

### A. Virtual Display Driver (VDD)

- [ ] Install VDD + VDC manually from the official release.
- [ ] Confirm a virtual monitor appears in Windows Display Settings as an extended display.
- [ ] Manually edit `C:\VirtualDisplayDriver\vdd_settings.xml`:
  - Add a monitor entry with a non-default resolution (e.g., 1280x720).
  - Determine the exact reload mechanism that makes Windows pick up the change.
    Options to test in order:
    1. Disable / re-enable the VDD device in Device Manager.
    2. `pnputil /restart-device` with the device instance ID.
    3. A named pipe / IPC channel that VDC itself uses (inspect VDC binary behavior).
    4. A file-system signal (e.g., a sentinel file VDC watches).
  - Document which method works and what the latency/side-effects are.
- [ ] Determine whether the virtual monitor persists across a driver reload or if all
  windows placed on it get moved back to the primary.
- [ ] Test on ARM64 Windows if applicable -- note whether test-signing is needed.

---

### B. Desktop Duplication API on the virtual output

- [ ] Write a minimal C# console app using `SharpDX` or `Vortice.Windows` that:
  - Enumerates all `IDXGIAdapter` / `IDXGIOutput` instances.
  - Identifies the output corresponding to the VDD virtual monitor by device name.
  - Acquires the Desktop Duplication interface on that output.
  - Captures 60 frames, saves 5 of them as PNG.
- [ ] Confirm the capture works when the virtual monitor has real content (a window
  dragged onto it).
- [ ] Confirm the capture works when the virtual monitor is blank / has no windows.
- [ ] Note any `DXGI_ERROR_*` codes returned and what they mean in the headless/virtual context.
- [ ] If Desktop Duplication fails: test `Windows.Graphics.Capture` as an alternative.

---

### C. ADB reverse tunnel

- [ ] Confirm `adb.exe` is available (system install or bundled platform-tools).
- [ ] Run `adb devices` with a real USB-connected Android device. Note output format.
- [ ] Run `adb -s <serial> reverse tcp:7878 tcp:7878`.
- [ ] On Windows: start a `TcpListener` on `127.0.0.1:7878`.
- [ ] On Android: in a test app, connect a `Socket` to `127.0.0.1:7878` and send/receive bytes.
- [ ] Measure round-trip latency of the echo (ping-pong 1000 times, report min/avg/p99).
- [ ] Test with USB 2.0 and USB 3.x if possible.
- [ ] Test when multiple ADB devices are connected.

---

## Findings

TODO: Fill in after running each investigation above.

### A. VDD findings

```
VDD version tested:
VDC version tested:
vdd_settings.xml format (paste example):

Reload mechanism that works:
  Method: 
  Command/code:
  Latency (time from file write to monitor appearing in Display Settings):
  Side effects (window displacement, flicker, etc.):

Persistence after reload:

ARM64 notes (if tested):
```

### B. Desktop Duplication findings

```
Library used (SharpDX / Vortice):
Virtual monitor device name as reported by DXGI:
Capture success on blank virtual monitor: yes/no
Capture success with content on virtual monitor: yes/no
DXGI errors encountered:
Fallback to Windows.Graphics.Capture needed: yes/no
Notes:
```

### C. ADB findings

```
ADB version:
adb devices output format:
Reverse tunnel setup time (ms):
Echo latency (min / avg / p99 ms):
USB 2.0 vs USB 3.x difference:
Multi-device behavior:
Notes:
```

---

## Adjustments to plan.md based on findings

TODO: List any changes required to the implementation plan after Phase 0.
