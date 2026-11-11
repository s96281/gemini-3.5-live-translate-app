using System;
using System.Linq;
using GeminiLiveTranslate.Core.Config;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class SupportedLanguagesTests
{
    [Fact]
    public void GetAll_ContainsOver70Languages()
    {
        var languages = SupportedLanguages.GetAll();
        Assert.NotNull(languages);
        Assert.True(languages.Count >= 70, $"Expected >= 70 languages, but found {languages.Count}");
    }

    [Fact]
    public void GetDefault_ReturnsTraditionalChinese()
    {
        var defaultLang = SupportedLanguages.GetDefault();
        Assert.NotNull(defaultLang);
        Assert.Equal("zh-TW", defaultLang.Code);
        Assert.Contains("繁體中文", defaultLang.DisplayName);
        Assert.Equal("🇹🇼", defaultLang.FlagEmoji);
    }

    [Theory]
    [InlineData("zh-TW", "繁體中文")]
    [InlineData("en-US", "英文")]
    [InlineData("ja-JP", "日文")]
    [InlineData("ko-KR", "韓文")]
    [InlineData("de-DE", "德文")]
    [InlineData("fr-FR", "法文")]
    [InlineData("es-ES", "西班牙文")]
    public void GetByCode_ReturnsCorrectLanguage(string code, string expectedNameSub)
    {
        var lang = SupportedLanguages.GetByCode(code);
        Assert.NotNull(lang);
        Assert.Equal(code, lang.Code);
        Assert.Contains(expectedNameSub, lang.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(lang.FlagEmoji));
        Assert.False(string.IsNullOrWhiteSpace(lang.NativeName));
    }

    [Fact]
    public void GetByCode_IsCaseInsensitive()
    {
        var langLower = SupportedLanguages.GetByCode("zh-tw");
        var langUpper = SupportedLanguages.GetByCode("ZH-TW");
        var langMixed = SupportedLanguages.GetByCode("Zh-Tw");

        Assert.NotNull(langLower);
        Assert.NotNull(langUpper);
        Assert.NotNull(langMixed);
        Assert.Equal("zh-TW", langLower.Code);
        Assert.Equal(langLower.Code, langUpper.Code);
        Assert.Equal(langLower.Code, langMixed.Code);
    }

    [Fact]
    public void GetByCode_UnknownCode_ReturnsNull()
    {
        Assert.Null(SupportedLanguages.GetByCode("xx-YY"));
        Assert.Null(SupportedLanguages.GetByCode(null));
        Assert.Null(SupportedLanguages.GetByCode(""));
    }

    [Fact]
    public void GetByCodeOrDefault_UnknownCode_ReturnsDefaultZhTw()
    {
        var lang = SupportedLanguages.GetByCodeOrDefault("unknown-code");
        Assert.NotNull(lang);
        Assert.Equal("zh-TW", lang.Code);
    }

    [Fact]
    public void Search_MatchesDisplayNameCodeAndNativeName()
    {
        var resultsJa = SupportedLanguages.Search("日本語");
        Assert.NotEmpty(resultsJa);
        Assert.Contains(resultsJa, l => l.Code == "ja-JP");

        var resultsFr = SupportedLanguages.Search("fr-");
        Assert.True(resultsFr.Count >= 2); // fr-FR, fr-CA

        var resultsZh = SupportedLanguages.Search("繁體中文");
        Assert.True(resultsZh.Count >= 2); // zh-TW, zh-HK
    }

    [Fact]
    public void Search_EmptyOrNullQuery_ReturnsAllLanguages()
    {
        var all = SupportedLanguages.GetAll();
        var searchEmpty = SupportedLanguages.Search("");
        var searchNull = SupportedLanguages.Search(null);

        Assert.Equal(all.Count, searchEmpty.Count);
        Assert.Equal(all.Count, searchNull.Count);
    }
}
