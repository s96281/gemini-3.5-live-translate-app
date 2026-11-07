import 'dart:async';
import 'dart:io';
import 'package:flutter/services.dart';

/// Service interfacing with native platform loopback audio capture
/// - Windows: WASAPI Loopback on Render Endpoint (AUDCLNT_STREAMFLAGS_LOOPBACK)
/// - Android: AudioPlaybackCapture API (Android 10+ via MediaProjection)
class NativeSystemAudioService {
  static const MethodChannel _methodChannel =
      MethodChannel('com.gemini.livetranslate/system_audio');
  static const EventChannel _eventChannel =
      EventChannel('com.gemini.livetranslate/system_audio_stream');

  StreamSubscription? _eventSubscription;
  final StreamController<Uint8List> _streamController =
      StreamController<Uint8List>.broadcast();

  bool _isCapturing = false;

  bool get isCapturing => _isCapturing;
  Stream<Uint8List> get audioStream => _streamController.stream;

  /// Check whether the current platform supports true internal audio loopback capture
  static Future<bool> isSupported() async {
    if (!Platform.isWindows && !Platform.isAndroid) {
      return false;
    }
    try {
      final result = await _methodChannel.invokeMethod<bool>('isSupported');
      return result ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Start native system audio capture
  Future<bool> startCapture() async {
    if (_isCapturing) return true;

    try {
      final success = await _methodChannel.invokeMethod<bool>('startCapture');
      if (success != true) {
        return false;
      }

      _isCapturing = true;
      _eventSubscription = _eventChannel.receiveBroadcastStream().listen(
        (dynamic event) {
          if (event is Uint8List) {
            _streamController.add(event);
          } else if (event is List<int>) {
            _streamController.add(Uint8List.fromList(event));
          }
        },
        onError: (err) {
          _isCapturing = false;
        },
        onDone: () {
          _isCapturing = false;
        },
      );

      return true;
    } catch (e) {
      _isCapturing = false;
      rethrow;
    }
  }

  /// Stop native system audio capture
  Future<void> stopCapture() async {
    if (!_isCapturing) return;

    try {
      await _eventSubscription?.cancel();
      _eventSubscription = null;
      await _methodChannel.invokeMethod('stopCapture');
    } catch (_) {
    } finally {
      _isCapturing = false;
    }
  }

  void dispose() {
    stopCapture();
    _streamController.close();
  }
}
