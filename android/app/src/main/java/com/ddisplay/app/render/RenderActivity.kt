package com.ddisplay.app.render

import android.os.Bundle
import android.view.SurfaceHolder
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityRenderBinding
import com.ddisplay.app.decode.MediaCodecDecoder
import com.ddisplay.app.input.TouchCapture
import com.ddisplay.app.transport.SocketTransport

/**
 * Full-screen activity for rendering the decoded video stream.
 * Renders hardware-decoded frames directly onto SurfaceView.
 */
class RenderActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRenderBinding
    private var decoder: MediaCodecDecoder? = null
    private var touchCapture: TouchCapture? = null

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
                binding.surfaceView.setOnTouchListener(tc)

                binding.surfaceView.setOnClickListener {
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

        binding.btnStopStreaming.setOnClickListener {
            transport.disconnect()
            finish()
        }
    }

    override fun onDestroy() {
        activeTransport?.onMediaFrameReceived = null
        decoder?.release()
        decoder = null
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
