package com.ddisplay.app.ui

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivityPairingBinding
import com.ddisplay.app.transport.TransportManager

class PairingActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPairingBinding
    private lateinit var transportManager: TransportManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPairingBinding.inflate(layoutInflater)
        setContentView(binding.root)

        transportManager = TransportManager(this)

        transportManager.onConnected = { transport, label ->
            // Hand off to MainActivity which handles hello/hello-ack.
            finish()
        }

        transportManager.onConnectionFailed = { reason ->
            runOnUiThread {
                binding.btnPair.isEnabled = true
                // TODO: show error snackbar to the user.
            }
        }

        binding.btnPair.setOnClickListener {
            val host = binding.etHostIp.text?.toString()?.trim() ?: ""
            val code = binding.etPairingCode.text?.toString()?.trim() ?: ""

            if (host.isBlank() || code.isBlank()) return@setOnClickListener

            binding.btnPair.isEnabled = false
            transportManager.connectManual(host)
        }
    }

    override fun onDestroy() {
        transportManager.disconnect()
        super.onDestroy()
    }
}
