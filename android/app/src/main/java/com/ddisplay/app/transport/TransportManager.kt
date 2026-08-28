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
private const val USB_ADB_HOST = "127.0.0.1"
private const val NSD_SERVICE_TYPE = "_ddisplay._tcp."

/**
 * Transport manager for Android.
 * Supports manual connect via USB or Wi-Fi as well as background polling.
 */
class TransportManager(
    private val context: Context,
    private val port: Int = 7878,
) {
    private val scope = CoroutineScope(Dispatchers.IO)
    private var transport: SocketTransport? = null
    private var nsdManager: NsdManager? = null
    private var isNsdDiscovering = false
    private var pollJob: Job? = null

    var isBackgroundMode: Boolean = false
        set(value) {
            field = value
            Log.d(TAG, "Background mode updated: $value")
        }

    var onConnected: ((SocketTransport, String) -> Unit)? = null
    var onDisconnected: ((String) -> Unit)? = null
    var onConnectionFailed: ((String) -> Unit)? = null
    var onStatusChanged: ((String) -> Unit)? = null

    fun connectUsb() {
        scope.launch {
            val success = tryConnect(USB_ADB_HOST, "USB")
            if (!success) {
                withContext(Dispatchers.Main) {
                    onConnectionFailed?.invoke("Could not connect to PC at $USB_ADB_HOST:$port. Ensure PC app is running.")
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

                val interval = if (isBackgroundMode) 5000L else 2000L
                delay(interval)
            }
        }
    }

    fun stopPolling() {
        pollJob?.cancel()
        pollJob = null
        stopNsdBrowse()
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
        } catch (e: Exception) {
            Log.w(TAG, "Failed to stop NSD discovery: ${e.message}")
        } finally {
            isNsdDiscovering = false
        }
    }

    private val discoveryListener = object : NsdManager.DiscoveryListener {
        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
            isNsdDiscovering = false
        }

        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {
            isNsdDiscovering = false
        }

        override fun onDiscoveryStarted(serviceType: String) {
            isNsdDiscovering = true
        }

        override fun onDiscoveryStopped(serviceType: String) {
            isNsdDiscovering = false
        }

        override fun onServiceFound(service: NsdServiceInfo) {
            try {
                nsdManager?.resolveService(service, object : NsdManager.ResolveListener {
                    override fun onResolveFailed(info: NsdServiceInfo, errorCode: Int) {}
                    override fun onServiceResolved(info: NsdServiceInfo) {
                        val host = info.host?.hostAddress ?: return
                        if (transport == null || transport?.isConnected != true) {
                            scope.launch { tryConnect(host, "Wi-Fi (NSD)") }
                        }
                    }
                })
            } catch (e: Exception) {
                Log.w(TAG, "Resolve service error: ${e.message}")
            }
        }

        override fun onServiceLost(service: NsdServiceInfo) {}
    }
}
