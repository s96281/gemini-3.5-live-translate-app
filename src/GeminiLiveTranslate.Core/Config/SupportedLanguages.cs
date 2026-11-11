using System;
using System.Collections.Generic;
using System.Linq;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Core.Config;

/// <summary>
/// Directory of 70+ supported languages with BCP-47 codes, names, and flags.
/// </summary>
public static class SupportedLanguages
{
    public const string DefaultLanguageCode = "zh-TW";

    private static readonly List<LanguageInfo> AllLanguages = new()
    {
        new LanguageInfo("zh-TW", "繁體中文 (台灣)", "繁體中文", "🇹🇼"),
        new LanguageInfo("zh-CN", "簡體中文 (中國)", "简体中文", "🇨🇳"),
        new LanguageInfo("zh-HK", "繁體中文 (香港)", "繁體中文", "🇭🇰"),
        new LanguageInfo("en-US", "英文 (美國)", "English (US)", "🇺🇸"),
        new LanguageInfo("en-GB", "英文 (英國)", "English (UK)", "🇬🇧"),
        new LanguageInfo("ja-JP", "日文 (日本)", "日本語", "🇯🇵"),
        new LanguageInfo("ko-KR", "韓文 (韓國)", "한국어", "🇰🇷"),
        new LanguageInfo("es-ES", "西班牙文 (西班牙)", "Español (España)", "🇪🇸"),
        new LanguageInfo("es-MX", "西班牙文 (墨西哥)", "Español (México)", "🇲🇽"),
        new LanguageInfo("fr-FR", "法文 (法國)", "Français", "🇫🇷"),
        new LanguageInfo("fr-CA", "法文 (加拿大)", "Français (Canada)", "🇨🇦"),
        new LanguageInfo("de-DE", "德文 (德國)", "Deutsch", "🇩🇪"),
        new LanguageInfo("it-IT", "義大利文 (義大利)", "Italiano", "🇮🇹"),
        new LanguageInfo("pt-BR", "葡萄牙文 (巴西)", "Português (Brasil)", "🇧🇷"),
        new LanguageInfo("pt-PT", "葡萄牙文 (葡萄牙)", "Português (Portugal)", "🇵🇹"),
        new LanguageInfo("ru-RU", "俄文 (俄羅斯)", "Русский", "🇷🇺"),
        new LanguageInfo("ar-SA", "阿拉伯文 (沙烏地阿拉伯)", "العربية", "🇸🇦"),
        new LanguageInfo("ar-EG", "阿拉伯文 (埃及)", "العربية (مصر)", "🇪🇬"),
        new LanguageInfo("hi-IN", "印地文 (印度)", "हिन्दी", "🇮🇳"),
        new LanguageInfo("bn-BD", "孟加拉文 (孟加拉)", "বাংলা", "🇧🇩"),
        new LanguageInfo("pa-IN", "旁遮普文 (印度)", "ਪੰਜਾਬੀ", "🇮🇳"),
        new LanguageInfo("jv-ID", "爪哇文 (印尼)", "Basa Jawa", "🇮🇩"),
        new LanguageInfo("vi-VN", "越南文 (越南)", "Tiếng Việt", "🇻🇳"),
        new LanguageInfo("th-TH", "泰文 (泰國)", "ไทย", "🇹🇭"),
        new LanguageInfo("id-ID", "印尼文 (印尼)", "Bahasa Indonesia", "🇮🇩"),
        new LanguageInfo("ms-MY", "馬來文 (馬來西亞)", "Bahasa Melayu", "🇲🇾"),
        new LanguageInfo("tl-PH", "菲律賓文 (菲律賓)", "Filipino", "🇵🇭"),
        new LanguageInfo("tr-TR", "土耳其文 (土耳其)", "Türkçe", "🇹🇷"),
        new LanguageInfo("pl-PL", "波蘭文 (波蘭)", "Polski", "🇵🇱"),
        new LanguageInfo("uk-UA", "烏克蘭文 (烏克蘭)", "Українська", "🇺🇦"),
        new LanguageInfo("nl-NL", "荷蘭文 (荷蘭)", "Nederlands", "🇳🇱"),
        new LanguageInfo("el-GR", "希臘文 (希臘)", "Ελληνικά", "🇬🇷"),
        new LanguageInfo("cs-CZ", "捷克文 (捷克)", "Čeština", "🇨🇿"),
        new LanguageInfo("sv-SE", "瑞典文 (瑞典)", "Svenska", "🇸🇪"),
        new LanguageInfo("hu-HU", "匈牙利文 (匈牙利)", "Magyar", "🇭🇺"),
        new LanguageInfo("ro-RO", "羅馬尼亞文 (羅馬尼亞)", "Română", "🇷🇴"),
        new LanguageInfo("da-DK", "丹麥文 (丹麥)", "Dansk", "🇩🇰"),
        new LanguageInfo("fi-FI", "芬蘭文 (芬蘭)", "Suomi", "🇫🇮"),
        new LanguageInfo("no-NO", "挪威文 (挪威)", "Norsk", "🇳🇴"),
        new LanguageInfo("he-IL", "希伯來文 (以色列)", "עברית", "🇮🇱"),
        new LanguageInfo("ur-PK", "烏爾都文 (巴基斯坦)", "اردو", "🇵🇰"),
        new LanguageInfo("fa-IR", "波斯文 (伊朗)", "فارسی", "🇮🇷"),
        new LanguageInfo("ta-IN", "坦米爾文 (印度)", "தமிழ்", "🇮🇳"),
        new LanguageInfo("te-IN", "泰盧固文 (印度)", "తెలుగు", "🇮🇳"),
        new LanguageInfo("mr-IN", "馬拉地文 (印度)", "मराठी", "🇮🇳"),
        new LanguageInfo("gu-IN", "古吉拉特文 (印度)", "ગુજરાતી", "🇮🇳"),
        new LanguageInfo("kn-IN", "卡納達文 (印度)", "ಕನ್ನಡ", "🇮🇳"),
        new LanguageInfo("ml-IN", "馬拉雅拉姆文 (印度)", "മലയാളം", "🇮🇳"),
        new LanguageInfo("my-MM", "緬甸文 (緬甸)", "မြန်မာ", "🇲🇲"),
        new LanguageInfo("km-KH", "高棉文 (柬埔寨)", "ភាសាខ្មែរ", "🇰🇭"),
        new LanguageInfo("lo-LA", "寮文 (寮國)", "ພາສາລາວ", "🇱🇦"),
        new LanguageInfo("ne-NP", "尼泊爾文 (尼泊爾)", "नेपाली", "🇳🇵"),
        new LanguageInfo("si-LK", "僧伽羅文 (斯里蘭卡)", "සිංහල", "🇱🇰"),
        new LanguageInfo("am-ET", "阿姆哈拉文 (衣索比亞)", "አማርኛ", "🇪🇹"),
        new LanguageInfo("sw-KE", "史瓦希利文 (肯亞)", "Kiswahili", "🇰🇪"),
        new LanguageInfo("sk-SK", "斯洛伐克文 (斯洛伐克)", "Slovenčina", "🇸🇰"),
        new LanguageInfo("bg-BG", "保加利亞文 (保加利亞)", "Български", "🇧🇬"),
        new LanguageInfo("sr-RS", "塞爾維亞文 (塞爾維亞)", "Српски", "🇷🇸"),
        new LanguageInfo("hr-HR", "克羅埃西亞文 (克羅埃西亞)", "Hrvatski", "🇭🇷"),
        new LanguageInfo("sl-SI", "斯洛維尼亞文 (斯洛維尼亞)", "Slovenščina", "🇸🇮"),
        new LanguageInfo("lt-LT", "立陶宛文 (立陶宛)", "Lietuvių", "🇱🇹"),
        new LanguageInfo("lv-LV", "拉脫維亞文 (拉脫維亞)", "Latviešu", "🇱🇻"),
        new LanguageInfo("et-EE", "愛沙尼亞文 (愛沙尼亞)", "Eesti", "🇪🇪"),
        new LanguageInfo("is-IS", "冰島文 (冰島)", "Íslenska", "🇮🇸"),
        new LanguageInfo("ga-IE", "愛爾蘭文 (愛爾蘭)", "Gaeilge", "🇮🇪"),
        new LanguageInfo("af-ZA", "南非荷蘭文 (南非)", "Afrikaans", "🇿🇦"),
        new LanguageInfo("zu-ZA", "祖魯文 (南非)", "isiZulu", "🇿🇦"),
        new LanguageInfo("az-AZ", "亞塞拜然文 (亞塞拜然)", "Azərbaycan", "🇦🇿"),
        new LanguageInfo("ka-GE", "喬治亞文 (喬治亞)", "ქართული", "🇬🇪"),
        new LanguageInfo("hy-AM", "亞美尼亞文 (亞美尼亞)", "Հայերեն", "🇦🇲"),
        new LanguageInfo("mn-MN", "蒙古文 (蒙古)", "Монгол", "🇲🇳"),
        new LanguageInfo("kk-KZ", "哈薩克文 (哈薩克)", "Қазақ", "🇰🇿"),
        new LanguageInfo("uz-UZ", "烏茲別克文 (烏茲別克)", "Oʻzbek", "🇺🇿"),
        new LanguageInfo("ca-ES", "加泰隆尼亞文 (西班牙)", "Català", "🇪🇸"),
        new LanguageInfo("eu-ES", "巴斯克文 (西班牙)", "Euskara", "🇪🇸")
    };

    private static readonly Dictionary<string, LanguageInfo> ByCodeMap =
        AllLanguages.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all supported languages.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> GetAll() => AllLanguages;

    /// <summary>
    /// Gets the default language (zh-TW).
    /// </summary>
    public static LanguageInfo GetDefault() => ByCodeMap[DefaultLanguageCode];

    /// <summary>
    /// Finds a language by its BCP-47 code (case-insensitive).
    /// </summary>
    public static LanguageInfo? GetByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return ByCodeMap.TryGetValue(code.Trim(), out var lang) ? lang : null;
    }

    /// <summary>
    /// Finds a language by code or returns default (zh-TW) if not found.
    /// </summary>
    public static LanguageInfo GetByCodeOrDefault(string? code)
    {
        return GetByCode(code) ?? GetDefault();
    }

    /// <summary>
    /// Searches languages by query string matching code, display name, or native name.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AllLanguages;

        string q = query.Trim();
        return AllLanguages
            .Where(l => l.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        l.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        l.NativeName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
