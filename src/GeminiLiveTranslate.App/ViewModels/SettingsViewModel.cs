using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;

namespace GeminiLiveTranslate.App.ViewModels;

/// <summary>
/// ViewModel for the Fluent 2 Application Settings Dialog (SettingsDialog).
/// Operates on an isolated draft copy so Cancel() never mutates active settings or disk state.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<string> ThemeOptions = new[] { "Dark", "Light" };

    private readonly ISettingsService _settingsService;
    private readonly AppSettings _originalSnapshot;

    private string _apiKey;
    private bool _isApiKeyRevealed;
    private string _selectedModel;
    private string _selectedVoice;
    private int _selectedParagraphLength;
    private LanguageInfo _selectedLanguage;
    private bool _autoPlayAudio;
    private AudioSourceType _selectedAudioSource;
    private string _selectedTheme;
    private bool _isSaved;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler? SettingsCancelled;

    public IReadOnlyList<string> AvailableModels => LiveConfig.AvailableModels;
    public IReadOnlyList<string> AvailableVoices => LiveConfig.AvailableVoices;
    public IReadOnlyList<int> SupportedParagraphLengths => LiveConfig.SupportedParagraphLengths;
    public IReadOnlyList<LanguageInfo> AvailableLanguages => SupportedLanguages.GetAll();
    public IReadOnlyList<string> AvailableThemes => ThemeOptions;

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_apiKey, normalized, StringComparison.Ordinal))
            {
                _apiKey = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedApiKey));
                OnPropertyChanged(nameof(HasApiKey));
            }
        }
    }

    public string MaskedApiKey => SettingsService.MaskApiKey(_apiKey);
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public bool IsApiKeyRevealed
    {
        get => _isApiKeyRevealed;
        set
        {
            if (_isApiKeyRevealed != value)
            {
                _isApiKeyRevealed = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            string sanitized = SanitizeModel(value);
            if (!string.Equals(_selectedModel, sanitized, StringComparison.Ordinal))
            {
                _selectedModel = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            string sanitized = SanitizeVoice(value);
            if (!string.Equals(_selectedVoice, sanitized, StringComparison.Ordinal))
            {
                _selectedVoice = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public int SelectedParagraphLength
    {
        get => _selectedParagraphLength;
        set
        {
            int sanitized = SettingsService.SanitizeParagraphLength(value);
            if (_selectedParagraphLength != sanitized)
            {
                _selectedParagraphLength = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public LanguageInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            var resolved = value ?? SupportedLanguages.GetDefault();
            if (_selectedLanguage != resolved)
            {
                _selectedLanguage = resolved;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetLanguageCode));
            }
        }
    }

    public string TargetLanguageCode
    {
        get => _selectedLanguage.Code;
        set
        {
            var resolved = SupportedLanguages.GetByCodeOrDefault(value);
            SelectedLanguage = resolved;
        }
    }

    public bool AutoPlayAudio
    {
        get => _autoPlayAudio;
        set
        {
            if (_autoPlayAudio != value)
            {
                _autoPlayAudio = value;
                OnPropertyChanged();
            }
        }
    }

    public AudioSourceType SelectedAudioSource
    {
        get => _selectedAudioSource;
        set
        {
            if (_selectedAudioSource != value)
            {
                _selectedAudioSource = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            string sanitized = SanitizeTheme(value);
            if (!string.Equals(_selectedTheme, sanitized, StringComparison.Ordinal))
            {
                _selectedTheme = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSaved
    {
        get => _isSaved;
        private set
        {
            if (_isSaved != value)
            {
                _isSaved = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand ToggleRevealApiKeyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public SettingsViewModel(ISettingsService? settingsService = null, AppSettings? activeSettings = null)
    {
        _settingsService = settingsService ?? new SettingsService();

        var loaded = activeSettings != null ? CloneSettings(activeSettings) : _settingsService.LoadSettings();
        _originalSnapshot = CloneSettings(loaded);

        string loadedKey = _settingsService.GetApiKey();
        if (string.IsNullOrWhiteSpace(loadedKey) && !string.IsNullOrWhiteSpace(loaded.EncryptedApiKey))
        {
            loadedKey = _settingsService.DecryptSecret(loaded.EncryptedApiKey);
        }

        _apiKey = loadedKey ?? string.Empty;
        _selectedModel = SanitizeModel(loaded.Model);
        _selectedVoice = SanitizeVoice(loaded.Voice);
        _selectedParagraphLength = SettingsService.SanitizeParagraphLength(loaded.MaxParagraphLength);
        _selectedLanguage = SupportedLanguages.GetByCodeOrDefault(loaded.TargetLanguageCode);
        _autoPlayAudio = loaded.AutoPlayAudio;
        _selectedAudioSource = loaded.AudioSource;
        _selectedTheme = SanitizeTheme(loaded.Theme);

        ToggleRevealApiKeyCommand = new RelayCommand(ToggleRevealApiKey);
        SaveCommand = new RelayCommand(() => Save());
        CancelCommand = new RelayCommand(Cancel);
    }

    public void ToggleRevealApiKey()
    {
        IsApiKeyRevealed = !IsApiKeyRevealed;
    }

    /// <summary>
    /// Commits draft settings to persistent storage via ISettingsService,
    /// encrypts the API key via DPAPI, and returns the newly saved AppSettings snapshot.
    /// </summary>
    public AppSettings Save()
    {
        string trimmedKey = (_apiKey ?? string.Empty).Trim();
        string? encryptedKey = string.IsNullOrWhiteSpace(trimmedKey)
            ? null
            : _settingsService.EncryptSecret(trimmedKey);

        var committed = new AppSettings
        {
            EncryptedApiKey = encryptedKey,
            Model = _selectedModel,
            Voice = _selectedVoice,
            TargetLanguageCode = _selectedLanguage.Code,
            AutoPlayAudio = _autoPlayAudio,
            AudioSource = _selectedAudioSource,
            MaxParagraphLength = _selectedParagraphLength,
            Theme = _selectedTheme,
            OverlayLeft = _originalSnapshot.OverlayLeft,
            OverlayTop = _originalSnapshot.OverlayTop,
            OverlayWidth = _originalSnapshot.OverlayWidth,
            OverlayHeight = _originalSnapshot.OverlayHeight,
            OverlayOpacity = _originalSnapshot.OverlayOpacity,
            OverlayFontSize = _originalSnapshot.OverlayFontSize,
            OverlayClickThrough = _originalSnapshot.OverlayClickThrough
        };

        _settingsService.SaveApiKey(trimmedKey);
        _settingsService.SaveSettings(committed);

        IsSaved = true;
        SettingsSaved?.Invoke(this, committed);
        return committed;
    }

    /// <summary>
    /// Discards draft modifications and restores state to the original snapshot without touching persisted storage.
    /// </summary>
    public void Cancel()
    {
        _selectedModel = SanitizeModel(_originalSnapshot.Model);
        _selectedVoice = SanitizeVoice(_originalSnapshot.Voice);
        _selectedParagraphLength = SettingsService.SanitizeParagraphLength(_originalSnapshot.MaxParagraphLength);
        _selectedLanguage = SupportedLanguages.GetByCodeOrDefault(_originalSnapshot.TargetLanguageCode);
        _autoPlayAudio = _originalSnapshot.AutoPlayAudio;
        _selectedAudioSource = _originalSnapshot.AudioSource;
        _selectedTheme = SanitizeTheme(_originalSnapshot.Theme);

        IsSaved = false;
        SettingsCancelled?.Invoke(this, EventArgs.Empty);
    }

    public static string SanitizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return LiveConfig.DefaultModel;
        }

        string trimmed = model.Trim();
        var match = LiveConfig.AvailableModels.FirstOrDefault(
            m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));

        return match ?? LiveConfig.DefaultModel;
    }

    public static string SanitizeVoice(string? voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
        {
            return LiveConfig.DefaultVoice;
        }

        string trimmed = voice.Trim();
        var match = LiveConfig.AvailableVoices.FirstOrDefault(
            v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase));

        return match ?? LiveConfig.DefaultVoice;
    }

    public static string SanitizeTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return "Dark";
        }

        return string.Equals(theme.Trim(), "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
    }

    private static AppSettings CloneSettings(AppSettings src)
    {
        return new AppSettings
        {
            EncryptedApiKey = src.EncryptedApiKey,
            Model = src.Model,
            Voice = src.Voice,
            TargetLanguageCode = src.TargetLanguageCode,
            AutoPlayAudio = src.AutoPlayAudio,
            AudioSource = src.AudioSource,
            MaxParagraphLength = src.MaxParagraphLength,
            OverlayLeft = src.OverlayLeft,
            OverlayTop = src.OverlayTop,
            OverlayWidth = src.OverlayWidth,
            OverlayHeight = src.OverlayHeight,
            OverlayOpacity = src.OverlayOpacity,
            OverlayFontSize = src.OverlayFontSize,
            OverlayClickThrough = src.OverlayClickThrough,
            Theme = src.Theme
        };
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
