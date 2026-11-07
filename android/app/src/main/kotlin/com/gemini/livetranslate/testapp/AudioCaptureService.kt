package com.gemini.livetranslate.testapp

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import android.os.Build
import android.os.IBinder
import androidx.annotation.RequiresApi
import java.util.concurrent.atomic.AtomicBoolean

class AudioCaptureService : Service() {

    companion object {
        const val CHANNEL_ID = "GeminiAudioCaptureChannel"
        const val NOTIFICATION_ID = 1001

        var mediaProjection: MediaProjection? = null
        var audioDataListener: ((ByteArray) -> Unit)? = null
        var audioErrorListener: ((String) -> Unit)? = null

        private val isRecording = AtomicBoolean(false)
        private var recordingThread: Thread? = null
        private var audioRecord: AudioRecord? = null

        fun stopCapture(context: Context) {
            isRecording.set(false)
            try {
                audioRecord?.stop()
                audioRecord?.release()
                audioRecord = null
            } catch (_: Exception) {}

            try {
                mediaProjection?.stop()
                mediaProjection = null
            } catch (_: Exception) {}

            val intent = Intent(context, AudioCaptureService::class.java)
            context.stopService(intent)
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val notification = createNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
            } else {
                startForeground(NOTIFICATION_ID, notification)
            }
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startAudioRecordQ()
        } else {
            audioErrorListener?.invoke("AudioPlaybackCapture requires Android 10 (API 29) or higher.")
            stopSelf()
        }

        return START_NOT_STICKY
    }

    @RequiresApi(Build.VERSION_CODES.Q)
    private fun startAudioRecordQ() {
        val projection = mediaProjection
        if (projection == null) {
            audioErrorListener?.invoke("MediaProjection is null")
            stopSelf()
            return
        }

        val config = AudioPlaybackCaptureConfiguration.Builder(projection)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
            .build()

        val sampleRate = 16000
        val channelConfig = AudioFormat.CHANNEL_IN_MONO
        val audioFormat = AudioFormat.ENCODING_PCM_16BIT

        val minBufferSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
        // 100ms chunk for 16kHz 16-bit mono = 3200 bytes
        val bufferSize = maxOf(minBufferSize, 3200 * 2)

        try {
            audioRecord = AudioRecord.Builder()
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setEncoding(audioFormat)
                        .setSampleRate(sampleRate)
                        .setChannelMask(channelConfig)
                        .build()
                )
                .setAudioPlaybackCaptureConfig(config)
                .setBufferSizeInBytes(bufferSize)
                .build()

            if (audioRecord?.state != AudioRecord.STATE_INITIALIZED) {
                audioErrorListener?.invoke("AudioRecord failed to initialize")
                stopSelf()
                return
            }

            audioRecord?.startRecording()
            isRecording.set(true)

            recordingThread = Thread {
                val chunk = ByteArray(3200) // 100ms
                while (isRecording.get()) {
                    val bytesRead = audioRecord?.read(chunk, 0, chunk.size) ?: -1
                    if (bytesRead > 0) {
                        val dataToSend = if (bytesRead == chunk.size) {
                            chunk.clone()
                        } else {
                            chunk.copyOf(bytesRead)
                        }
                        audioDataListener?.invoke(dataToSend)
                    }
                }
            }.apply {
                priority = Thread.MAX_PRIORITY
                start()
            }
        } catch (e: Exception) {
            audioErrorListener?.invoke("Exception starting AudioRecord: ${e.message}")
            stopSelf()
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Gemini System Audio Capture",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "正在擷取本機播放音效以進行即時口譯"
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager?.createNotificationChannel(channel)
        }
    }

    private fun createNotification(): Notification {
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }

        return builder
            .setContentTitle("Gemini Live 即時口譯")
            .setContentText("正在擷取裝置系統播放音效...")
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .build()
    }

    override fun onDestroy() {
        isRecording.set(false)
        try {
            recordingThread?.interrupt()
            recordingThread = null
        } catch (_: Exception) {}
        try {
            audioRecord?.stop()
            audioRecord?.release()
            audioRecord = null
        } catch (_: Exception) {}
        super.onDestroy()
    }
}
