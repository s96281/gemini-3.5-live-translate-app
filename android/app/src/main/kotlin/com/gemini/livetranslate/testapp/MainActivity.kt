package com.gemini.livetranslate.testapp

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.os.Build
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {

    private val METHOD_CHANNEL = "com.gemini.livetranslate/system_audio"
    private val EVENT_CHANNEL = "com.gemini.livetranslate/system_audio_stream"
    private val REQUEST_MEDIA_PROJECTION = 2001

    private var eventSink: EventChannel.EventSink? = null
    private var pendingResult: MethodChannel.Result? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)

        // MethodChannel
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, METHOD_CHANNEL).setMethodCallHandler { call, result ->
            when (call.method) {
                "isSupported" -> {
                    // Android 10+ (API 29+) 支援 AudioPlaybackCapture
                    result.success(Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q)
                }
                "startCapture" -> {
                    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
                        result.error("UNSUPPORTED", "系統音效擷取需要 Android 10 (API 29) 或更高版本", null)
                        return@setMethodCallHandler
                    }

                    pendingResult = result
                    try {
                        val mediaProjectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
                        startActivityForResult(mediaProjectionManager.createScreenCaptureIntent(), REQUEST_MEDIA_PROJECTION)
                    } catch (e: Exception) {
                        result.error("FAILED", "無法啟動權限請求: ${e.message}", null)
                        pendingResult = null
                    }
                }
                "stopCapture" -> {
                    AudioCaptureService.stopCapture(this)
                    result.success(true)
                }
                else -> {
                    result.notImplemented()
                }
            }
        }

        // EventChannel
        EventChannel(flutterEngine.dartExecutor.binaryMessenger, EVENT_CHANNEL).setStreamHandler(
            object : EventChannel.StreamHandler {
                override fun onListen(arguments: Any?, events: EventChannel.EventSink?) {
                    eventSink = events
                    AudioCaptureService.audioDataListener = { byteArray ->
                        runOnUiThread {
                            eventSink?.success(byteArray)
                        }
                    }
                    AudioCaptureService.audioErrorListener = { errMsg ->
                        runOnUiThread {
                            eventSink?.error("AUDIO_CAPTURE_ERROR", errMsg, null)
                        }
                    }
                }

                override fun onCancel(arguments: Any?) {
                    AudioCaptureService.audioDataListener = null
                    AudioCaptureService.audioErrorListener = null
                    eventSink = null
                }
            }
        )
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQUEST_MEDIA_PROJECTION) {
            if (resultCode == Activity.RESULT_OK && data != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                try {
                    val serviceIntent = Intent(this, AudioCaptureService::class.java).apply {
                        putExtra(AudioCaptureService.EXTRA_RESULT_CODE, resultCode)
                        putExtra(AudioCaptureService.EXTRA_DATA_INTENT, data)
                    }
                    ContextCompat.startForegroundService(this, serviceIntent)
                    pendingResult?.success(true)
                } catch (e: Exception) {
                    pendingResult?.error("SERVICE_START_FAILED", "啟動音訊服務失敗: ${e.message}", null)
                }
            } else {
                pendingResult?.error("PERMISSION_DENIED", "使用者已取消系統音訊錄製權限", null)
            }
            pendingResult = null
        }
    }

    override fun onDestroy() {
        AudioCaptureService.stopCapture(this)
        super.onDestroy()
    }
}
