package com.ddisplay.app.render

import android.os.Bundle
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityRenderBinding
import com.ddisplay.app.decode.MediaCodecDecoder
import com.ddisplay.app.input.TouchCapture
import com.ddisplay.app.transport.SocketTransport

/**
 * Full-screen activity for rendering the decoded video stream.
 * Uses ActivityRenderBinding (ViewBinding) for the SurfaceView reference.
 *
 * This activity receives an already-connected SocketTransport and a configured decoder
 * via a companion object singleton (simple approach for v1 -- replace with a
 * bound service or ViewModel in Phase 9 polish).
 */
class RenderActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRenderBinding
    private var decoder: MediaCodecDecoder? = null
    private var touchCapture: TouchCapture? = null

    companion object {
        // Set before starting this activity.
        var activeTransport: SocketTransport? = null
        var codecMime: String = "video/avc"
        var displayWidthPx: Int = 1080
        var displayHeightPx: Int = 1920
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityRenderBinding.inflate(layoutInflater)
        setContentView(binding.root)

        enterImmersiveMode()

        val transport = activeTransport ?: run { finish(); return }

        // Set up decoder with the surface from the SurfaceView.
        binding.surfaceView.holder.addCallback(object : android.view.SurfaceHolder.Callback {
            override fun surfaceCreated(holder: android.view.SurfaceHolder) {
                val dec = MediaCodecDecoder(codecMime, holder.surface)
                dec.configure(displayWidthPx, displayHeightPx)
                dec.start()
                decoder = dec

                // Wire media frames from the transport directly to the decoder.
                transport.onMediaFrameReceived = { nalData, isKeyframe, pts ->
                    dec.submitFrame(nalData, isKeyframe, pts)
                }

                // Wire touch events.
                val tc = TouchCapture(transport)
                touchCapture = tc
                binding.surfaceView.setOnTouchListener(tc)

                // HUD toggle on single tap via GestureDetector.
                binding.surfaceView.setOnClickListener {
                    val hud = binding.hudOverlay
                    hud.visibility = if (hud.visibility == View.VISIBLE) View.GONE else View.VISIBLE
                }
            }

            override fun surfaceChanged(holder: android.view.SurfaceHolder, format: Int, width: Int, height: Int) {}
            override fun surfaceDestroyed(holder: android.view.SurfaceHolder) {
                decoder?.stop()
            }
        })

        binding.hudOverlay.visibility = View.GONE

        binding.btnStopStreaming.setOnClickListener {
            transport.disconnect()
            finish()
        }
    }

    override fun onDestroy() {
        decoder?.release()
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
        window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }
}
