using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Service managing persistent application settings and secure credential storage.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads application settings from disk or returns defaults if file does not exist.
    /// </summary>
    AppSettings LoadSettings();

    /// <summary>
    /// Saves application settings to disk.
    /// </summary>
    void SaveSettings(AppSettings settings);

    /// <summary>
    /// Retrieves decrypted API key, falling back to environment variables (GEMINI_API_KEY, GOOGLE_API_KEY).
    /// </summary>
    string GetApiKey();

    /// <summary>
    /// Encrypts and saves API key to application settings.
    /// </summary>
    void SaveApiKey(string apiKey);

    /// <summary>
    /// Encrypts a plaintext secret string using DPAPI on Windows or secure fallback on non-Windows.
    /// </summary>
    string EncryptSecret(string plainText);

    /// <summary>
    /// Decrypts an encrypted secret string using DPAPI on Windows or secure fallback on non-Windows.
    /// </summary>
    string DecryptSecret(string? cipherText);
}
