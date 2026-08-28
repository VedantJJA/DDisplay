package com.ddisplay.app.protocol

import org.json.JSONObject

// ---------------------------------------------------------------------------
// Control message type constants.
// ---------------------------------------------------------------------------

object MessageType {
    const val HELLO = "hello"
    const val HELLO_ACK = "hello-ack"
    const val START_STREAM = "start-stream"
    const val STOP_STREAM = "stop-stream"
    const val SCREENSHOT = "screenshot"
    const val REQUEST_SCREENSHOT = "request-screenshot"
    const val PAIR_REQUEST = "pair-request"
    const val PAIR_CONFIRM = "pair-confirm"
    const val TOUCH = "touch"
    const val HEARTBEAT = "heartbeat"
    const val HEARTBEAT_ACK = "heartbeat-ack"
    const val TEST_DATA = "test-data"
    const val TEST_DATA_ACK = "test-data-ack"
    const val CURSOR = "cursor"
    const val TILE_PATCH = "tile-patch"
    const val BYE = "bye"
    const val ERROR = "error"
}

// ---------------------------------------------------------------------------
// Outgoing (Android -> Windows) messages.
// ---------------------------------------------------------------------------

object ControlMessages {

    fun hello(
        deviceModel: String,
        screenWidthPx: Int,
        screenHeightPx: Int,
        densityDpi: Int,
        supportedCodecs: List<String>,
        maxDecodeWidthPx: Int,
        maxDecodeHeightPx: Int,
    ): JSONObject = JSONObject().apply {
        put("type", MessageType.HELLO)
        put("protocolVersion", 1)
        put("deviceModel", deviceModel)
        put("screenWidthPx", screenWidthPx)
        put("screenHeightPx", screenHeightPx)
        put("densityDpi", densityDpi)
        put("supportedCodecs", org.json.JSONArray(supportedCodecs))
        put("maxDecodeWidthPx", maxDecodeWidthPx)
        put("maxDecodeHeightPx", maxDecodeHeightPx)
    }

    fun startStream(screenWidthPx: Int, screenHeightPx: Int): JSONObject = JSONObject().apply {
        put("type", MessageType.START_STREAM)
        put("protocolVersion", 1)
        put("screenWidthPx", screenWidthPx)
        put("screenHeightPx", screenHeightPx)
    }

    fun stopStream(): JSONObject = JSONObject().apply {
        put("type", MessageType.STOP_STREAM)
        put("protocolVersion", 1)
    }

    fun requestScreenshot(): JSONObject = JSONObject().apply {
        put("type", MessageType.REQUEST_SCREENSHOT)
        put("protocolVersion", 1)
    }

    fun testData(sequence: Long, payload: String, timestampMs: Long): JSONObject = JSONObject().apply {
        put("type", MessageType.TEST_DATA)
        put("protocolVersion", 1)
        put("sequence", sequence)
        put("payload", payload)
        put("timestampMs", timestampMs)
    }

    fun testDataAck(sequence: Long, echoTimestampMs: Long, bytesReceived: Long): JSONObject = JSONObject().apply {
        put("type", MessageType.TEST_DATA_ACK)
        put("protocolVersion", 1)
        put("sequence", sequence)
        put("echoTimestampMs", echoTimestampMs)
        put("bytesReceived", bytesReceived)
    }

    fun pairConfirm(sessionId: String, code: String, deviceFingerprint: String): JSONObject =
        JSONObject().apply {
            put("type", MessageType.PAIR_CONFIRM)
            put("protocolVersion", 1)
            put("sessionId", sessionId)
            put("code", code)
            put("deviceFingerprint", deviceFingerprint)
        }

    fun touch(
        eventType: String,
        pointerId: Int,
        normalizedX: Double,
        normalizedY: Double,
        timestampMs: Long,
    ): JSONObject = JSONObject().apply {
        put("type", MessageType.TOUCH)
        put("protocolVersion", 1)
        put("eventType", eventType)
        put("pointerId", pointerId)
        put("normalizedX", normalizedX)
        put("normalizedY", normalizedY)
        put("timestampMs", timestampMs)
    }

    fun heartbeat(timestampMs: Long): JSONObject = JSONObject().apply {
        put("type", MessageType.HEARTBEAT)
        put("protocolVersion", 1)
        put("timestampMs", timestampMs)
    }

    fun heartbeatAck(echoTimestampMs: Long, timestampMs: Long): JSONObject = JSONObject().apply {
        put("type", MessageType.HEARTBEAT_ACK)
        put("protocolVersion", 1)
        put("echoTimestampMs", echoTimestampMs)
        put("timestampMs", timestampMs)
    }

    fun bye(reason: String = "user-disconnect"): JSONObject = JSONObject().apply {
        put("type", MessageType.BYE)
        put("protocolVersion", 1)
        put("reason", reason)
    }
}

// ---------------------------------------------------------------------------
// Incoming (Windows -> Android) message data classes.
// ---------------------------------------------------------------------------

data class HelloAck(
    val sessionId: String,
    val virtualDisplayWidthPx: Int,
    val virtualDisplayHeightPx: Int,
    val refreshRateHz: Int,
    val codec: String,
    val bitrateKbps: Int,
) {
    companion object {
        fun fromJson(json: JSONObject): HelloAck = HelloAck(
            sessionId = json.optString("sessionId", ""),
            virtualDisplayWidthPx = json.optInt("virtualDisplayWidthPx", 1920),
            virtualDisplayHeightPx = json.optInt("virtualDisplayHeightPx", 1080),
            refreshRateHz = json.optInt("refreshRateHz", 60),
            codec = json.optString("codec", "video/avc"),
            bitrateKbps = json.optInt("bitrateKbps", 8000),
        )
    }
}
