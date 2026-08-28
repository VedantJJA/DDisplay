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
    private var testBytesTransferred = 0L
    private var lastRttMs = 0L

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

        binding.btnConnectUsb.setOnClickListener {
            updateStatus("Connecting via USB (127.0.0.1:7878)...", connected = false)
            transportManager.connectUsb()
        }

        binding.btnConnectWifi.setOnClickListener {
            startActivity(Intent(this, PairingActivity::class.java))
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
        testBytesTransferred = 0
        lastRttMs = 0

        updateStatus("Connected via $label. Exchanging test data...", connected = true)
        binding.tvTransportBadge.text = label
        binding.tvTransportBadge.visibility = View.VISIBLE

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

        // 2. Start Test Data Ping-Pong Loop for debug verification
        startTestDataLoop(transport)
    }

    private fun startTestDataLoop(transport: SocketTransport) {
        testDataJob?.cancel()
        testDataJob = scope.launch(Dispatchers.IO) {
            val testPayload = "DDisplay-Test-Payload-1024-Bytes".repeat(32) // ~1KB
            val payloadBytes = testPayload.toByteArray()

            while (isActive && transport.isConnected) {
                testPacketsSent++
                testBytesTransferred += payloadBytes.size

                val now = System.currentTimeMillis()
                val msg = ControlMessages.testData(testPacketsSent, testPayload, now)
                transport.sendControlMessage(msg)

                scope.launch(Dispatchers.Main) {
                    val kb = testBytesTransferred / 1024.0
                    updateStatus("Connected (Active) - Packets: $testPacketsSent | Data: ${"%.1f".format(kb)} KB | RTT: ${lastRttMs}ms", connected = true)
                }

                delay(1000L)
            }
        }
    }

    private fun handleControlMessage(json: JSONObject, transport: SocketTransport) {
        when (json.optString("type")) {
            MessageType.HELLO_ACK -> {
                val ack = HelloAck.fromJson(json)
                scope.launch {
                    updateStatus("Handshake OK (${ack.virtualDisplayWidthPx}x${ack.virtualDisplayHeightPx}) - Running data test...", connected = true)
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
                val echoTime = json.optLong("echoTimestampMs")
                if (echoTime > 0) {
                    lastRttMs = System.currentTimeMillis() - echoTime
                }
            }
            MessageType.BYE -> {
                scope.launch { onTransportDisconnected("PC disconnected") }
            }
        }
    }

    private fun onTransportDisconnected(reason: String) {
        testDataJob?.cancel()
        testDataJob = null
        activeTransport = null

        scope.launch {
            updateStatus("Disconnected: $reason (Ready to connect)", connected = false)
            binding.tvTransportBadge.visibility = View.GONE
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

    override fun onDestroy() {
        testDataJob?.cancel()
        transportManager.stopPolling()
        super.onDestroy()
    }
}
