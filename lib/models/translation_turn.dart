/// Represents a single translation turn or conversational segment.
class TranslationTurn {
  final String id;
  final DateTime timestamp;
  String sourceText; // Live streaming recognized source text
  String targetText; // Live streaming translated target text
  bool isFinal; // Whether this turn has finished generating
  final String targetLanguageCode;

  TranslationTurn({
    required this.id,
    required this.timestamp,
    this.sourceText = '',
    this.targetText = '',
    this.isFinal = false,
    required this.targetLanguageCode,
  });

  TranslationTurn copyWith({
    String? sourceText,
    String? targetText,
    bool? isFinal,
    String? targetLanguageCode,
  }) {
    return TranslationTurn(
      id: id,
      timestamp: timestamp,
      sourceText: sourceText ?? this.sourceText,
      targetText: targetText ?? this.targetText,
      isFinal: isFinal ?? this.isFinal,
      targetLanguageCode: targetLanguageCode ?? this.targetLanguageCode,
    );
  }
}
