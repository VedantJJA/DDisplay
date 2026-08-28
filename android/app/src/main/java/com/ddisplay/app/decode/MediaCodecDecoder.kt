package com.ddisplay.app.decode

import android.media.MediaCodec
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

private const val TAG = "MediaCodecDecoder"

/**
 * H.264 / H.265 hardware decoder using MediaCodec in surface mode.
 * Decoded frames are rendered directly to the provided Surface -- zero CPU copy.
 *
 * Usage:
 *   1. Create with the codec MIME type and target surface.
 *   2. Call configure() with width, height from hello-ack.
 *   3. Call start().
 *   4. For each received NAL unit buffer, call submitFrame().
 *   5. Call stop() / release() when streaming ends.
 */
class MediaCodecDecoder(
    private val codecMime: String,
    private val surface: Surface,
) {
    private var codec: MediaCodec? = null
    private var isRunning = false
    private var widthPx = 0
    private var heightPx = 0

    fun configure(widthPx: Int, heightPx: Int) {
        this.widthPx = widthPx
        this.heightPx = heightPx

        val format = MediaFormat.createVideoFormat(codecMime, widthPx, heightPx)
        // Prefer low-latency decode -- important for the second-monitor feel.
        format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, widthPx * heightPx / 2)

        codec = MediaCodec.createDecoderByType(codecMime).apply {
            configure(format, surface, null, 0)
        }
    }

    fun start() {
        codec?.start()
        isRunning = true
    }

    /**
     * Submits one Annex-B NAL unit buffer to the decoder.
     * This is called from the network receive coroutine.
     */
    fun submitFrame(nalData: ByteArray, isKeyframe: Boolean, presentationTimeMs: Long) {
        val decoder = codec ?: return
        if (!isRunning) return

        val inputIndex = decoder.dequeueInputBuffer(10_000L) // 10ms timeout
        if (inputIndex < 0) {
            Log.w(TAG, "No input buffer available -- dropping frame")
            return
        }

        val inputBuffer: ByteBuffer = decoder.getInputBuffer(inputIndex) ?: return
        inputBuffer.clear()
        inputBuffer.put(nalData)

        val flags = if (isKeyframe) MediaCodec.BUFFER_FLAG_KEY_FRAME else 0
        decoder.queueInputBuffer(inputIndex, 0, nalData.size, presentationTimeMs * 1000L, flags)

        // Release any available output buffers back to the surface (renders the frame).
        drainOutput()
    }

    private fun drainOutput() {
        val decoder = codec ?: return
        val info = MediaCodec.BufferInfo()
        var outputIndex: Int
        do {
            outputIndex = decoder.dequeueOutputBuffer(info, 0L)
            if (outputIndex >= 0) {
                // render = true: decoder outputs directly to the surface.
                decoder.releaseOutputBuffer(outputIndex, true)
            }
        } while (outputIndex >= 0)
    }

    fun stop() {
        isRunning = false
        try {
            codec?.stop()
        } catch (_: Exception) {}
    }

    fun release() {
        stop()
        codec?.release()
        codec = null
    }

    /**
     * Reconfigures the decoder for a new resolution. Called when the host changes resolution
     * mid-session (e.g., device rotation or settings change).
     */
    fun reconfigure(widthPx: Int, heightPx: Int) {
        stop()
        codec?.release()
        codec = null
        configure(widthPx, heightPx)
        start()
    }
}
