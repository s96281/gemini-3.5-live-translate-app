using System;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Represents a bilingual turn in the translation history.
/// </summary>
public class TranslationTurn
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string SourceText { get; set; } = string.Empty;
    public string TargetText { get; set; } = string.Empty;
    public bool IsFinal { get; set; } = false;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = "zh-TW";

    public TranslationTurn()
    {
    }

    public TranslationTurn(string sourceText, string targetText, bool isFinal = true, string sourceLang = "auto", string targetLang = "zh-TW")
    {
        Id = Guid.NewGuid().ToString();
        Timestamp = DateTimeOffset.UtcNow;
        SourceText = sourceText ?? string.Empty;
        TargetText = targetText ?? string.Empty;
        IsFinal = isFinal;
        SourceLanguage = sourceLang ?? string.Empty;
        TargetLanguage = targetLang ?? "zh-TW";
    }
}
