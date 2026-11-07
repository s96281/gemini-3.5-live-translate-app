import 'dart:async';
import 'dart:math';
import 'dart:typed_data';

import 'package:record/record.dart';

/// Service responsible for recording raw 16kHz 16-bit linear PCM audio from the device
/// microphone and emitting 100ms chunks (3200 bytes) along with normalized amplitude.
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

  bool get isRecording => _isRecording;
  Stream<Uint8List> get audioChunkStream => _chunkController.stream;
  Stream<double> get amplitudeStream => _amplitudeController.stream;

  /// Check and request microphone permission
  Future<bool> hasPermission() async {
    return await _audioRecorder.hasPermission();
  }

  /// Start streaming raw 16kHz PCM audio
  Future<void> startRecording() async {
    if (_isRecording) return;

    final hasPerm = await hasPermission();
    if (!hasPerm) {
      throw Exception('未獲得麥克風權限，請於系統設定中授權。');
    }

    _buffer.clear();

    // Configure 16kHz, 16-bit, Mono Linear PCM stream
    final recordStream = await _audioRecorder.startStream(
      const RecordConfig(
        encoder: AudioEncoder.pcm16bits,
        sampleRate: 16000,
        numChannels: 1,
        bitRate: 256000,
        noiseSuppress: true,
        echoCancel: true,
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
