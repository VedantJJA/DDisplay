package com.ddisplay.app.protocol

import org.json.JSONObject

// ---------------------------------------------------------------------------
// Control message type constants.
// ---------------------------------------------------------------------------

object MessageType {
    const val HELLO = "hello"
    const val HELLO_ACK = "hello-ack"
    const val PAIR_REQUEST = "pair-request"
    const val PAIR_CONFIRM = "pair-confirm"
    const val TOUCH = "touch"
    const val HEARTBEAT = "heartbeat"
    const val HEARTBEAT_ACK = "heartbeat-ack"
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
    val keyframeIntervalSec: Int,
) {
    companion object {
        fun fromJson(json: JSONObject) = HelloAck(
            sessionId = json.getString("sessionId"),
            virtualDisplayWidthPx = json.getInt("virtualDisplayWidthPx"),
            virtualDisplayHeightPx = json.getInt("virtualDisplayHeightPx"),
            refreshRateHz = json.getInt("refreshRateHz"),
            codec = json.getString("codec"),
            bitrateKbps = json.getInt("bitrateKbps"),
            keyframeIntervalSec = json.getInt("keyframeIntervalSec"),
        )
    }
}

data class PairRequest(
    val sessionId: String,
    val code: String,
) {
    companion object {
        fun fromJson(json: JSONObject) = PairRequest(
            sessionId = json.getString("sessionId"),
            code = json.getString("code"),
        )
    }
}
