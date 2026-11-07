import 'dart:async';
import 'dart:math';
import 'dart:typed_data';

import 'package:record/record.dart';
import '../models/live_config.dart';

/// Service responsible for recording raw 16kHz 16-bit linear PCM audio from the device
/// (system playback audio or microphone) and emitting 100ms chunks (3200 bytes) along with normalized amplitude.
class AudioRecorderService {
  final AudioRecorder _audioRecorder = AudioRecorder();
  StreamSubscription<Uint8List>? _recordStreamSub;

  final StreamController<Uint8List> _chunkController =
      StreamController<Uint8List>.broadcast();
  final StreamController<double> _amplitudeController =
      StreamController<double>.broadcast();

  // Target: 100ms of 16kHz 16-bit mono PCM = 1600 samples * 2 bytes = 3200 bytes
  static const int targetChunkSize = 3200;
  final List<int> _buffer = [];

  bool _isRecording = false;
  AudioSourceMode _currentMode = AudioSourceMode.systemAudio;

  bool get isRecording => _isRecording;
  AudioSourceMode get currentMode => _currentMode;
  Stream<Uint8List> get audioChunkStream => _chunkController.stream;
  Stream<double> get amplitudeStream => _amplitudeController.stream;

  /// Check and request audio recording permission
  Future<bool> hasPermission() async {
    return await _audioRecorder.hasPermission();
  }

  /// List available audio input devices (e.g. Microphones, Stereo Mix, Virtual Audio Cables)
  Future<List<InputDevice>> getAvailableInputDevices() async {
    try {
      return await _audioRecorder.listInputDevices();
    } catch (_) {
      return [];
    }
  }

  /// Start streaming raw 16kHz PCM audio
  /// [sourceMode]: [AudioSourceMode.systemAudio] or [AudioSourceMode.microphone]
  /// [deviceId]: Optional specific input device ID (e.g. Stereo Mix / VB-CABLE / Specific Mic)
  Future<void> startRecording({
    AudioSourceMode sourceMode = AudioSourceMode.systemAudio,
    String? deviceId,
  }) async {
    if (_isRecording) return;

    final hasPerm = await hasPermission();
    if (!hasPerm) {
      throw Exception('未獲得音訊錄製權限，請於系統設定中授權。');
    }

    _currentMode = sourceMode;
    _buffer.clear();

    InputDevice? selectedDevice;
    final devices = await getAvailableInputDevices();

    if (deviceId != null && deviceId.isNotEmpty) {
      selectedDevice = devices.cast<InputDevice?>().firstWhere(
            (d) => d?.id == deviceId,
            orElse: () => null,
          );
    } else if (sourceMode == AudioSourceMode.systemAudio) {
      // 智慧比對系統播放音效裝置（立體聲混音 / Stereo Mix / Loopback / Virtual Cable）
      selectedDevice = devices.cast<InputDevice?>().firstWhere(
            (d) {
              if (d == null) return false;
              final label = d.label.toLowerCase();
              return label.contains('stereo mix') ||
                  label.contains('立體聲混音') ||
                  label.contains('what u hear') ||
                  label.contains('loopback') ||
                  label.contains('cable') ||
                  label.contains('virtual') ||
                  label.contains('system');
            },
            orElse: () => null,
          );
    }

    // 依據音訊來源調整 DSP 濾波：
    // 錄製系統音效時必須關閉 echoCancel 與 noiseSuppress，避免作業系統誤將播放音效濾除
    final bool isSystemAudio = (sourceMode == AudioSourceMode.systemAudio);

    final recordStream = await _audioRecorder.startStream(
      RecordConfig(
        encoder: AudioEncoder.pcm16bits,
        sampleRate: 16000,
        numChannels: 1,
        bitRate: 256000,
        device: selectedDevice,
        noiseSuppress: isSystemAudio ? false : true,
        echoCancel: isSystemAudio ? false : true,
        autoGain: isSystemAudio ? false : true,
      ),
    );

    _isRecording = true;

    _recordStreamSub = recordStream.listen((data) {
      _processIncomingPcm(data);
    }, onError: (err) {
      _isRecording = false;
    }, onDone: () {
      _isRecording = false;
    });
  }

  /// Process incoming bytes, split into 3200-byte (100ms) chunks and calculate RMS amplitude
  void _processIncomingPcm(Uint8List incoming) {
    _buffer.addAll(incoming);

    // Calculate RMS amplitude for visualizer from incoming PCM samples
    if (incoming.length >= 2) {
      double sumSquares = 0.0;
      int sampleCount = incoming.length ~/ 2;
      final byteData = ByteData.sublistView(incoming);

      for (int i = 0; i < incoming.length - 1; i += 2) {
        int sample = byteData.getInt16(i, Endian.little);
        sumSquares += sample * sample;
      }

      double rms = sqrt(sumSquares / sampleCount);
      // Normalize to 0.0 ~ 1.0 (typical max int16 is 32767, speech is around 500-15000)
      double normalized = (rms / 8000.0).clamp(0.0, 1.0);
      _amplitudeController.add(normalized);
    }

    // Emit 100ms chunks (3200 bytes)
    while (_buffer.length >= targetChunkSize) {
      final chunk = Uint8List.fromList(_buffer.sublist(0, targetChunkSize));
      _buffer.removeRange(0, targetChunkSize);
      _chunkController.add(chunk);
    }
  }

  /// Stop audio recording
  Future<void> stopRecording() async {
    if (!_isRecording) return;
    _isRecording = false;

    await _recordStreamSub?.cancel();
    _recordStreamSub = null;
    await _audioRecorder.stop();

    // Flush any leftover buffer if it has meaningful data
    if (_buffer.length >= 800) {
      _chunkController.add(Uint8List.fromList(_buffer));
    }
    _buffer.clear();
    _amplitudeController.add(0.0);
  }

  void dispose() {
    stopRecording();
    _audioRecorder.dispose();
    _chunkController.close();
    _amplitudeController.close();
  }
}
