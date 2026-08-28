package com.ddisplay.app.ui

import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.media.MediaCodecList
import android.media.MediaFormat
import android.os.Build
import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityMainBinding
import com.ddisplay.app.protocol.ControlMessages
import com.ddisplay.app.protocol.HelloAck
import com.ddisplay.app.protocol.MessageType
import com.ddisplay.app.render.RenderActivity
import com.ddisplay.app.service.StreamingForegroundService
import com.ddisplay.app.transport.SocketTransport
import com.ddisplay.app.transport.TransportManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import org.json.JSONObject

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var transportManager: TransportManager
    private lateinit var prefs: SharedPreferences
    private val scope = CoroutineScope(Dispatchers.Main)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = getSharedPreferences("ddisplay_settings", Context.MODE_PRIVATE)
        transportManager = TransportManager(this)

        transportManager.onConnected = { transport, label ->
            onTransportConnected(transport, label)
        }

        transportManager.onConnectionFailed = { reason ->
            updateStatus("Waiting for PC: $reason", connected = false)
        }

        binding.btnConnectUsb.setOnClickListener {
            updateStatus("Connecting via USB...", connected = false)
            transportManager.connectAuto()
        }

        binding.btnConnectWifi.setOnClickListener {
            startActivity(Intent(this, PairingActivity::class.java))
        }

        binding.btnSettings.setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }

        // Configure background listener switch
        val isBackgroundEnabled = prefs.getBoolean("pref_background_listen", true)
        binding.switchBackgroundMode.isChecked = isBackgroundEnabled
        updateForegroundService(isBackgroundEnabled)

        binding.switchBackgroundMode.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean("pref_background_listen", isChecked).apply()
            updateForegroundService(isChecked)
        }

        updateStatus("Ready. Waiting for connection from PC...", connected = false)
    }

    override fun onResume() {
        super.onResume()
        // Automatically start listening for PC connection
        transportManager.startAutoListen()
    }

    private fun updateForegroundService(enabled: Boolean) {
        val serviceIntent = Intent(this, StreamingForegroundService::class.java)
        if (enabled) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(serviceIntent)
            } else {
                startService(serviceIntent)
            }
        } else {
            stopService(serviceIntent)
        }
    }

    private fun onTransportConnected(transport: SocketTransport, label: String) {
        updateStatus("Connected via $label. Handshaking...", connected = true)
        binding.tvTransportBadge.text = label
        binding.tvTransportBadge.visibility = View.VISIBLE

        transport.onControlMessageReceived = { json ->
            handleControlMessage(json, transport)
        }

        transport.onDisconnected = { reason ->
            scope.launch {
                updateStatus("Disconnected: $reason. Ready for PC...", connected = false)
                binding.tvTransportBadge.visibility = View.GONE
                // Resume listening for next connection
                transportManager.startAutoListen()
            }
        }

        // Send hello
        scope.launch(Dispatchers.IO) {
            val supportedCodecs = getSupportedVideoCodecs()
            val displayMetrics = resources.displayMetrics
            transport.sendControlMessage(
                ControlMessages.hello(
                    deviceModel = Build.MODEL,
                    screenWidthPx = displayMetrics.widthPixels,
                    screenHeightPx = displayMetrics.heightPixels,
                    densityDpi = displayMetrics.densityDpi,
                    supportedCodecs = supportedCodecs,
                    maxDecodeWidthPx = displayMetrics.widthPixels,
                    maxDecodeHeightPx = displayMetrics.heightPixels,
                )
            )
        }
    }

    private fun handleControlMessage(json: JSONObject, transport: SocketTransport) {
        when (json.optString("type")) {
            MessageType.HELLO_ACK -> {
                val ack = HelloAck.fromJson(json)
                scope.launch {
                    updateStatus("Starting stream at ${ack.virtualDisplayWidthPx}x${ack.virtualDisplayHeightPx}", connected = true)
                    launchRenderActivity(transport, ack)
                }
            }
            MessageType.PAIR_REQUEST -> {}
            MessageType.BYE -> {
                scope.launch { 
                    updateStatus("Host disconnected. Waiting for PC...", connected = false)
                    binding.tvTransportBadge.visibility = View.GONE
                    transportManager.startAutoListen()
                }
            }
            MessageType.ERROR -> {
                val msg = json.optString("message", "Unknown error")
                scope.launch { updateStatus("Error: $msg", connected = false) }
            }
        }
    }

    private fun launchRenderActivity(transport: SocketTransport, ack: HelloAck) {
        RenderActivity.activeTransport = transport
        RenderActivity.codecMime = ack.codec
        RenderActivity.displayWidthPx = ack.virtualDisplayWidthPx
        RenderActivity.displayHeightPx = ack.virtualDisplayHeightPx
        val intent = Intent(this, RenderActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK
        }
        startActivity(intent)
    }

    private fun updateStatus(status: String, connected: Boolean) {
        binding.tvConnectionStatus.text = if (connected) getString(com.ddisplay.app.R.string.label_connected) else getString(com.ddisplay.app.R.string.label_not_connected)
        binding.tvStatusDetail.text = status
    }

    private fun getSupportedVideoCodecs(): List<String> {
        val codecList = MediaCodecList(MediaCodecList.REGULAR_CODECS)
        val supported = mutableSetOf<String>()
        for (info in codecList.codecInfos) {
            if (!info.isEncoder) {
                for (type in info.supportedTypes) {
                    if (type == MediaFormat.MIMETYPE_VIDEO_AVC || type == MediaFormat.MIMETYPE_VIDEO_HEVC) {
                        supported.add(type)
                    }
                }
            }
        }
        if (supported.isEmpty()) {
            supported.add(MediaFormat.MIMETYPE_VIDEO_AVC)
        }
        return supported.toList()
    }

    override fun onDestroy() {
        val isBackgroundEnabled = prefs.getBoolean("pref_background_listen", true)
        if (!isBackgroundEnabled) {
            transportManager.disconnect()
        }
        super.onDestroy()
    }
}
