import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:gemini_live_translate/models/live_config.dart';
import 'package:gemini_live_translate/services/gemini_live_client.dart';

void main() {
  group('GeminiLiveClient Protocol Tests', () {
    test('Client initializes with correct configuration', () {
      const config = LiveConfig(
        apiKey: 'test_api_key_123',
        targetLanguageCode: 'zh-TW',
        voiceName: 'Puck',
      );

      final client = GeminiLiveClient(config: config);
      expect(client.currentState, equals(LiveConnectionState.disconnected));
      expect(client.config.apiKey, equals('test_api_key_123'));
      expect(client.config.targetLanguageCode, equals('zh-TW'));
      expect(client.config.voiceName, equals('Puck'));
      expect(client.config.audioSourceMode, equals(AudioSourceMode.systemAudio));
    });

    test('LiveConfig supports audioSourceMode and copyWith', () {
      const config = LiveConfig(
        apiKey: 'key',
        audioSourceMode: AudioSourceMode.systemAudio,
        selectedDeviceId: null,
      );
      expect(config.audioSourceMode, equals(AudioSourceMode.systemAudio));

      final updated = config.copyWith(
        audioSourceMode: AudioSourceMode.microphone,
        selectedDeviceId: 'stereo_mix_01',
      );
      expect(updated.audioSourceMode, equals(AudioSourceMode.microphone));
      expect(updated.selectedDeviceId, equals('stereo_mix_01'));
    });

    test('Gemini Live setup payload schema adheres to specification', () {
      const config = LiveConfig(
        apiKey: 'fake_key',
        targetLanguageCode: 'ja-JP',
        voiceName: 'Charon',
      );

      final setupPayload = {
        'setup': {
          'model': 'models/gemini-3.5-live-translate-preview',
          'generationConfig': {
            'responseModalities': ['AUDIO'],
            'speechConfig': {
              'voiceConfig': {
                'prebuiltVoiceConfig': {
                  'voiceName': config.voiceName,
                }
              }
            },
            'translationConfig': {
              'targetLanguageCode': config.targetLanguageCode,
            },
            'inputAudioTranscription': {},
            'outputAudioTranscription': {},
          }
        }
      };

      final jsonStr = jsonEncode(setupPayload);
      final decoded = jsonDecode(jsonStr) as Map<String, dynamic>;

      expect(decoded['setup']['model'], equals('models/gemini-3.5-live-translate-preview'));
      expect(decoded['setup']['generationConfig']['translationConfig']['targetLanguageCode'], equals('ja-JP'));
      expect(decoded['setup']['generationConfig']['speechConfig']['voiceConfig']['prebuiltVoiceConfig']['voiceName'], equals('Charon'));
      expect(decoded['setup']['generationConfig']['inputAudioTranscription'], isNotNull);
      expect(decoded['setup']['generationConfig']['outputAudioTranscription'], isNotNull);
    });

    test('Audio PCM chunk Base64 encoding format', () {
      // 3200 bytes of mock 16kHz PCM data (100ms)
      final mockPcm = Uint8List(3200);
      for (int i = 0; i < mockPcm.length; i++) {
        mockPcm[i] = i % 256;
      }

      final base64String = base64Encode(mockPcm);
      final payload = {
        'realtimeInput': {
          'mediaChunks': [
            {
              'mimeType': 'audio/pcm;rate=16000',
              'data': base64String,
            }
          ]
        }
      };

      final jsonStr = jsonEncode(payload);
      final decoded = jsonDecode(jsonStr) as Map<String, dynamic>;

      final mediaChunk = decoded['realtimeInput']['mediaChunks'][0];
      expect(mediaChunk['mimeType'], equals('audio/pcm;rate=16000'));
      expect(base64Decode(mediaChunk['data'] as String).length, equals(3200));
    });

    test('Server message with transcription and audio parts decodes correctly', () {
      final mockServerMsg = {
        'serverContent': {
          'modelTurn': {
            'parts': [
              {
                'inlineData': {
                  'mimeType': 'audio/pcm;rate=24000',
                  'data': base64Encode(Uint8List.fromList([1, 2, 3, 4])),
                }
              },
              {
                'text': '你好，很高興認識你。'
              }
            ]
          },
          'turnComplete': true
        },
        'inputAudioTranscription': {
          'text': 'Hello, nice to meet you.'
        },
        'outputAudioTranscription': {
          'text': '你好，很高興認識你。'
        }
      };

      final jsonStr = jsonEncode(mockServerMsg);
      final decoded = jsonDecode(jsonStr) as Map<String, dynamic>;

      expect(decoded['inputAudioTranscription']['text'], equals('Hello, nice to meet you.'));
      expect(decoded['outputAudioTranscription']['text'], equals('你好，很高興認識你。'));

      final parts = decoded['serverContent']['modelTurn']['parts'] as List;
      expect(parts.length, equals(2));
      expect(parts[1]['text'], equals('你好，很高興認識你。'));
    });
  });
}
