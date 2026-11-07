/// 音訊來源模式：裝置本身播放音效 (System Audio) 或 實體麥克風 (Microphone)
enum AudioSourceMode {
  systemAudio,
  microphone,
}

/// Configuration options for the Gemini 3.5 Live Translate session.
class LiveConfig {
  final String apiKey;
  final String targetLanguageCode;
  final String voiceName;
  final bool autoPlayAudio;
  final bool echoTargetLanguage;
  final AudioSourceMode audioSourceMode;
  final String? selectedDeviceId;

  const LiveConfig({
    required this.apiKey,
    this.targetLanguageCode = 'zh-TW',
    this.voiceName = 'Puck',
    this.autoPlayAudio = false,
    this.echoTargetLanguage = false,
    this.audioSourceMode = AudioSourceMode.systemAudio,
    this.selectedDeviceId,
  });

  LiveConfig copyWith({
    String? apiKey,
    String? targetLanguageCode,
    String? voiceName,
    bool? autoPlayAudio,
    bool? echoTargetLanguage,
    AudioSourceMode? audioSourceMode,
    String? selectedDeviceId,
  }) {
    return LiveConfig(
      apiKey: apiKey ?? this.apiKey,
      targetLanguageCode: targetLanguageCode ?? this.targetLanguageCode,
      voiceName: voiceName ?? this.voiceName,
      autoPlayAudio: autoPlayAudio ?? this.autoPlayAudio,
      echoTargetLanguage: echoTargetLanguage ?? this.echoTargetLanguage,
      audioSourceMode: audioSourceMode ?? this.audioSourceMode,
      selectedDeviceId: selectedDeviceId ?? this.selectedDeviceId,
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
