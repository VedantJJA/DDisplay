package com.ddisplay.app.transport

import com.ddisplay.app.protocol.ControlMessages
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * TCP socket transport shared by both the USB-ADB and Wi-Fi paths.
 * Buffers control messages so none are lost during activity/handler transitions.
 */
class SocketTransport(
    private val host: String,
    private val port: Int,
) {
    companion object {
        const val TAG_CONTROL: Byte = 0x01
        const val TAG_MEDIA: Byte = 0x02
    }

    private var socket: Socket? = null
    private var input: DataInputStream? = null
    private var output: DataOutputStream? = null
    private var readJob: Job? = null
    private val messageQueue = ConcurrentLinkedQueue<JSONObject>()

    var onControlMessageReceived: ((JSONObject) -> Unit)? = null
        set(value) {
            field = value
            if (value != null) {
                while (!messageQueue.isEmpty()) {
                    val msg = messageQueue.poll() ?: break
                    value.invoke(msg)
                }
            }
        }

    var onMediaFrameReceived: ((ByteArray, Boolean, Long) -> Unit)? = null
    var onDisconnected: ((String) -> Unit)? = null

    val isConnected: Boolean get() = socket?.isConnected == true && socket?.isClosed == false

    fun connect() {
        val s = Socket(host, port)
        s.tcpNoDelay = true
        socket = s
        input = DataInputStream(s.getInputStream())
        output = DataOutputStream(s.getOutputStream())
    }

    fun startReadLoop(scope: CoroutineScope) {
        readJob = scope.launch(Dispatchers.IO) {
            val stream = input ?: return@launch
            try {
                while (true) {
                    val payloadLength = stream.readInt()
                    val tag = stream.readByte()
                    val payload = ByteArray(payloadLength)
                    stream.readFully(payload)

                    when (tag) {
                        TAG_CONTROL -> {
                            val json = JSONObject(String(payload, Charsets.UTF_8))
                            val handler = onControlMessageReceived
                            if (handler != null) {
                                handler.invoke(json)
                            } else {
                                messageQueue.add(json)
                            }
                        }
                        TAG_MEDIA -> {
                            if (payload.size >= 5) {
                                val flags = payload[0]
                                val isKeyframe = (flags.toInt() and 0x01) != 0
                                val pts = ByteBuffer.wrap(payload, 1, 4)
                                    .order(ByteOrder.BIG_ENDIAN).int.toLong()
                                val nalData = payload.copyOfRange(5, payload.size)
                                onMediaFrameReceived?.invoke(nalData, isKeyframe, pts)
                            }
                        }
                    }
                }
            } catch (e: Exception) {
                onDisconnected?.invoke(e.message ?: "Read error")
            }
        }
    }

    fun sendControlMessage(json: JSONObject) {
        val jsonBytes = json.toString().toByteArray(Charsets.UTF_8)
        val out = output ?: return
        synchronized(out) {
            out.writeInt(jsonBytes.size)
            out.write(TAG_CONTROL.toInt())
            out.write(jsonBytes)
            out.flush()
        }
    }

    fun disconnect() {
        readJob?.cancel()
        try {
            val out = output
            if (out != null) {
                sendControlMessage(ControlMessages.bye())
            }
        } catch (_: Exception) {}
        socket?.close()
        socket = null
        input = null
        output = null
    }
}
