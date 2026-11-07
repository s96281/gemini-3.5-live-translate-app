/// Configuration options for the Gemini 3.5 Live Translate session.
class LiveConfig {
  final String apiKey;
  final String targetLanguageCode;
  final String voiceName;
  final bool autoPlayAudio;
  final bool echoTargetLanguage;

  const LiveConfig({
    required this.apiKey,
    this.targetLanguageCode = 'zh-TW',
    this.voiceName = 'Puck',
    this.autoPlayAudio = false,
    this.echoTargetLanguage = false,
  });

  LiveConfig copyWith({
    String? apiKey,
    String? targetLanguageCode,
    String? voiceName,
    bool? autoPlayAudio,
    bool? echoTargetLanguage,
  }) {
    return LiveConfig(
      apiKey: apiKey ?? this.apiKey,
      targetLanguageCode: targetLanguageCode ?? this.targetLanguageCode,
      voiceName: voiceName ?? this.voiceName,
      autoPlayAudio: autoPlayAudio ?? this.autoPlayAudio,
      echoTargetLanguage: echoTargetLanguage ?? this.echoTargetLanguage,
    );
  }

  // Gemini Live supported voices
  static const List<String> availableVoices = [
    'Puck', // Balanced, natural
    'Charon', // Deep, authoritative
    'Kore', // Calm, friendly
    'Fenrir', // Energetic, clear
    'Aoede', // Gentle, warm
  ];
}
