# DDisplay Wire Protocol -- v1

**Status:** Draft  
**Version:** 1  
**Transport:** TCP (length-prefixed framing over a single connection)

---

## Overview

Two logical channels share one TCP connection. Each message is prefixed with a
4-byte big-endian length field followed by a 1-byte channel tag:

```
[4-byte total payload length (big-endian)][1-byte channel tag][payload]
```

Channel tags:
- `0x01` -- Control channel (JSON, UTF-8)
- `0x02` -- Media channel (binary)

---

## 1. Control channel (tag 0x01)

Payload is a UTF-8 JSON object terminated by the length prefix (no delimiter needed).

Every control message MUST include:

```json
{
  "type": "<message-type>",
  "protocolVersion": 1
}
```

### 1.1 hello (Android -> Windows)

Sent immediately after TCP connection is established.

```json
{
  "type": "hello",
  "protocolVersion": 1,
  "deviceModel": "Pixel 7",
  "screenWidthPx": 1080,
  "screenHeightPx": 2400,
  "densityDpi": 420,
  "supportedCodecs": ["video/avc", "video/hevc"],
  "maxDecodeWidthPx": 1920,
  "maxDecodeHeightPx": 1080
}
```

Fields:
- `deviceModel` -- human-readable device model string
- `screenWidthPx` / `screenHeightPx` -- physical screen dimensions
- `densityDpi` -- logical density
- `supportedCodecs` -- MIME types the device can hardware-decode
- `maxDecodeWidthPx` / `maxDecodeHeightPx` -- maximum resolution the decoder supports

### 1.2 hello-ack (Windows -> Android)

```json
{
  "type": "hello-ack",
  "protocolVersion": 1,
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "virtualDisplayWidthPx": 1080,
  "virtualDisplayHeightPx": 1920,
  "refreshRateHz": 60,
  "codec": "video/avc",
  "bitrateKbps": 8000,
  "keyframeIntervalSec": 2
}
```

Fields:
- `sessionId` -- UUID for this session, used in logging and pairing
- `virtualDisplayWidthPx` / `virtualDisplayHeightPx` -- chosen virtual monitor resolution
- `refreshRateHz` -- chosen refresh rate
- `codec` -- chosen codec MIME type (must be in `supportedCodecs` from hello)
- `bitrateKbps` -- initial target bitrate
- `keyframeIntervalSec` -- keyframe interval

### 1.3 pair-request (Windows -> Android, Wi-Fi path only)

```json
{
  "type": "pair-request",
  "protocolVersion": 1,
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "code": "482917"
}
```

The pairing code is shown on the Windows app. The user enters it on Android.

### 1.4 pair-confirm (Android -> Windows)

```json
{
  "type": "pair-confirm",
  "protocolVersion": 1,
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "code": "482917",
  "deviceFingerprint": "<SHA-256 of the Android TLS cert>"
}
```

### 1.5 touch (Android -> Windows)

```json
{
  "type": "touch",
  "protocolVersion": 1,
  "eventType": "down",
  "pointerId": 0,
  "normalizedX": 0.512,
  "normalizedY": 0.334,
  "timestampMs": 1722345678901
}
```

Fields:
- `eventType` -- one of `"down"`, `"move"`, `"up"`, `"cancel"`
- `pointerId` -- finger/pointer index (0-based, for multi-touch v2)
- `normalizedX` / `normalizedY` -- position relative to rendering surface size, range [0.0, 1.0]
- `timestampMs` -- `SystemClock.uptimeMillis()` on Android at the time of the event

### 1.6 heartbeat (either direction)

```json
{
  "type": "heartbeat",
  "protocolVersion": 1,
  "timestampMs": 1722345678901
}
```

### 1.7 heartbeat-ack (opposite direction from heartbeat)

```json
{
  "type": "heartbeat-ack",
  "protocolVersion": 1,
  "echoTimestampMs": 1722345678901,
  "timestampMs": 1722345679001
}
```

### 1.8 bye (either direction)

```json
{
  "type": "bye",
  "protocolVersion": 1,
  "reason": "user-disconnect"
}
```

`reason` values: `"user-disconnect"`, `"error"`, `"codec-mismatch"`, `"version-mismatch"`.

### 1.9 error (either direction)

```json
{
  "type": "error",
  "protocolVersion": 1,
  "code": "CODEC_NOT_SUPPORTED",
  "message": "No common codec found between host and client."
}
```

---

## 2. Media channel (tag 0x02)

Binary payload immediately follows the 5-byte header (4-byte length + 1-byte tag).

```
[1-byte flags][4-byte presentation timestamp, big-endian milliseconds][N bytes: H.264/H.265 Annex B NAL units]
```

Flags byte (bitmask):
- Bit 0 (`0x01`): keyframe / IDR frame
- Bit 1 (`0x02`): end-of-stream
- Bits 2-7: reserved, set to 0

Codec byte-stream format: H.264 Annex B (start codes `00 00 00 01`) or H.265 Annex B.
SPS and PPS are prepended to every keyframe to allow decoder resync.

---

## 3. Connection lifecycle

```
Android                          Windows
   |                                |
   |--- TCP connect --------------->|
   |--- hello ---------------------->|
   |<-- pair-request (Wi-Fi only) --|  (skipped on ADB/USB path)
   |--- pair-confirm -------------->|  (skipped on ADB/USB path)
   |<-- hello-ack ------------------|
   |                                |  (Windows starts virtual monitor, encoder)
   |<== media frames (continuous) ==|
   |--- touch (on user input) ----->|
   |--- heartbeat ----------------->|
   |<-- heartbeat-ack --------------|
   |--- bye ------------------------>|  (or either side closes the TCP connection)
```

---

## 4. Versioning

- `protocolVersion` is an integer, starting at `1`.
- If `hello.protocolVersion` is higher than the host supports, the host sends
  `error { code: "VERSION_TOO_HIGH" }` and closes the connection.
- If `hello.protocolVersion` is lower than the minimum the host accepts, the host sends
  `error { code: "VERSION_TOO_LOW" }` and closes the connection.
- Minor backwards-compatible additions (new optional JSON fields) do NOT increment the version.

---

## 5. Security (Wi-Fi path)

- TLS 1.3 wraps the TCP connection.
- Windows generates a self-signed RSA-2048 certificate per installation.
- On first pairing, the Android client stores the SHA-256 fingerprint of the Windows cert
  and sends it in `pair-confirm`. Subsequent connections verify this fingerprint (TOFU).
- ADB/USB path: TLS is still used but pairing is skipped; ADB authorization is the
  trust boundary.

---

## 6. Port numbers

| Port | Purpose |
|------|---------|
| 7878 | Primary TCP port (control + media multiplexed) |

The port is configurable in the Windows app settings. Both sides must agree.
