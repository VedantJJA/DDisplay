package com.ddisplay.app.decode

import android.media.MediaCodec
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

private const val TAG = "MediaCodecDecoder"

/**
 * Hardware H.264/H.265 video decoder using MediaCodec in Surface mode.
 * Decoded frames are rendered directly to the provided Surface with zero CPU copy.
 */
class MediaCodecDecoder(
    private val codecMime: String,
    private val surface: Surface,
) {
    private var codec: MediaCodec? = null
    private var isRunning = false
    private var widthPx = 0
    private var heightPx = 0
    private var drainThread: Thread? = null

    fun configure(widthPx: Int, heightPx: Int) {
        this.widthPx = widthPx
        this.heightPx = heightPx

        val format = MediaFormat.createVideoFormat(codecMime, widthPx, heightPx).apply {
            setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, widthPx * heightPx)
            setInteger(MediaFormat.KEY_PUSH_BLANK_BUFFERS_ON_STOP, 0)
        }

        codec = MediaCodec.createDecoderByType(codecMime).apply {
            configure(format, surface, null, 0)
        }
    }

    fun start() {
        val dec = codec ?: return
        dec.start()
        isRunning = true

        drainThread = Thread {
            val info = MediaCodec.BufferInfo()
            while (isRunning) {
                try {
                    val currentCodec = codec ?: break
                    val outputIndex = currentCodec.dequeueOutputBuffer(info, 10_000L) // 10ms
                    if (outputIndex >= 0) {
                        currentCodec.releaseOutputBuffer(outputIndex, true)
                    } else if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                        Log.d(TAG, "Decoder output format changed: ${currentCodec.outputFormat}")
                    }
                } catch (e: Exception) {
                    if (!isRunning) break
                }
            }
        }.apply {
            name = "DDisplay-Decoder-Drain"
            isDaemon = true
            start()
        }
    }

    /**
     * Submits an Annex-B NAL unit buffer to the decoder.
     */
    fun submitFrame(nalData: ByteArray, isKeyframe: Boolean, presentationTimeMs: Long) {
        val decoder = codec ?: return
        if (!isRunning || nalData.isEmpty()) return

        try {
            val inputIndex = decoder.dequeueInputBuffer(10_000L)
            if (inputIndex < 0) {
                Log.w(TAG, "No input buffer available -- dropping frame")
                return
            }

            val inputBuffer: ByteBuffer = decoder.getInputBuffer(inputIndex) ?: return
            inputBuffer.clear()
            inputBuffer.put(nalData)

            val flags = if (isKeyframe) MediaCodec.BUFFER_FLAG_KEY_FRAME else 0
            decoder.queueInputBuffer(inputIndex, 0, nalData.size, presentationTimeMs * 1000L, flags)
        } catch (e: Exception) {
            Log.w(TAG, "Error queueing input buffer: ${e.message}")
        }
    }

    fun stop() {
        isRunning = false
        drainThread?.interrupt()
        drainThread = null
        try {
            codec?.stop()
        } catch (_: Exception) {}
    }

    fun release() {
        stop()
        try {
            codec?.release()
        } catch (_: Exception) {}
        codec = null
    }

    fun reconfigure(widthPx: Int, heightPx: Int) {
        release()
        configure(widthPx, heightPx)
        start()
    }
}
