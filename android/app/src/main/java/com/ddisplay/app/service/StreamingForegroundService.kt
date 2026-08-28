package com.ddisplay.app.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import com.ddisplay.app.R
import com.ddisplay.app.ui.MainActivity

/**
 * Foreground service that keeps the streaming session alive while the user
 * is not looking at the RenderActivity (e.g., phone screen turns off temporarily).
 */
class StreamingForegroundService : Service() {

    companion object {
        const val CHANNEL_ID = "ddisplay_streaming"
        const val NOTIFICATION_ID = 1001
        const val ACTION_STOP = "com.ddisplay.app.service.ACTION_STOP"

        fun startService(context: Context) {
            val intent = Intent(context, StreamingForegroundService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stopService(context: Context) {
            val intent = Intent(context, StreamingForegroundService::class.java)
            context.stopService(intent)
        }
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }

        startForeground(NOTIFICATION_ID, buildNotification())
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "DDisplay Streaming",
            NotificationManager.IMPORTANCE_LOW,
        ).apply {
            description = "Active while DDisplay is streaming"
        }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(): Notification {
        val openIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE,
        )

        val stopIntent = PendingIntent.getService(
            this, 1,
            Intent(this, StreamingForegroundService::class.java).apply {
                action = ACTION_STOP
            },
            PendingIntent.FLAG_IMMUTABLE,
        )

        return Notification.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.notification_streaming_title))
            .setContentText(getString(R.string.notification_streaming_text))
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentIntent(openIntent)
            .setOngoing(true)
            .addAction(
                android.R.drawable.ic_media_pause,
                getString(R.string.action_stop_streaming),
                stopIntent,
            )
            .build()
    }
}
