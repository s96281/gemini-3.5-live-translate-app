using System;
using System.IO;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public SettingsTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"gemini_settings_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }

    [Fact]
    public void LoadSettings_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = new SettingsService(_tempSettingsPath);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(LiveConfig.DefaultModel, settings.Model);
        Assert.Equal(LiveConfig.DefaultVoice, settings.Voice);
        Assert.Equal(LiveConfig.DefaultTargetLanguageCode, settings.TargetLanguageCode);
        Assert.Equal(50, settings.MaxParagraphLength);
        Assert.Equal(AudioSourceType.SystemAudioLoopback, settings.AudioSource);
        Assert.Equal(640.0, settings.OverlayWidth);
        Assert.Equal(140.0, settings.OverlayHeight);
    }

    [Fact]
    public void SaveAndLoadSettings_PreservesAllPropertiesAccurately()
    {
        var service = new SettingsService(_tempSettingsPath);
        var settings = new AppSettings
        {
            Model = "models/gemini-2.5-flash",
            Voice = "Kore",
            TargetLanguageCode = "ja-JP",
            AutoPlayAudio = true,
            AudioSource = AudioSourceType.Microphone,
            MaxParagraphLength = 75,
            OverlayLeft = 200.0,
            OverlayTop = 300.0,
            OverlayWidth = 800.0,
            OverlayHeight = 180.0,
            OverlayOpacity = 0.5,
            OverlayFontSize = 32.0,
            OverlayClickThrough = true,
            Theme = "Light"
        };

        service.SaveSettings(settings);

        var loaded = service.LoadSettings();
        Assert.Equal("models/gemini-2.5-flash", loaded.Model);
        Assert.Equal("Kore", loaded.Voice);
        Assert.Equal("ja-JP", loaded.TargetLanguageCode);
        Assert.True(loaded.AutoPlayAudio);
        Assert.Equal(AudioSourceType.Microphone, loaded.AudioSource);
        Assert.Equal(75, loaded.MaxParagraphLength);
        Assert.Equal(200.0, loaded.OverlayLeft);
        Assert.Equal(300.0, loaded.OverlayTop);
        Assert.Equal(800.0, loaded.OverlayWidth);
        Assert.Equal(180.0, loaded.OverlayHeight);
        Assert.Equal(0.5, loaded.OverlayOpacity);
        Assert.Equal(32.0, loaded.OverlayFontSize);
        Assert.True(loaded.OverlayClickThrough);
        Assert.Equal("Light", loaded.Theme);
    }

    [Fact]
    public void SaveApiKey_And_GetApiKey_EncryptsAndDecryptsRoundtrip()
    {
        var service = new SettingsService(_tempSettingsPath);
        string originalKey = "AIzaSySecretTestKey_1234567890abcdef";

        service.SaveApiKey(originalKey);

        // Verify stored settings contains cipher/encoded text, not raw plaintext
        var rawSettings = service.LoadSettings();
        Assert.NotNull(rawSettings.EncryptedApiKey);
        Assert.NotEqual(originalKey, rawSettings.EncryptedApiKey);

        // Verify decrypted retrieval
        string retrieved = service.GetApiKey();
        Assert.Equal(originalKey, retrieved);
    }

    [Fact]
    public void GetApiKey_FallsBackToEnvironmentVariablesWhenStoredKeyIsEmpty()
    {
        var service = new SettingsService(_tempSettingsPath);
        string envBackup = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;

        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "env_test_gemini_api_key");
            string retrieved = service.GetApiKey();
            Assert.Equal("env_test_gemini_api_key", retrieved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", string.IsNullOrEmpty(envBackup) ? null : envBackup);
        }
    }

    [Fact]
    public void DecryptSecret_CorruptedData_ReturnsEmptyStringWithoutThrowing()
    {
        var service = new SettingsService(_tempSettingsPath);
        string result = service.DecryptSecret("totally_invalid_not_base64_or_anything!@#$%^");
        // Must not throw exception
        Assert.NotNull(result);
    }

    [Fact]
    public void LoadSettings_CorruptedJsonFile_GracefullyFallsBackToDefaults()
    {
        File.WriteAllText(_tempSettingsPath, "{ this is corrupted invalid json }}}");
        var service = new SettingsService(_tempSettingsPath);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(LiveConfig.DefaultModel, settings.Model);
    }

    [Fact]
    public void GetApiKey_StoredKeyTakesPrecedenceOverEnvironmentVariable()
    {
        var service = new SettingsService(_tempSettingsPath);
        service.SaveApiKey("stored_priority_api_key");

        string envBackup = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "env_secondary_api_key");
            string apiKey = service.GetApiKey();
            Assert.Equal("stored_priority_api_key", apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", string.IsNullOrEmpty(envBackup) ? null : envBackup);
        }
    }

    [Fact]
    public void SaveApiKey_NullOrEmpty_ClearsEncryptedKey()
    {
        var service = new SettingsService(_tempSettingsPath);
        service.SaveApiKey("initial_key");
        Assert.False(string.IsNullOrEmpty(service.GetApiKey()));

        service.SaveApiKey("");
        Assert.Empty(service.GetApiKey());
    }
}
