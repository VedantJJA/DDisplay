package com.ddisplay.app.render

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Bundle
import android.util.Base64
import android.view.SurfaceHolder
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityRenderBinding
import com.ddisplay.app.decode.MediaCodecDecoder
import com.ddisplay.app.input.TouchCapture
import com.ddisplay.app.protocol.ControlMessages
import com.ddisplay.app.protocol.MessageType
import com.ddisplay.app.transport.SocketTransport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import org.json.JSONObject

/**
 * Full-screen activity for rendering the Windows desktop display.
 * Supports both high-fidelity JPEG screenshot frames and MediaCodec H.264 stream.
 */
class RenderActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRenderBinding
    private var decoder: MediaCodecDecoder? = null
    private var touchCapture: TouchCapture? = null
    private val scope = CoroutineScope(Dispatchers.Main)
    private var currentBitmap: Bitmap? = null

    companion object {
        var activeTransport: SocketTransport? = null
        var codecMime: String = "video/avc"
        var displayWidthPx: Int = 1920
        var displayHeightPx: Int = 1080
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityRenderBinding.inflate(layoutInflater)
        setContentView(binding.root)

        enterImmersiveMode()

        val transport = activeTransport ?: run {
            finish()
            return
        }

        transport.onDisconnected = {
            runOnUiThread {
                finish()
            }
        }

        // Handle incoming screenshot and control messages
        transport.onControlMessageReceived = { json ->
            handleControlMessage(json)
        }

        // Request initial screenshot immediately
        scope.launch(Dispatchers.IO) {
            transport.sendControlMessage(ControlMessages.requestScreenshot())
        }

        binding.surfaceView.holder.addCallback(object : SurfaceHolder.Callback {
            override fun surfaceCreated(holder: SurfaceHolder) {
                val dec = MediaCodecDecoder(codecMime, holder.surface)
                dec.configure(displayWidthPx, displayHeightPx)
                dec.start()
                decoder = dec

                transport.onMediaFrameReceived = { nalData, isKeyframe, pts ->
                    dec.submitFrame(nalData, isKeyframe, pts)
                }

                val tc = TouchCapture(transport)
                touchCapture = tc
                binding.ivScreenshot.setOnTouchListener(tc)

                binding.ivScreenshot.setOnClickListener {
                    val hud = binding.hudOverlay
                    hud.visibility = if (hud.visibility == View.VISIBLE) View.GONE else View.VISIBLE
                }
            }

            override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}

            override fun surfaceDestroyed(holder: SurfaceHolder) {
                transport.onMediaFrameReceived = null
                decoder?.stop()
                decoder?.release()
                decoder = null
            }
        })

        binding.hudOverlay.visibility = View.GONE
        binding.tvHudResolution.text = "${displayWidthPx}x${displayHeightPx}"

        binding.btnRefreshScreenshot.setOnClickListener {
            scope.launch(Dispatchers.IO) {
                transport.sendControlMessage(ControlMessages.requestScreenshot())
            }
        }

        binding.btnStopStreaming.setOnClickListener {
            scope.launch(Dispatchers.IO) {
                transport.sendControlMessage(ControlMessages.stopStream())
            }
            finish()
        }
    }

    private fun handleControlMessage(json: JSONObject) {
        val type = json.optString("type")
        if (type == MessageType.SCREENSHOT) {
            val base64 = json.optString("imageBase64")
            if (base64.isNotEmpty()) {
                try {
                    val imageBytes = Base64.decode(base64, Base64.DEFAULT)
                    val bmp = BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size)
                    if (bmp != null) {
                        runOnUiThread {
                            binding.ivScreenshot.setImageBitmap(bmp)
                            currentBitmap?.recycle()
                            currentBitmap = bmp
                        }
                    }
                } catch (_: Exception) {}
            }
        } else if (type == MessageType.STOP_STREAM || type == MessageType.BYE) {
            runOnUiThread {
                finish()
            }
        }
    }

    override fun onDestroy() {
        activeTransport?.onMediaFrameReceived = null
        decoder?.release()
        decoder = null
        currentBitmap?.recycle()
        currentBitmap = null
        super.onDestroy()
    }

    private fun enterImmersiveMode() {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.R) {
            window.insetsController?.apply {
                hide(WindowInsets.Type.statusBars() or WindowInsets.Type.navigationBars())
                systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    or View.SYSTEM_UI_FLAG_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
            )
        }
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }
}
