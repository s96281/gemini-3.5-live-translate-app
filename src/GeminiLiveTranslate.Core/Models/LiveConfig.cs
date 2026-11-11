using System;
using System.Collections.Generic;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Runtime streaming configuration for Gemini Live session.
/// </summary>
public class LiveConfig
{
    public const string DefaultModel = "models/gemini-2.0-flash-exp";
    public const string DefaultVoice = "Puck";
    public const string DefaultTargetLanguageCode = "zh-TW";
    public const int DefaultMaxParagraphLength = 50;
    public static readonly int[] SupportedParagraphLengths = { 35, 50, 75, 100 };

    public static readonly IReadOnlyList<string> AvailableModels = new[]
    {
        "models/gemini-2.0-flash-exp",
        "models/gemini-2.0-flash-realtime-exp",
        "models/gemini-2.5-flash",
        "models/gemini-3.5-live-translate-preview"
    };

    public static readonly IReadOnlyList<string> AvailableVoices = new[]
    {
        "Puck",
        "Charon",
        "Kore",
        "Fenrir",
        "Aoede"
    };

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public string Voice { get; set; } = DefaultVoice;
    public string TargetLanguageCode { get; set; } = DefaultTargetLanguageCode;
    public bool AutoPlayAudio { get; set; } = false;
    public AudioSourceType AudioSourceMode { get; set; } = AudioSourceType.SystemAudioLoopback;
    public int MaxParagraphLength { get; set; } = DefaultMaxParagraphLength;

    public LiveConfig()
    {
    }

    public LiveConfig(string apiKey, string? model = null, string? voice = null, string? targetLang = null)
    {
        ApiKey = apiKey ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model)) Model = model;
        if (!string.IsNullOrWhiteSpace(voice)) Voice = voice;
        if (!string.IsNullOrWhiteSpace(targetLang)) TargetLanguageCode = targetLang;
    }
}
