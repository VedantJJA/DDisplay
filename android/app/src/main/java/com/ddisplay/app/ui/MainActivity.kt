package com.ddisplay.app.ui

import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
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
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONObject

private const val PREFS_NAME = "ddisplay_prefs"
private const val PREF_BG_MODE = "pref_background_mode"
private const val NOTIFICATION_PERMISSION_CODE = 101

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var transportManager: TransportManager
    private lateinit var prefs: SharedPreferences
    private val scope = CoroutineScope(Dispatchers.Main)

    private var activeTransport: SocketTransport? = null
    private var testDataJob: Job? = null
    private var testPacketsSent = 0L
    private var testPacketsAcked = 0L
    private var testBytesTransferred = 0L
    private var lastRttMs = 0L

    private var serverDisplayWidth = 1920
    private var serverDisplayHeight = 1080
    private var isStreamingActive = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        transportManager = TransportManager(this)

        setupBackgroundModeToggle()

        transportManager.onConnected = { transport, label ->
            onTransportConnected(transport, label)
        }

        transportManager.onDisconnected = { reason ->
            onTransportDisconnected(reason)
        }

        transportManager.onConnectionFailed = { reason ->
            updateStatus(reason, connected = false)
        }

        binding.btnConnect.setOnClickListener {
            updateStatus("Connecting to PC...", connected = false)
            transportManager.connectUsb()
        }

        binding.btnStartStreaming.setOnClickListener {
            startStreamingSession()
        }

        binding.btnSettings.setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }

        // Start background polling
        transportManager.startPolling()
    }

    private fun setupBackgroundModeToggle() {
        val isBgEnabled = prefs.getBoolean(PREF_BG_MODE, false)
        binding.switchBackgroundMode.isChecked = isBgEnabled
        transportManager.isBackgroundMode = isBgEnabled

        binding.switchBackgroundMode.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean(PREF_BG_MODE, isChecked).apply()
            transportManager.isBackgroundMode = isChecked

            if (isChecked) {
                requestNotificationPermissionIfNeeded()
                StreamingForegroundService.startService(this)
            } else {
                StreamingForegroundService.stopService(this)
            }
        }

        if (isBgEnabled) {
            StreamingForegroundService.startService(this)
        }
    }

    private fun onTransportConnected(transport: SocketTransport, label: String) {
        activeTransport = transport
        testPacketsSent = 0
        testPacketsAcked = 0
        testBytesTransferred = 0
        lastRttMs = 0
        isStreamingActive = false

        updateStatus("Connected via $label (Ready to stream)", connected = true)
        binding.tvTransportBadge.text = label
        binding.tvTransportBadge.visibility = View.VISIBLE
        binding.tvPacketLoss.visibility = View.VISIBLE
        binding.btnConnect.visibility = View.GONE
        binding.btnStartStreaming.visibility = View.VISIBLE

        transport.onControlMessageReceived = { json ->
            handleControlMessage(json, transport)
        }

        // 1. Send Hello handshake
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

        // 2. Start Test Data Ping-Pong Loop
        startTestDataLoop(transport)
    }

    private fun startTestDataLoop(transport: SocketTransport) {
        testDataJob?.cancel()
        testDataJob = scope.launch(Dispatchers.IO) {
            val testPayload = "DDisplay-Test-Payload-1024-Bytes".repeat(32)
            val payloadBytes = testPayload.toByteArray()

            while (isActive && transport.isConnected && !isStreamingActive) {
                testPacketsSent++
                testBytesTransferred += payloadBytes.size

                val now = System.currentTimeMillis()
                val msg = ControlMessages.testData(testPacketsSent, testPayload, now)
                transport.sendControlMessage(msg)

                scope.launch(Dispatchers.Main) {
                    val kb = testBytesTransferred / 1024.0
                    val lostCount = maxOf(0L, testPacketsSent - testPacketsAcked - 1)
                    val lossPct = if (testPacketsSent > 0) (lostCount * 100.0) / testPacketsSent else 0.0

                    if (!isStreamingActive) {
                        updateStatus("Connected - Packets: $testPacketsSent | Data: ${"%.1f".format(kb)} KB | RTT: ${lastRttMs}ms", connected = true)
                        binding.tvPacketLoss.text = "Packet Loss: $lostCount (${"%.1f".format(lossPct)}%)"
                    }
                }

                delay(1000L)
            }
        }
    }

    private fun handleControlMessage(json: JSONObject, transport: SocketTransport) {
        when (json.optString("type")) {
            MessageType.HELLO_ACK -> {
                val ack = HelloAck.fromJson(json)
                serverDisplayWidth = ack.virtualDisplayWidthPx
                serverDisplayHeight = ack.virtualDisplayHeightPx

                scope.launch {
                    updateStatus("Handshake OK (${ack.virtualDisplayWidthPx}x${ack.virtualDisplayHeightPx}) - Ready to stream", connected = true)
                }
            }
            MessageType.START_STREAM -> {
                scope.launch {
                    startStreamingSession()
                }
            }
            MessageType.STOP_STREAM -> {
                scope.launch {
                    isStreamingActive = false
                    updateStatus("Streaming stopped (Connected)", connected = true)
                    startTestDataLoop(transport)
                }
            }
            MessageType.TEST_DATA -> {
                val seq = json.optLong("sequence")
                val sendTime = json.optLong("timestampMs")
                val payload = json.optString("payload")
                testBytesTransferred += payload.length

                scope.launch(Dispatchers.IO) {
                    val ack = ControlMessages.testDataAck(seq, sendTime, testBytesTransferred)
                    transport.sendControlMessage(ack)
                }
            }
            MessageType.TEST_DATA_ACK -> {
                testPacketsAcked++
                val echoTime = json.optLong("echoTimestampMs")
                if (echoTime > 0) {
                    lastRttMs = System.currentTimeMillis() - echoTime
                }
                scope.launch {
                    val lostCount = maxOf(0L, testPacketsSent - testPacketsAcked)
                    val lossPct = if (testPacketsSent > 0) (lostCount * 100.0) / testPacketsSent else 0.0
                    binding.tvPacketLoss.text = "Packet Loss: $lostCount (${"%.1f".format(lossPct)}%)"
                }
            }
            MessageType.BYE -> {
                scope.launch { onTransportDisconnected("PC disconnected") }
            }
        }
    }

    private fun startStreamingSession() {
        val transport = activeTransport ?: return
        isStreamingActive = true
        testDataJob?.cancel()

        val displayMetrics = resources.displayMetrics
        scope.launch(Dispatchers.IO) {
            transport.sendControlMessage(
                ControlMessages.startStream(displayMetrics.widthPixels, displayMetrics.heightPixels)
            )
        }

        RenderActivity.activeTransport = transport
        RenderActivity.displayWidthPx = if (serverDisplayWidth > 0) serverDisplayWidth else displayMetrics.widthPixels
        RenderActivity.displayHeightPx = if (serverDisplayHeight > 0) serverDisplayHeight else displayMetrics.heightPixels
        RenderActivity.codecMime = "video/avc"

        startActivity(Intent(this, RenderActivity::class.java))
    }

    private fun onTransportDisconnected(reason: String) {
        testDataJob?.cancel()
        testDataJob = null
        activeTransport = null
        isStreamingActive = false

        scope.launch {
            updateStatus("Disconnected: $reason (Ready to connect)", connected = false)
            binding.tvTransportBadge.visibility = View.GONE
            binding.tvPacketLoss.visibility = View.GONE
            binding.btnStartStreaming.visibility = View.GONE
            binding.btnConnect.visibility = View.VISIBLE
        }
    }

    private fun updateStatus(status: String, connected: Boolean) {
        binding.tvConnectionStatus.text = if (connected) getString(com.ddisplay.app.R.string.label_connected) else getString(com.ddisplay.app.R.string.label_not_connected)
        binding.tvStatusDetail.text = status
    }

    private fun getSupportedVideoCodecs(): List<String> = listOf("video/avc", "video/hevc")

    private fun requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, android.Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                ActivityCompat.requestPermissions(this, arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), NOTIFICATION_PERMISSION_CODE)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        isStreamingActive = false
        val transport = activeTransport
        if (transport != null && transport.isConnected) {
            transport.onControlMessageReceived = { json ->
                handleControlMessage(json, transport)
            }
            startTestDataLoop(transport)
        }
    }

    override fun onDestroy() {
        testDataJob?.cancel()
        transportManager.stopPolling()
        super.onDestroy()
    }
}
