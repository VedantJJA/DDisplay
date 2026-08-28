package com.ddisplay.app.ui

import android.content.Context
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.ddisplay.app.databinding.ActivitySettingsBinding

class SettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySettingsBinding

    companion object {
        const val PREFS_NAME = "ddisplay_settings"
        const val KEY_PORT = "pref_port"
        const val KEY_KEEP_SCREEN_ON = "pref_keep_screen_on"
        const val KEY_LOW_LATENCY = "pref_low_latency"
        const val KEY_ORIENTATION_LOCK = "pref_orientation_lock"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivitySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

        // Load current settings
        binding.etPort.setText(prefs.getInt(KEY_PORT, 7878).toString())
        binding.switchKeepScreenOn.isChecked = prefs.getBoolean(KEY_KEEP_SCREEN_ON, true)
        binding.switchLowLatency.isChecked = prefs.getBoolean(KEY_LOW_LATENCY, true)
        binding.switchOrientationLock.isChecked = prefs.getBoolean(KEY_ORIENTATION_LOCK, false)

        binding.btnSaveSettings.setOnClickListener {
            val portText = binding.etPort.text?.toString()?.trim() ?: "7878"
            val port = portText.toIntOrNull() ?: 7878

            prefs.edit()
                .putInt(KEY_PORT, port)
                .putBoolean(KEY_KEEP_SCREEN_ON, binding.switchKeepScreenOn.isChecked)
                .putBoolean(KEY_LOW_LATENCY, binding.switchLowLatency.isChecked)
                .putBoolean(KEY_ORIENTATION_LOCK, binding.switchOrientationLock.isChecked)
                .apply()

            finish()
        }
    }
}
