import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import '../config/languages.dart';
import '../models/live_config.dart';

enum LiveConnectionState {
  disconnected,
  connecting,
  connected,
  translating,
  error,
}

class GeminiLiveClient {
  final LiveConfig config;
  WebSocket? _webSocket;
  StreamSubscription? _socketSubscription;

  final StreamController<LiveConnectionState> _stateController =
      StreamController<LiveConnectionState>.broadcast();
  final StreamController<String> _sourceTranscriptController =
      StreamController<String>.broadcast();
  final StreamController<String> _targetTranscriptController =
      StreamController<String>.broadcast();
  final StreamController<Uint8List> _audioOutputController =
      StreamController<Uint8List>.broadcast();
  final StreamController<String> _errorController =
      StreamController<String>.broadcast();

  LiveConnectionState _currentState = LiveConnectionState.disconnected;

  GeminiLiveClient({required this.config});

  LiveConnectionState get currentState => _currentState;
  Stream<LiveConnectionState> get stateStream => _stateController.stream;
  Stream<String> get sourceTranscriptStream => _sourceTranscriptController.stream;
  Stream<String> get targetTranscriptStream => _targetTranscriptController.stream;
  Stream<Uint8List> get audioOutputStream => _audioOutputController.stream;
  Stream<String> get errorStream => _errorController.stream;

  void _setState(LiveConnectionState newState) {
    _currentState = newState;
    if (!_stateController.isClosed) {
      _stateController.add(newState);
    }
  }

  /// Connects to Gemini Live Multimodal WebSocket endpoint
  Future<void> connect() async {
    if (config.apiKey.trim().isEmpty) {
      _errorController.add('請先設定有效的 Google AI Studio API Key');
      _setState(LiveConnectionState.error);
      return;
    }

    _setState(LiveConnectionState.connecting);

    try {
      final wsUrl = Uri.parse(
        'wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent?key=${config.apiKey.trim()}',
      );

      _webSocket = await WebSocket.connect(wsUrl.toString()).timeout(
        const Duration(seconds: 15),
        onTimeout: () {
          throw TimeoutException('連線超時，請檢查網路連線或 API Key 是否正確');
        },
      );

      _socketSubscription = _webSocket!.listen(
        _onMessageReceived,
        onError: (err) {
          _errorController.add('WebSocket 錯誤: $err');
          _setState(LiveConnectionState.error);
        },
        onDone: () {
          final code = _webSocket?.closeCode;
          final reason = _webSocket?.closeReason;
          if (code != null && code != 1000) {
            final reasonText = (reason != null && reason.isNotEmpty) ? reason : '未知原因';
            _errorController.add('連線已中斷 (代碼 $code: $reasonText)');
            _setState(LiveConnectionState.error);
          } else {
            _setState(LiveConnectionState.disconnected);
          }
        },
        cancelOnError: true,
      );

      // Send initial setup payload
      _sendSetupPayload();
    } catch (e) {
      _errorController.add('連線失敗: $e');
      _setState(LiveConnectionState.error);
    }
  }

  /// Sends the initial configuration required by Gemini Multimodal Live API
  void _sendSetupPayload() {
    if (_webSocket == null || _webSocket!.readyState != WebSocket.open) return;

    final targetLang = SupportedLanguages.list.firstWhere(
      (l) => l.code == config.targetLanguageCode,
      orElse: () => SupportedLanguages.defaultLanguage,
    );

    final setupPayload = {
      'setup': {
        'model': config.modelName,
        'generationConfig': {
          'responseModalities': ['AUDIO'],
          'speechConfig': {
            'voiceConfig': {
              'prebuiltVoiceConfig': {
                'voiceName': config.voiceName,
              }
            }
          }
        },
        'inputAudioTranscription': {},
        'outputAudioTranscription': {},
        'systemInstruction': {
          'parts': [
            {
              'text':
                  'You are a professional real-time speech-to-speech interpreter. Your sole objective is to simultaneously listen to incoming user speech or audio, and immediately translate it into ${targetLang.nameZh} (${config.targetLanguageCode}). Output the translated speech clearly and naturally in real-time. Do not chat or add explanations, only output the direct translation.'
            }
          ]
        }
      }
    };

    _webSocket!.add(jsonEncode(setupPayload));
  }

  /// Sends 16kHz 16-bit linear PCM audio chunk
  void sendAudioChunk(Uint8List pcm16kChunk) {
    if (_webSocket == null || _webSocket!.readyState != WebSocket.open) return;

    final audioPayload = {
      'realtimeInput': {
        'mediaChunks': [
          {
            'mimeType': 'audio/pcm;rate=16000',
            'data': base64Encode(pcm16kChunk),
          }
        ]
      }
    };

    _webSocket!.add(jsonEncode(audioPayload));
    if (_currentState == LiveConnectionState.connected) {
      _setState(LiveConnectionState.translating);
    }
  }

