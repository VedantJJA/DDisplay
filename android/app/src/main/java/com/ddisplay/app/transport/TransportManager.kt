package com.ddisplay.app.transport

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private const val TAG = "TransportManager"
private const val NSD_SERVICE_TYPE = "_ddisplay._tcp."
private const val USB_ADB_HOST = "127.0.0.1"

/**
 * Manages transport discovery (USB/ADB, mDNS/NSD, manual IP) with seamless auto-reconnect.
 */
class TransportManager(
    private val context: Context,
    private val port: Int = 7878,
) {
    private val scope = CoroutineScope(Dispatchers.IO)
    private var pollJob: Job? = null
    private var nsdManager: NsdManager? = null
    private var isNsdDiscovering = false
    private var isConnecting = false

    var transport: SocketTransport? = null
        private set

    var onConnected: ((SocketTransport, String) -> Unit)? = null
    var onDisconnected: ((String) -> Unit)? = null
    var onConnectionFailed: ((String) -> Unit)? = null

    var isBackgroundMode: Boolean = false

    private val discoveryListener = object : NsdManager.DiscoveryListener {
        override fun onDiscoveryStarted(regType: String) {
            Log.d(TAG, "NSD service discovery started")
        }

        override fun onServiceFound(service: NsdServiceInfo) {
            if (service.serviceType.contains("ddisplay")) {
                try {
                    nsdManager?.resolveService(service, resolveListener)
                } catch (e: Exception) {
                    Log.w(TAG, "Resolve failed: ${e.message}")
                }
            }
        }

        override fun onServiceLost(service: NsdServiceInfo) {
            Log.d(TAG, "NSD service lost: ${service.serviceName}")
        }

        override fun onDiscoveryStopped(serviceType: String) {
            isNsdDiscovering = false
        }

        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
            isNsdDiscovering = false
        }

        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {
            isNsdDiscovering = false
        }
    }

    private val resolveListener = object : NsdManager.ResolveListener {
        override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
            Log.w(TAG, "NSD resolve failed: error code $errorCode")
        }

        override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
            val host = serviceInfo.host?.hostAddress ?: return
            val resolvedPort = serviceInfo.port
            scope.launch {
                if (transport == null || transport?.isConnected != true) {
                    tryConnect(host, "Wi-Fi (mDNS)")
                }
            }
        }
    }

    fun startPolling() {
        stopPolling()
        pollJob = scope.launch {
            while (isActive) {
                if (transport == null || transport?.isConnected != true) {
                    val connected = tryConnect(USB_ADB_HOST, "USB")
                    if (!connected) {
                        startNsdBrowse()
                    }
                }

                val interval = if (isBackgroundMode) 4000L else 1500L
                delay(interval)
            }
        }
    }

    fun stopPolling() {
        pollJob?.cancel()
        pollJob = null
        stopNsdBrowse()
    }

    fun connectUsb() {
        scope.launch {
            if (isConnecting) return@launch
            isConnecting = true
            try {
                // Try multiple quick attempts for USB port mapping
                var connected = false
                for (attempt in 1..3) {
                    if (tryConnect(USB_ADB_HOST, "USB")) {
                        connected = true
                        break
                    }
                    delay(300L)
                }
                if (!connected && (transport == null || transport?.isConnected != true)) {
                    withContext(Dispatchers.Main) {
                        onConnectionFailed?.invoke("Connecting to PC at 127.0.0.1:$port... Auto-retrying...")
                    }
                }
            } finally {
                isConnecting = false
            }
        }
    }

    fun connectManual(host: String) {
        scope.launch {
            val success = tryConnect(host, "Wi-Fi (manual)")
            if (!success) {
                withContext(Dispatchers.Main) {
                    onConnectionFailed?.invoke("Could not connect to $host:$port")
                }
            }
        }
    }

    fun disconnect() {
        stopPolling()
        transport?.disconnect()
        transport = null
    }

    private suspend fun tryConnect(host: String, label: String): Boolean {
        return try {
            val t = SocketTransport(host, port)
            withContext(Dispatchers.IO) { t.connect() }
            transport = t

            t.onDisconnected = { reason ->
                transport = null
                scope.launch(Dispatchers.Main) {
                    onDisconnected?.invoke(reason)
                }
                startPolling()
            }

            t.startReadLoop(scope)
            withContext(Dispatchers.Main) {
                onConnected?.invoke(t, label)
            }
            true
        } catch (e: Exception) {
            false
        }
    }

    @Synchronized
    private fun startNsdBrowse() {
        if (isNsdDiscovering) return
        try {
            nsdManager = context.getSystemService(Context.NSD_SERVICE) as? NsdManager
            nsdManager?.discoverServices(NSD_SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
            isNsdDiscovering = true
        } catch (e: Exception) {
            Log.w(TAG, "Failed to start NSD discovery: ${e.message}")
        }
    }

    @Synchronized
    private fun stopNsdBrowse() {
        if (!isNsdDiscovering) return
        try {
            nsdManager?.stopServiceDiscovery(discoveryListener)
        } catch (_: Exception) {}
        isNsdDiscovering = false
    }
}
