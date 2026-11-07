package com.gemini.livetranslate.testapp

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.os.Build
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

                    if (AudioCaptureService.mediaProjection != null) {
                        startCaptureService()
                        result.success(true)
                    } else {
                        pendingResult = result
                        val mediaProjectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
                        startActivityForResult(mediaProjectionManager.createScreenCaptureIntent(), REQUEST_MEDIA_PROJECTION)
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
                val mediaProjectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
                AudioCaptureService.mediaProjection = mediaProjectionManager.getMediaProjection(resultCode, data)
                startCaptureService()
                pendingResult?.success(true)
            } else {
                pendingResult?.error("PERMISSION_DENIED", "使用者拒絕了系統音訊錄製權限", null)
            }
            pendingResult = null
        }
    }

    private fun startCaptureService() {
        val intent = Intent(this, AudioCaptureService::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
    }

    override fun onDestroy() {
        AudioCaptureService.stopCapture(this)
        super.onDestroy()
    }
}
