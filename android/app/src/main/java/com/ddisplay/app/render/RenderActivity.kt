package com.ddisplay.app.render

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
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
 * Full-screen activity for rendering the Windows desktop display.
 * Supports unified canvas compositing: full frames, dirty tile patches, and client-side cursor overlays.
 */
class RenderActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRenderBinding
    private var touchCapture: TouchCapture? = null
    private val scope = CoroutineScope(Dispatchers.Main)

    private var screenCanvasBitmap: Bitmap? = null
    private var screenCanvas: Canvas? = null
    private val canvasPaint = Paint(Paint.FILTER_BITMAP_FLAG)

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
        initCanvas(displayWidthPx, displayHeightPx)

        val transport = activeTransport ?: run {
            finish()
            return
        }

        transport.onDisconnected = {
            runOnUiThread {
                finish()
            }
        }

        // Handle incoming screenshot, patch, cursor, and control messages
        transport.onControlMessageReceived = { json ->
            handleControlMessage(json)
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

    @Synchronized
    private fun initCanvas(w: Int, h: Int) {
        val width = if (w > 0) w else 1920
        val height = if (h > 0) h else 1080

        if (screenCanvasBitmap == null || screenCanvasBitmap?.width != width || screenCanvasBitmap?.height != height) {
            screenCanvasBitmap?.recycle()
            val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            screenCanvasBitmap = bmp
            screenCanvas = Canvas(bmp)
            binding.ivScreenshot.setImageBitmap(bmp)
        }
    }

    private fun handleControlMessage(json: JSONObject) {
        when (json.optString("type")) {
            MessageType.CURSOR -> {
                val x = json.optInt("x", 0)
                val y = json.optInt("y", 0)
                val visible = json.optBoolean("visible", true)

                runOnUiThread {
                    if (!visible) {
                        binding.ivCursor.visibility = View.GONE
                    } else {
                        binding.ivCursor.visibility = View.VISIBLE

                        // Map desktop coordinates to Android screen coordinates
                        val ivWidth = binding.ivScreenshot.width
                        val ivHeight = binding.ivScreenshot.height
                        val bmpW = screenCanvasBitmap?.width ?: displayWidthPx
                        val bmpH = screenCanvasBitmap?.height ?: displayHeightPx

                        if (bmpW > 0 && bmpH > 0 && ivWidth > 0 && ivHeight > 0) {
                            val scale = minOf(ivWidth.toFloat() / bmpW, ivHeight.toFloat() / bmpH)
                            val leftOffset = (ivWidth - bmpW * scale) / 2f
                            val topOffset = (ivHeight - bmpH * scale) / 2f

                            binding.ivCursor.x = leftOffset + (x * scale)
                            binding.ivCursor.y = topOffset + (y * scale)
                        }
                    }
                }
            }
            MessageType.TILE_PATCH -> {
                val tileX = json.optInt("tileX", 0)
                val tileY = json.optInt("tileY", 0)
                val base64 = json.optString("imageBase64")

                if (base64.isNotEmpty()) {
                    try {
                        val imageBytes = Base64.decode(base64, Base64.DEFAULT)
                        val tileBmp = BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size)

                        if (tileBmp != null) {
                            synchronized(this) {
                                screenCanvas?.drawBitmap(tileBmp, tileX.toFloat(), tileY.toFloat(), canvasPaint)
                            }
                            tileBmp.recycle()

                            runOnUiThread {
                                binding.ivScreenshot.invalidate()
                            }
                        }
                    } catch (_: Exception) {}
                }
            }
            MessageType.SCREENSHOT -> {
                val base64 = json.optString("imageBase64")
                val frameW = json.optInt("width", displayWidthPx)
                val frameH = json.optInt("height", displayHeightPx)

                if (base64.isNotEmpty()) {
                    try {
                        val imageBytes = Base64.decode(base64, Base64.DEFAULT)
                        val fullBmp = BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size)

                        if (fullBmp != null) {
                            synchronized(this) {
                                initCanvas(fullBmp.width, fullBmp.height)
                                screenCanvas?.drawBitmap(fullBmp, 0f, 0f, canvasPaint)
                            }
                            fullBmp.recycle()

                            runOnUiThread {
                                binding.tvHudResolution.text = "${frameW}x${frameH}"
                                binding.ivScreenshot.invalidate()
                            }
                        }
                    } catch (_: Exception) {}
                }
            }
            MessageType.STOP_STREAM, MessageType.BYE -> {
                runOnUiThread {
                    finish()
                }
            }
        }
    }

    override fun onDestroy() {
        activeTransport?.onMediaFrameReceived = null
        screenCanvasBitmap?.recycle()
        screenCanvasBitmap = null
        screenCanvas = null
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
