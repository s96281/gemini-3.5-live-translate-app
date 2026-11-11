using System;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Persistent application settings model.
/// </summary>
public class AppSettings
{
    public string? EncryptedApiKey { get; set; }
    public string Model { get; set; } = LiveConfig.DefaultModel;
    public string Voice { get; set; } = LiveConfig.DefaultVoice;
    public string TargetLanguageCode { get; set; } = LiveConfig.DefaultTargetLanguageCode;
    public bool AutoPlayAudio { get; set; } = false;
    public AudioSourceType AudioSource { get; set; } = AudioSourceType.SystemAudioLoopback;
    public int MaxParagraphLength { get; set; } = LiveConfig.DefaultMaxParagraphLength;
    [System.Text.Json.Serialization.JsonIgnore]
    public int MaxParagraphCharacters { get => MaxParagraphLength; set => MaxParagraphLength = value; }

    // Overlay geometry and visual settings
    public double OverlayLeft { get; set; } = 100.0;
    public double OverlayTop { get; set; } = 100.0;
    public double OverlayWidth { get; set; } = 640.0;
    public double OverlayHeight { get; set; } = 140.0;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayFontSize { get; set; } = 24.0;
    public bool OverlayClickThrough { get; set; } = false;
    public string Theme { get; set; } = "Dark";
}
