using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Production settings and credential storage service supporting DPAPI and environment variable fallbacks.
/// </summary>
public class SettingsService : ISettingsService
{
    private const string NonWindowsPrefix = "b64:";
    private readonly string _settingsFilePath;
    private readonly object _fileLock = new();

    public SettingsService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData) || !OperatingSystem.IsWindows())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                appData = Path.Combine(home, ".config");
            }
            string dir = Path.Combine(appData, "GeminiLiveTranslate");
            _settingsFilePath = Path.Combine(dir, "settings.json");
        }
    }

    /// <inheritdoc/>
    public AppSettings LoadSettings()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, GeminiJsonContext.DefaultOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsService] Warning: Failed to load settings from {_settingsFilePath}: {ex.Message}. Using defaults.");
            }

            return new AppSettings();
        }
    }

    /// <inheritdoc/>
    public void SaveSettings(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        lock (_fileLock)
        {
            try
            {
                string? directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions(GeminiJsonContext.DefaultOptions)
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsService] Error: Failed to save settings to {_settingsFilePath}: {ex.Message}");
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public string GetApiKey()
    {
        var settings = LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.EncryptedApiKey))
        {
            string decrypted = DecryptSecret(settings.EncryptedApiKey);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                return decrypted.Trim();
            }
        }

        // Check environment variable fallback
        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey.Trim();
        }

        envKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey.Trim();
        }

        return string.Empty;
    }

    /// <inheritdoc/>
    public void SaveApiKey(string apiKey)
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            settings.EncryptedApiKey = null;
        }
        else
        {
            settings.EncryptedApiKey = EncryptSecret(apiKey.Trim());
        }
        SaveSettings(settings);
    }

    /// <inheritdoc/>
    public string EncryptSecret(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                byte[] cipherBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception)
            {
                // Fallback if DPAPI is unavailable
            }
        }

        // Non-Windows or DPAPI fallback
        return NonWindowsPrefix + Convert.ToBase64String(plainBytes);
    }

    /// <inheritdoc/>
    public string DecryptSecret(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return string.Empty;
        }

        string trimmed = cipherText.Trim();

        // Check for non-Windows base64 fallback marker
        if (trimmed.StartsWith(NonWindowsPrefix, StringComparison.Ordinal))
        {
            try
            {
                string b64 = trimmed.Substring(NonWindowsPrefix.Length);
                byte[] bytes = Convert.FromBase64String(b64);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(trimmed);
                byte[] plainBytes = ProtectedData.Unprotect(
                    cipherBytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // In case it was stored as plaintext or fallback base64
            }
        }

        // Try standard base64 decoding
        try
        {
            byte[] bytes = Convert.FromBase64String(trimmed);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Decryption failed on corrupted ciphertext; never leak invalid data
            return string.Empty;
        }
    }

    /// <summary>
    /// Masks an API key for safe display in UI (e.g. AIza••••4321).
    /// </summary>
    public static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        string trimmed = key.Trim();
        if (trimmed.Length <= 8) return new string('•', trimmed.Length);
        return trimmed.Substring(0, 4) + new string('•', trimmed.Length - 8) + trimmed.Substring(trimmed.Length - 4);
    }

    /// <summary>
    /// Calculates exponential backoff delay for connection retries.
    /// </summary>
    public static int CalculateBackoffDelay(int retryAttempt, int initialDelayMs = 1000) =>
        initialDelayMs * (int)Math.Pow(2, Math.Max(0, retryAttempt - 1));

    /// <summary>
    /// Sanitizes paragraph character threshold to a valid positive value.
    /// </summary>
    public static int SanitizeParagraphLength(int length) =>
        length > 0 ? length : LiveConfig.DefaultMaxParagraphLength;
}
