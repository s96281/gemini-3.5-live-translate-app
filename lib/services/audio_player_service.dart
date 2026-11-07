import 'dart:async';
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';

/// Service responsible for playing translated speech audio chunks (24kHz, 16-bit Mono PCM)
/// returned by Gemini 3.5 Live Translate.
class AudioPlayerService {
  final AudioPlayer _player = AudioPlayer();
  final List<int> _pcmBuffer = [];
  bool _isPlaying = false;
  Timer? _flushTimer;

  bool _isMuted = false;

  bool get isMuted => _isMuted;
  set isMuted(bool val) {
    _isMuted = val;
    if (val) {
      stop();
    }
  }

  AudioPlayerService() {
    _player.onPlayerComplete.listen((_) {
      _isPlaying = false;
      _playNextIfAvailable();
    });
  }

  /// Feed 24kHz PCM chunk from Gemini Live
  void feedPcmChunk(Uint8List pcmChunk) {
    if (_isMuted) return;

    _pcmBuffer.addAll(pcmChunk);

    // Schedule playback batch (e.g., every 300ms of buffered audio to prevent stuttering)
    _flushTimer?.cancel();
    _flushTimer = Timer(const Duration(milliseconds: 250), () {
      _playNextIfAvailable();
    });
  }

  Future<void> _playNextIfAvailable() async {
    if (_isPlaying || _pcmBuffer.isEmpty || _isMuted) return;

    // Grab accumulated PCM
    final dataToPlay = Uint8List.fromList(_pcmBuffer);
    _pcmBuffer.clear();

    if (dataToPlay.length < 1000) return;

    try {
      _isPlaying = true;
      // Wrap raw 24kHz 16-bit mono PCM with a standard RIFF/WAV header
      final wavData = _wrapPcmWithWavHeader(dataToPlay, sampleRate: 24000, numChannels: 1);
      await _player.play(BytesSource(wavData));
    } catch (_) {
      _isPlaying = false;
    }
  }

  /// Construct standard RIFF/WAV container around raw PCM bytes
  Uint8List _wrapPcmWithWavHeader(Uint8List pcmBytes, {required int sampleRate, required int numChannels}) {
    final int byteRate = sampleRate * numChannels * 2;
    final int blockAlign = numChannels * 2;
    final int dataSize = pcmBytes.length;
    final int chunkSize = 36 + dataSize;

    final header = ByteData(44);

    // RIFF chunk descriptor
    header.setUint8(0, 0x52); // 'R'
    header.setUint8(1, 0x49); // 'I'
    header.setUint8(2, 0x46); // 'F'
    header.setUint8(3, 0x46); // 'F'
    header.setUint32(4, chunkSize, Endian.little);
    header.setUint8(8, 0x57);  // 'W'
    header.setUint8(9, 0x41);  // 'A'
    header.setUint8(10, 0x56); // 'V'
    header.setUint8(11, 0x45); // 'E'

    // "fmt " sub-chunk
    header.setUint8(12, 0x66); // 'f'
    header.setUint8(13, 0x6D); // 'm'
    header.setUint8(14, 0x74); // 't'
    header.setUint8(15, 0x20); // ' '
    header.setUint32(16, 16, Endian.little); // Subchunk1Size for PCM
    header.setUint16(20, 1, Endian.little);  // AudioFormat (1 = Linear PCM)
    header.setUint16(22, numChannels, Endian.little);
    header.setUint32(24, sampleRate, Endian.little);
    header.setUint32(28, byteRate, Endian.little);
    header.setUint16(32, blockAlign, Endian.little);
    header.setUint16(34, 16, Endian.little); // BitsPerSample (16-bit)

    // "data" sub-chunk
    header.setUint8(36, 0x64); // 'd'
    header.setUint8(37, 0x61); // 'a'
    header.setUint8(38, 0x74); // 't'
    header.setUint8(39, 0x61); // 'a'
    header.setUint32(40, dataSize, Endian.little);

    final wavBuffer = Uint8List(44 + dataSize);
    wavBuffer.setRange(0, 44, header.buffer.asUint8List());
    wavBuffer.setRange(44, 44 + dataSize, pcmBytes);
    return wavBuffer;
  }

  Future<void> stop() async {
    _flushTimer?.cancel();
    _pcmBuffer.clear();
    _isPlaying = false;
    await _player.stop();
  }

  void dispose() {
    stop();
    _player.dispose();
  }
}
