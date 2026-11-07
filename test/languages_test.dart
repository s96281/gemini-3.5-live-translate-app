import 'package:flutter_test/flutter_test.dart';
import 'package:gemini_live_translate/config/languages.dart';

void main() {
  group('SupportedLanguages Tests', () {
    test('defaultLanguage is Traditional Chinese zh-TW', () {
      expect(SupportedLanguages.defaultLanguage.code, equals('zh-TW'));
      expect(SupportedLanguages.defaultLanguage.flag, equals('🇹🇼'));
    });

    test('findByCode matches exact BCP-47 code', () {
      final ja = SupportedLanguages.findByCode('ja-JP');
      expect(ja.code, equals('ja-JP'));
      expect(ja.nameZh, equals('日語'));
      expect(ja.flag, equals('🇯🇵'));
    });

    test('findByCode matches prefix code when regional suffix differs', () {
      final en = SupportedLanguages.findByCode('en');
      expect(en.code.startsWith('en'), isTrue);
    });

    test('findByCode returns default language on unknown code', () {
      final unknown = SupportedLanguages.findByCode('xyz-999');
      expect(unknown.code, equals('zh-TW'));
    });

    test('supported list has 30+ major languages', () {
      expect(SupportedLanguages.list.length, greaterThanOrEqualTo(30));
    });
  });
}
