package com.ddisplay.app.transport

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private const val TAG = "TransportManager"
private const val USB_ADB_HOST = "127.0.0.1"
private const val NSD_SERVICE_TYPE = "_ddisplay._tcp."

/**
 * Manages transport selection for the Android client side.
 *
 * Priority:
 *   1. USB-ADB: connect to localhost -- works automatically when the Windows host
 *      has run `adb reverse tcp:PORT tcp:PORT`.
 *   2. NSD-discovered Wi-Fi host.
 *   3. Manually specified host IP.
 */
class TransportManager(
    private val context: Context,
    private val port: Int = 7878,
) {
    private val scope = CoroutineScope(Dispatchers.IO)
    private var transport: SocketTransport? = null
    private var nsdManager: NsdManager? = null

    var onConnected: ((SocketTransport, String) -> Unit)? = null
    var onConnectionFailed: ((String) -> Unit)? = null

    /**
     * Tries to connect to the Windows host, in priority order.
     */
    fun connectAuto() {
        scope.launch {
            // 1. Try USB-ADB (localhost).
            if (tryConnect(USB_ADB_HOST, "USB")) return@launch
            // 2. Start NSD browse and wait for a discovered host.
            startNsdBrowse()
        }
    }

    /**
     * Connects directly to a manually-specified host.
     */
    fun connectManual(host: String) {
        scope.launch {
            tryConnect(host, "Wi-Fi (manual)")
        }
    }

    fun disconnect() {
        nsdManager?.stopServiceDiscovery(discoveryListener)
        transport?.disconnect()
        transport = null
    }

    private suspend fun tryConnect(host: String, label: String): Boolean {
        return try {
            val t = SocketTransport(host, port)
            withContext(Dispatchers.IO) { t.connect() }
            transport = t
            t.startReadLoop(scope)
            withContext(Dispatchers.Main) { onConnected?.invoke(t, label) }
            true
        } catch (e: Exception) {
            Log.d(TAG, "Failed to connect to $host via $label: ${e.message}")
            false
        }
    }

    private fun startNsdBrowse() {
        nsdManager = context.getSystemService(Context.NSD_SERVICE) as NsdManager
        nsdManager?.discoverServices(NSD_SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
    }

    private val discoveryListener = object : NsdManager.DiscoveryListener {
        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
            onConnectionFailed?.invoke("NSD discovery start failed: $errorCode")
        }
        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onDiscoveryStarted(serviceType: String) {}
        override fun onDiscoveryStopped(serviceType: String) {}

        override fun onServiceFound(service: NsdServiceInfo) {
            nsdManager?.resolveService(service, object : NsdManager.ResolveListener {
                override fun onResolveFailed(info: NsdServiceInfo, errorCode: Int) {}
                override fun onServiceResolved(info: NsdServiceInfo) {
                    val host = info.host?.hostAddress ?: return
                    scope.launch { tryConnect(host, "Wi-Fi (NSD)") }
                }
            })
        }

        override fun onServiceLost(service: NsdServiceInfo) {}
    }
}
