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
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Log
import androidx.annotation.RequiresApi
import java.util.concurrent.atomic.AtomicBoolean

class AudioCaptureService : Service() {

    companion object {
        private const val TAG = "AudioCaptureService"
        const val CHANNEL_ID = "GeminiAudioCaptureChannel"
        const val NOTIFICATION_ID = 1001

        const val EXTRA_RESULT_CODE = "EXTRA_RESULT_CODE"
        const val EXTRA_DATA_INTENT = "EXTRA_DATA_INTENT"

        var audioDataListener: ((ByteArray) -> Unit)? = null
        var audioErrorListener: ((String) -> Unit)? = null

        private val isRecording = AtomicBoolean(false)
        private var recordingThread: Thread? = null
        private var audioRecord: AudioRecord? = null
        private var mediaProjection: MediaProjection? = null

        fun stopCapture(context: Context) {
            isRecording.set(false)
            try {
                audioRecord?.stop()
                audioRecord?.release()
                audioRecord = null
            } catch (e: Exception) {
                Log.w(TAG, "Error stopping AudioRecord", e)
            }

            try {
                mediaProjection?.stop()
                mediaProjection = null
            } catch (e: Exception) {
                Log.w(TAG, "Error stopping MediaProjection", e)
            }

            try {
                val intent = Intent(context, AudioCaptureService::class.java)
                context.stopService(intent)
            } catch (e: Exception) {
                Log.w(TAG, "Error stopping service", e)
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val notification = createNotification()

        // 1. 依照 Android 14+ 規範，必須先進入 MediaProjection 類型的 Foreground 狀態
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
            } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                startForeground(NOTIFICATION_ID, notification)
            } else {
                startForeground(NOTIFICATION_ID, notification)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to startForeground", e)
            audioErrorListener?.invoke("無法啟動前台服務: ${e.message}")
            stopSelf()
            return START_NOT_STICKY
        }

        // 2. 獲取 MediaProjection 並初始化錄製
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val resultCode = intent?.getIntExtra(EXTRA_RESULT_CODE, 0) ?: 0
            val dataIntent = intent?.getParcelableExtra<Intent>(EXTRA_DATA_INTENT)

            if (resultCode != 0 && dataIntent != null) {
                startAudioRecordQ(resultCode, dataIntent)
            } else if (mediaProjection != null) {
                // 已有現成的 projection
                startAudioRecordWithProjection(mediaProjection!!)
            } else {
                audioErrorListener?.invoke("未接收到有效的 MediaProjection 授權資料")
                stopSelf()
            }
        } else {
            audioErrorListener?.invoke("系統音效錄製需要 Android 10 (API 29) 或更高版本")
            stopSelf()
        }

        return START_NOT_STICKY
    }

    @RequiresApi(Build.VERSION_CODES.Q)
    private fun startAudioRecordQ(resultCode: Int, data: Intent) {
        try {
            val projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
            val projection = projectionManager.getMediaProjection(resultCode, data)
            if (projection == null) {
                audioErrorListener?.invoke("無法取得 MediaProjection 實例")
                stopSelf()
                return
            }

            // Android 14+ 強制要求必須在開始使用前註冊 Callback，否則會丟出 IllegalStateException
            projection.registerCallback(object : MediaProjection.Callback() {
                override fun onStop() {
                    super.onStop()
                    Log.i(TAG, "MediaProjection stopped by system or user")
                    stopCapture(applicationContext)
                }
            }, Handler(Looper.getMainLooper()))

            mediaProjection = projection
            startAudioRecordWithProjection(projection)
        } catch (e: Exception) {
            Log.e(TAG, "Exception initializing MediaProjection", e)
            audioErrorListener?.invoke("初始化音訊擷取授權失敗: ${e.message}")
            stopSelf()
        }
    }

    @RequiresApi(Build.VERSION_CODES.Q)
    private fun startAudioRecordWithProjection(projection: MediaProjection) {
        try {
            val config = AudioPlaybackCaptureConfiguration.Builder(projection)
                .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                .addMatchingUsage(AudioAttributes.USAGE_GAME)
                .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                .build()

            val sampleRate = 16000
            val channelConfig = AudioFormat.CHANNEL_IN_MONO
            val audioFormat = AudioFormat.ENCODING_PCM_16BIT

            val minBufferSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
            val bufferSize = maxOf(minBufferSize, 3200 * 2)

            val record = AudioRecord.Builder()
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

            if (record.state != AudioRecord.STATE_INITIALIZED) {
                audioErrorListener?.invoke("AudioRecord 初始化失敗 (State != INITIALIZED)")
                stopSelf()
                return
            }

            record.startRecording()
            audioRecord = record
            isRecording.set(true)

            recordingThread = Thread {
                val chunk = ByteArray(3200) // 100ms PCM 16kHz Mono
                while (isRecording.get()) {
                    try {
                        val bytesRead = audioRecord?.read(chunk, 0, chunk.size) ?: -1
                        if (bytesRead > 0) {
                            val dataToSend = if (bytesRead == chunk.size) {
                                chunk.clone()
                            } else {
                                chunk.copyOf(bytesRead)
                            }
                            audioDataListener?.invoke(dataToSend)
                        } else if (bytesRead < 0) {
                            Thread.sleep(10)
                        }
                    } catch (readEx: Exception) {
                        Log.e(TAG, "Error in audio reading loop", readEx)
                        break
                    }
                }
            }.apply {
                priority = Thread.MAX_PRIORITY
                name = "GeminiSystemAudioCaptureThread"
                start()
            }
        } catch (e: Exception) {
            Log.e(TAG, "Exception starting AudioRecord", e)
            audioErrorListener?.invoke("無法啟動系統音訊錄製: ${e.message}")
            stopSelf()
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            try {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    "Gemini Live 即時口譯",
                    NotificationManager.IMPORTANCE_LOW
                ).apply {
                    description = "正在擷取裝置系統播放音效以進行即時翻譯"
                    setShowBadge(false)
                }
                val manager = getSystemService(NotificationManager::class.java)
                manager?.createNotificationChannel(channel)
            } catch (e: Exception) {
                Log.e(TAG, "Failed to create NotificationChannel", e)
            }
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
            .setContentText("正在擷取裝置播放音效...")
            .setSmallIcon(android.R.drawable.stat_sys_speakerphone)
            .setOngoing(true)
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
        try {
            mediaProjection?.stop()
            mediaProjection = null
        } catch (_: Exception) {}
        super.onDestroy()
    }
}