  /// Handles server-side JSON and binary packets
  void _onMessageReceived(dynamic message) {
    try {
      Map<String, dynamic> data;
      if (message is String) {
        data = jsonDecode(message) as Map<String, dynamic>;
      } else if (message is List<int>) {
        data = jsonDecode(utf8.decode(message)) as Map<String, dynamic>;
      } else {
        return;
      }

      // 1. Setup completion signal
      if (data.containsKey('setupComplete')) {
        _setState(LiveConnectionState.connected);
        return;
      }

      // 2. Process Server Content
      if (data.containsKey('serverContent')) {
        final serverContent = data['serverContent'];
        if (serverContent is Map) {
          // A. User speech input transcription (Source speech)
          if (serverContent.containsKey('inputTranscription')) {
            final trans = serverContent['inputTranscription'];
            if (trans is Map && trans.containsKey('text')) {
              final text = trans['text']?.toString() ?? '';
              if (text.isNotEmpty) {
                _sourceTranscriptController.add(text);
              }
            }
          }
          if (serverContent.containsKey('interimInputTranscription')) {
            final trans = serverContent['interimInputTranscription'];
            if (trans is Map && trans.containsKey('text')) {
              final text = trans['text']?.toString() ?? '';
              if (text.isNotEmpty) {
                _sourceTranscriptController.add(text);
              }
            }
          }

          // B. Model output audio transcription (Translated speech)
          if (serverContent.containsKey('outputTranscription')) {
            final trans = serverContent['outputTranscription'];
            if (trans is Map && trans.containsKey('text')) {
              final text = trans['text']?.toString() ?? '';
              if (text.isNotEmpty) {
                _targetTranscriptController.add(text);
              }
            }
          }

          // C. Model turn parts (Translated audio + Subtitles)
          if (serverContent.containsKey('modelTurn')) {
            final modelTurn = serverContent['modelTurn'];
            if (modelTurn is Map && modelTurn.containsKey('parts')) {
              final parts = modelTurn['parts'] as List?;
              if (parts != null) {
                for (final part in parts) {
                  if (part is Map) {
                    // Extract translated audio (24kHz PCM)
                    if (part.containsKey('inlineData')) {
                      final inline = part['inlineData'];
                      if (inline is Map && inline.containsKey('data')) {
                        final rawBase64 = inline['data']?.toString() ?? '';
                        if (rawBase64.isNotEmpty) {
                          final pcmBytes = base64Decode(rawBase64);
                          _audioOutputController.add(pcmBytes);
                        }
                      }
                    }
                    // Extract text parts (Subtitles)
                    if (part.containsKey('text')) {
                      final text = part['text']?.toString() ?? '';
                      if (text.isNotEmpty) {
                        _targetTranscriptController.add(text);
                      }
                    }
                  }
                }
              }
            }
          }

          // Check for turn completion
          if (serverContent['turnComplete'] == true) {
            if (_currentState == LiveConnectionState.translating) {
              _setState(LiveConnectionState.connected);
            }
          }
        }
      }

      // 3. Process Fallbacks (Top-level transcription fields)
      if (data.containsKey('inputAudioTranscription')) {
        final trans = data['inputAudioTranscription'];
        if (trans is Map && trans.containsKey('text')) {
          final text = trans['text']?.toString() ?? '';
          if (text.isNotEmpty) {
            _sourceTranscriptController.add(text);
          }
        }
      }
      if (data.containsKey('outputAudioTranscription')) {
        final trans = data['outputAudioTranscription'];
        if (trans is Map && trans.containsKey('text')) {
          final text = trans['text']?.toString() ?? '';
          if (text.isNotEmpty) {
            _targetTranscriptController.add(text);
          }
        }
      }
      if (data.containsKey('inputTranscription')) {
        final trans = data['inputTranscription'];
        if (trans is Map && trans.containsKey('text')) {
          final text = trans['text']?.toString() ?? '';
          if (text.isNotEmpty) {
            _sourceTranscriptController.add(text);
          }
        }
      }
      if (data.containsKey('outputTranscription')) {
        final trans = data['outputTranscription'];
        if (trans is Map && trans.containsKey('text')) {
          final text = trans['text']?.toString() ?? '';
          if (text.isNotEmpty) {
            _targetTranscriptController.add(text);
          }
        }
      }
    } catch (_) {}
  }

  /// Disconnects the WebSocket session gracefully
  Future<void> disconnect() async {
    try {
      await _socketSubscription?.cancel();
      _socketSubscription = null;
      await _webSocket?.close();
      _webSocket = null;
    } catch (_) {}
    _setState(LiveConnectionState.disconnected);
  }

  void dispose() {
    disconnect();
    _stateController.close();
    _sourceTranscriptController.close();
    _targetTranscriptController.close();
    _audioOutputController.close();
    _errorController.close();
  }
}
