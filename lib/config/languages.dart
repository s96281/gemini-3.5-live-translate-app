/// Supported languages catalog for Gemini 3.5 Live Translate Preview.
/// Supports 70+ BCP-47 languages with native names, Traditional Chinese names, and flag emojis.
class LanguageInfo {
  final String code; // BCP-47 language tag
  final String nameZh; // Traditional Chinese display name
  final String nameNative; // Native language name
  final String flag; // Emoji flag

  const LanguageInfo({
    required this.code,
    required this.nameZh,
    required this.nameNative,
    required this.flag,
  });
}

class SupportedLanguages {
  static const LanguageInfo defaultLanguage = LanguageInfo(
    code: 'zh-TW',
    nameZh: '繁體中文 (台灣)',
    nameNative: '繁體中文',
    flag: '🇹🇼',
  );

  static const List<LanguageInfo> list = [
    LanguageInfo(code: 'zh-TW', nameZh: '繁體中文 (台灣)', nameNative: '繁體中文', flag: '🇹🇼'),
    LanguageInfo(code: 'zh-CN', nameZh: '簡體中文', nameNative: '简体中文', flag: '🇨🇳'),
    LanguageInfo(code: 'en-US', nameZh: '英語 (美國)', nameNative: 'English (US)', flag: '🇺🇸'),
    LanguageInfo(code: 'en-GB', nameZh: '英語 (英國)', nameNative: 'English (UK)', flag: '🇬🇧'),
    LanguageInfo(code: 'ja-JP', nameZh: '日語', nameNative: '日本語', flag: '🇯🇵'),
    LanguageInfo(code: 'ko-KR', nameZh: '韓語', nameNative: '한국어', flag: '🇰🇷'),
    LanguageInfo(code: 'es-ES', nameZh: '西班牙語', nameNative: 'Español', flag: '🇪🇸'),
    LanguageInfo(code: 'fr-FR', nameZh: '法語', nameNative: 'Français', flag: '🇫🇷'),
    LanguageInfo(code: 'de-DE', nameZh: '德語', nameNative: 'Deutsch', flag: '🇩🇪'),
    LanguageInfo(code: 'it-IT', nameZh: '義大利語', nameNative: 'Italiano', flag: '🇮🇹'),
    LanguageInfo(code: 'pt-BR', nameZh: '葡萄牙語 (巴西)', nameNative: 'Português (Brasil)', flag: '🇧🇷'),
    LanguageInfo(code: 'ru-RU', nameZh: '俄語', nameNative: 'Русский', flag: '🇷🇺'),
    LanguageInfo(code: 'vi-VN', nameZh: '越南語', nameNative: 'Tiếng Việt', flag: '🇻🇳'),
    LanguageInfo(code: 'th-TH', nameZh: '泰語', nameNative: 'ไทย', flag: '🇹🇭'),
    LanguageInfo(code: 'id-ID', nameZh: '印尼語', nameNative: 'Bahasa Indonesia', flag: '🇮🇩'),
    LanguageInfo(code: 'ms-MY', nameZh: '馬來語', nameNative: 'Bahasa Melayu', flag: '🇲🇾'),
    LanguageInfo(code: 'hi-IN', nameZh: '印地語', nameNative: 'हिन्दी', flag: '🇮🇳'),
    LanguageInfo(code: 'ar-SA', nameZh: '阿拉伯語', nameNative: 'العربية', flag: '🇸🇦'),
    LanguageInfo(code: 'tr-TR', nameZh: '土耳其語', nameNative: 'Türkçe', flag: '🇹🇷'),
    LanguageInfo(code: 'nl-NL', nameZh: '荷蘭語', nameNative: 'Nederlands', flag: '🇳🇱'),
    LanguageInfo(code: 'pl-PL', nameZh: '波蘭語', nameNative: 'Polski', flag: '🇵🇱'),
    LanguageInfo(code: 'sv-SE', nameZh: '瑞典語', nameNative: 'Svenska', flag: '🇸🇪'),
    LanguageInfo(code: 'da-DK', nameZh: '丹麥語', nameNative: 'Dansk', flag: '🇩🇰'),
    LanguageInfo(code: 'fi-FI', nameZh: '芬蘭語', nameNative: 'Suomi', flag: '🇫🇮'),
    LanguageInfo(code: 'no-NO', nameZh: '挪威語', nameNative: 'Norsk', flag: '🇳🇴'),
    LanguageInfo(code: 'cs-CZ', nameZh: '捷克語', nameNative: 'Čeština', flag: '🇨🇿'),
    LanguageInfo(code: 'el-GR', nameZh: '希臘語', nameNative: 'Ελληνικά', flag: '🇬🇷'),
    LanguageInfo(code: 'hu-HU', nameZh: '匈牙利語', nameNative: 'Magyar', flag: '🇭🇺'),
    LanguageInfo(code: 'ro-RO', nameZh: '羅馬尼亞語', nameNative: 'Română', flag: '🇷🇴'),
    LanguageInfo(code: 'uk-UA', nameZh: '烏克蘭語', nameNative: 'Українська', flag: '🇺🇦'),
    LanguageInfo(code: 'he-IL', nameZh: '希伯來語', nameNative: 'עברית', flag: '🇮🇱'),
    LanguageInfo(code: 'fil-PH', nameZh: '菲律賓語', nameNative: 'Filipino', flag: '🇵🇭'),
  ];

  static LanguageInfo findByCode(String code) {
    return list.firstWhere(
      (element) => element.code.toLowerCase() == code.toLowerCase() ||
                   element.code.split('-').first.toLowerCase() == code.split('-').first.toLowerCase(),
      orElse: () => defaultLanguage,
    );
  }
}
