package com.ddisplay.app.render

import android.graphics.BitmapFactory
import android.os.Bundle
import android.util.Base64
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityRenderBinding
import com.ddisplay.app.input.TouchCapture
import com.ddisplay.app.protocol.ControlMessages
import com.ddisplay.app.protocol.MessageType
import com.ddisplay.app.transport.SocketTransport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import org.json.JSONObject

/**
 * Full-screen activity for rendering the Windows extended desktop display.
 * Renders live stream frames with embedded hardware cursor and captures touch input.
 */
class RenderActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRenderBinding
    private var touchCapture: TouchCapture? = null
    private val scope = CoroutineScope(Dispatchers.Main)

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
                if (!isFinishing && !isDestroyed) {
                    finish()
                }
            }
        }

        transport.onControlMessageReceived = { json ->
            try {
                handleControlMessage(json)
            } catch (_: Exception) {}
        }

        val tc = TouchCapture(transport)
        touchCapture = tc
        binding.ivScreenshot.setOnTouchListener(tc)

        binding.ivScreenshot.setOnClickListener {
            val hud = binding.hudOverlay
            hud.visibility = if (hud.visibility == View.VISIBLE) View.GONE else View.VISIBLE
        }

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

        // Request initial full screen frame
        scope.launch(Dispatchers.IO) {
            transport.sendControlMessage(ControlMessages.requestScreenshot())
        }
    }

    private fun handleControlMessage(json: JSONObject) {
        val type = json.optString("type")
        when (type) {
            MessageType.SCREENSHOT -> {
                val base64 = json.optString("imageBase64")
                val frameW = json.optInt("width", displayWidthPx)
                val frameH = json.optInt("height", displayHeightPx)

                if (base64.isNotEmpty()) {
                    try {
                        val imageBytes = Base64.decode(base64, Base64.DEFAULT)
                        val bmp = BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size)

                        if (bmp != null) {
                            displayWidthPx = bmp.width
                            displayHeightPx = bmp.height

                            runOnUiThread {
                                if (isFinishing || isDestroyed) return@runOnUiThread
                                binding.ivScreenshot.setImageBitmap(bmp)
                                binding.tvHudResolution.text = "${bmp.width}x${bmp.height}"
                            }
                        }
                    } catch (_: Exception) {}
                }
            }
            MessageType.STOP_STREAM, MessageType.BYE -> {
                runOnUiThread {
                    if (!isFinishing && !isDestroyed) {
                        finish()
                    }
                }
            }
        }
    }

    override fun onDestroy() {
        activeTransport?.onMediaFrameReceived = null
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
