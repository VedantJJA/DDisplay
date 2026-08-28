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
 * Manages transport selection and auto-connection for the Android client.
 *
 * Runs an auto-listen loop that connects to the PC host whenever available
 * over USB (127.0.0.1:PORT via ADB reverse) or local Wi-Fi.
 */
class TransportManager(
    private val context: Context,
    private val port: Int = 7878,
) {
    private val scope = CoroutineScope(Dispatchers.IO)
    private var transport: SocketTransport? = null
    private var nsdManager: NsdManager? = null
    private var isNsdDiscovering = false
    private var autoListenJob: Job? = null

    var onConnected: ((SocketTransport, String) -> Unit)? = null
    var onConnectionFailed: ((String) -> Unit)? = null
    var onStatusChanged: ((String) -> Unit)? = null

    /**
     * Starts continuous auto-listening for PC connection.
     */
    fun startAutoListen() {
        stopAutoListen()
        autoListenJob = scope.launch {
            while (isActive) {
                if (transport == null || transport?.isConnected != true) {
                    // Try USB-ADB (localhost reverse tunnel)
                    if (tryConnect(USB_ADB_HOST, "USB")) {
                        break
                    }
                }
                delay(2000L)
            }
        }
        startNsdBrowse()
    }

    fun stopAutoListen() {
        autoListenJob?.cancel()
        autoListenJob = null
        stopNsdBrowse()
    }

    /**
     * Manually triggers a single connection attempt.
     */
    fun connectAuto() {
        scope.launch {
            if (tryConnect(USB_ADB_HOST, "USB")) return@launch
            startNsdBrowse()
        }
    }

    /**
     * Connects directly to a manually-specified host IP.
     */
    fun connectManual(host: String) {
        scope.launch {
            tryConnect(host, "Wi-Fi (manual)")
        }
    }

    fun disconnect() {
        stopAutoListen()
        transport?.disconnect()
        transport = null
    }

    private suspend fun tryConnect(host: String, label: String): Boolean {
        return try {
            val t = SocketTransport(host, port)
            withContext(Dispatchers.IO) { t.connect() }
            transport = t
            t.startReadLoop(scope)
            withContext(Dispatchers.Main) { 
                onConnected?.invoke(t, label) 
            }
            true
        } catch (e: Exception) {
            Log.d(TAG, "Attempt to connect to $host via $label: ${e.message}")
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
                        scope.launch { tryConnect(host, "Wi-Fi (NSD)") }
                    }
                })
            } catch (e: Exception) {
                Log.w(TAG, "Resolve service error: ${e.message}")
            }
        }

        override fun onServiceLost(service: NsdServiceInfo) {}
    }
}
