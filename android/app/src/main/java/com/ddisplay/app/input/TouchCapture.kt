package com.ddisplay.app.input

import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import com.ddisplay.app.protocol.ControlMessages
import com.ddisplay.app.transport.SocketTransport

/**
 * Captures touch events from the rendering SurfaceView, normalizes them to [0,1] coordinates,
 * and sends them to the Windows host as touch control messages.
 *
 * Attach via setOnTouchListener on the SurfaceView in RenderActivity.
 */
class TouchCapture(private val transport: SocketTransport) : View.OnTouchListener {

    override fun onTouch(view: View, event: MotionEvent): Boolean {
        val eventType = when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> "down"
            MotionEvent.ACTION_MOVE -> "move"
            MotionEvent.ACTION_UP -> "up"
            MotionEvent.ACTION_CANCEL -> "cancel"
            else -> return false
        }

        val viewWidth = view.width.takeIf { it > 0 } ?: return false
        val viewHeight = view.height.takeIf { it > 0 } ?: return false

        // Send the primary pointer for v1. Multi-pointer support is a v2 goal.
        val normalizedX = event.x.toDouble() / viewWidth
        val normalizedY = event.y.toDouble() / viewHeight

        val msg = ControlMessages.touch(
            eventType = eventType,
            pointerId = 0,
            normalizedX = normalizedX.coerceIn(0.0, 1.0),
            normalizedY = normalizedY.coerceIn(0.0, 1.0),
            timestampMs = SystemClock.uptimeMillis(),
        )

        try {
            transport.sendControlMessage(msg)
        } catch (_: Exception) {}

        return true
    }
}
