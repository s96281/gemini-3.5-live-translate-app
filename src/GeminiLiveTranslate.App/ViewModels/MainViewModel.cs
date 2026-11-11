using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GeminiLiveTranslate.App.Audio;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;

namespace GeminiLiveTranslate.App.ViewModels;

/// <summary>
/// Primary MVVM ViewModel for the Windows 11 Fluent Design 2 Main Dashboard Window (MainWindow).
/// Coordinates IGeminiLiveClient, IAudioCaptureEngine, ITextSegmentationService, ISettingsService,
/// IAudioDspService, SpectrumEngine, and OverlayViewModel.
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly IGeminiLiveClient _geminiClient;
    private readonly IAudioCaptureEngine _audioEngine;
    private readonly ITextSegmentationService _segmentationService;
    private readonly ISettingsService _settingsService;
    private readonly IAudioDspService _dspService;
    private readonly Action<Action> _uiDispatcher;
    private readonly bool _ownsGeminiClient;
    private readonly bool _ownsAudioEngine;

    private readonly object _historyLock = new();
    private readonly object _turnLock = new();
    private readonly StringBuilder _turnSourceBuilder = new();
    private readonly StringBuilder _turnTargetBuilder = new();

    // Event deduplication guards for dual-event contracts
    private bool _suppressNextAltSource;
    private bool _suppressNextAltTarget;
    private bool _suppressNextAltAudioOut;
    private bool _suppressNextAltInterrupted;
    private bool _suppressNextAltTurnComplete;
    private bool _suppressNextAltError;
    private bool _suppressNextAltAudioChunk;
    private bool _suppressNextAltSpectrum;
    private bool _suppressNextAltCaptureError;

    private float _currentRmsLevel;
    private LiveConnectionState _connectionState = LiveConnectionState.Disconnected;
    private bool _isSessionActive;
    private string _statusText = "準備就緒 (Ready)";
    private AudioSource _currentAudioSource = AudioSource.SystemAudioLoopback;
    private LanguageInfo _selectedLanguage;
    private string _currentSourceTranscript = string.Empty;
    private string _currentTargetTranscript = string.Empty;
    private string _theme = "Dark";
    private AppSettings _currentSettings;
    private string _lastCopiedText = string.Empty;
    private string? _lastErrorMessage;
    private int _playedAudioChunkCount;
    private int _disposedFlag;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler<TranslationTurn>? TurnAdded;

    public ObservableCollection<TranslationTurn> HistoryCards { get; } = new();
    public ObservableCollection<TranslationTurn> TranslationHistory => HistoryCards;

    public SpectrumEngine Spectrum { get; } = new(SpectrumEngine.DefaultBandCount);
    public IReadOnlyList<float> CurrentLevels => Spectrum.CurrentLevels;

    public float CurrentRmsLevel
    {
        get => _currentRmsLevel;
        private set
        {
            float sanitized = SpectrumEngine.SanitizeLevel(value);
            if (Math.Abs(_currentRmsLevel - sanitized) > 0.0005f)
            {
                _currentRmsLevel = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public LiveConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public bool IsConnected => _geminiClient.IsConnected || _connectionState == LiveConnectionState.Connected;

    public bool IsSessionActive
    {
        get => _isSessionActive;
        private set
        {
            if (_isSessionActive != value)
            {
                _isSessionActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SessionActionButtonText));
            }
        }
    }

    public string SessionActionButtonText => _isSessionActive ? "停止口譯 (Stop)" : "開始即時口譯 (Start)";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_statusText, normalized, StringComparison.Ordinal))
            {
                _statusText = normalized;
                OnPropertyChanged();
            }
        }
    }

    public AudioSource CurrentAudioSource
    {
        get => _currentAudioSource;
        private set
        {
            if (_currentAudioSource != value)
            {
                _currentAudioSource = value;
                _currentSettings.AudioSource = (AudioSourceType)value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSystemAudioLoopback));
                OnPropertyChanged(nameof(IsMicrophoneSource));
                OnPropertyChanged(nameof(ShouldMutePlayback));
                OnPropertyChanged(nameof(AudioSourceStatusText));
            }
        }
    }

    public bool IsSystemAudioLoopback
    {
        get => _currentAudioSource == AudioSource.SystemAudioLoopback;
        set
        {
            if (value && _currentAudioSource != AudioSource.SystemAudioLoopback)
            {
                SwitchAudioSource(AudioSource.SystemAudioLoopback);
            }
        }
    }

    public bool IsMicrophoneSource
    {
        get => _currentAudioSource == AudioSource.Microphone;
        set
        {
            if (value && _currentAudioSource != AudioSource.Microphone)
            {
                SwitchAudioSource(AudioSource.Microphone);
            }
        }
    }

    /// <summary>
    /// Enforces acoustic feedback protection: when capturing SystemAudioLoopback,
    /// speaker TTS playback is automatically muted to prevent infinite loopback oscillation.
    /// </summary>
    public bool ShouldMutePlayback =>
        _currentAudioSource == AudioSource.SystemAudioLoopback || !_currentSettings.AutoPlayAudio;

    public string AudioSourceStatusText => _currentAudioSource == AudioSource.SystemAudioLoopback
        ? "系統音效內錄 (System Loopback · 自動靜音防回授)"
        : "實體麥克風 (Microphone)";

    public int PlayedAudioChunkCount => Interlocked.CompareExchange(ref _playedAudioChunkCount, 0, 0);

    public IReadOnlyList<LanguageInfo> Languages => SupportedLanguages.GetAll();

    public LanguageInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            var resolved = value ?? SupportedLanguages.GetDefault();
            if (_selectedLanguage != resolved)
            {
                _selectedLanguage = resolved;
                _currentSettings.TargetLanguageCode = resolved.Code;
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

    public string CurrentSourceTranscript
    {
        get => _currentSourceTranscript;
        private set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_currentSourceTranscript, normalized, StringComparison.Ordinal))
            {
                _currentSourceTranscript = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentTargetTranscript
    {
        get => _currentTargetTranscript;
        private set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_currentTargetTranscript, normalized, StringComparison.Ordinal))
            {
                _currentTargetTranscript = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            string sanitized = SettingsViewModel.SanitizeTheme(value);
            if (!string.Equals(_theme, sanitized, StringComparison.Ordinal))
            {
                _theme = sanitized;
                _currentSettings.Theme = sanitized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(BackgroundBrushHex));
                OnPropertyChanged(nameof(CardBackgroundBrushHex));
            }
        }
    }

    public bool IsDarkTheme => !string.Equals(_theme, "Light", StringComparison.OrdinalIgnoreCase);

    public string BackgroundBrushHex =>
        string.Equals(_theme, "Light", StringComparison.OrdinalIgnoreCase) ? "#F3F3F3" : "#202020";

    public string CardBackgroundBrushHex =>
        string.Equals(_theme, "Light", StringComparison.OrdinalIgnoreCase) ? "#FFFFFF" : "#2C2C2C";

    public OverlayViewModel Overlay { get; } = new();

    public bool IsOverlayVisible
    {
        get => Overlay.IsVisible;
        set
        {
            if (Overlay.IsVisible != value)
            {
                Overlay.IsVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOverlayClickThrough
    {
        get => Overlay.IsClickThrough;
        set
        {
            if (Overlay.IsClickThrough != value)
            {
                Overlay.IsClickThrough = value;
                OnPropertyChanged();
            }
        }
    }

    public AppSettings CurrentSettings => _currentSettings;

    public string LastCopiedText
    {
        get => _lastCopiedText;
        private set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_lastCopiedText, normalized, StringComparison.Ordinal))
            {
                _lastCopiedText = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string? LastErrorMessage
    {
        get => _lastErrorMessage;
        private set
        {
            if (!string.Equals(_lastErrorMessage, value, StringComparison.Ordinal))
            {
                _lastErrorMessage = value;
                OnPropertyChanged();
            }
        }
    }

    // Commands
    public ICommand ToggleSessionCommand { get; }
    public ICommand StartSessionCommand { get; }
    public ICommand StopSessionCommand { get; }
    public ICommand SwitchAudioSourceCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand CopyTurnCommand { get; }
    public ICommand ToggleOverlayCommand { get; }
    public ICommand ToggleOverlayClickThroughCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public MainViewModel(
        IGeminiLiveClient? geminiClient = null,
        IAudioCaptureEngine? audioEngine = null,
        ITextSegmentationService? segmentationService = null,
        ISettingsService? settingsService = null,
        IAudioDspService? dspService = null,
        Action<Action>? uiDispatcher = null)
    {
        _dspService = dspService ?? new AudioDspService();
        _settingsService = settingsService ?? new SettingsService();
        _segmentationService = segmentationService ?? new TextSegmentationService();
        _uiDispatcher = uiDispatcher ?? (action => action());

        _ownsGeminiClient = geminiClient == null;
        _geminiClient = geminiClient ?? new GeminiLiveClient();

        _ownsAudioEngine = audioEngine == null;
        _audioEngine = audioEngine ?? new WasapiAudioCaptureEngine(_dspService);

        _currentSettings = _settingsService.LoadSettings();
        _selectedLanguage = SupportedLanguages.GetByCodeOrDefault(_currentSettings.TargetLanguageCode);
        _theme = SettingsViewModel.SanitizeTheme(_currentSettings.Theme);
        _currentAudioSource = (AudioSource)_currentSettings.AudioSource;
        _segmentationService.MaxCharacters = SettingsService.SanitizeParagraphLength(_currentSettings.MaxParagraphLength);
        Overlay.ApplySettings(_currentSettings);

        Overlay.PropertyChanged += OnOverlayPropertyChanged;

        SubscribeClientEvents();
        SubscribeAudioEvents();

        ToggleSessionCommand = new AsyncRelayCommand(() => ToggleSessionAsync());
        StartSessionCommand = new AsyncRelayCommand(() => StartSessionAsync());
        StopSessionCommand = new AsyncRelayCommand(() => StopSessionAsync());
        SwitchAudioSourceCommand = new RelayCommand(param =>
        {
            if (param is AudioSource src)
            {
                SwitchAudioSource(src);
            }
            else if (param is string str && Enum.TryParse<AudioSource>(str, true, out var parsed))
            {
                SwitchAudioSource(parsed);
            }
            else
            {
                var toggled = _currentAudioSource == AudioSource.SystemAudioLoopback
                    ? AudioSource.Microphone
                    : AudioSource.SystemAudioLoopback;
                SwitchAudioSource(toggled);
            }
        });
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        CopyTurnCommand = new RelayCommand(param =>
        {
            if (param is TranslationTurn turn)
            {
                CopyTurn(turn);
            }
        });
        ToggleOverlayCommand = new RelayCommand(() => IsOverlayVisible = !IsOverlayVisible);
        ToggleOverlayClickThroughCommand = new RelayCommand(() => IsOverlayClickThrough = !IsOverlayClickThrough);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public async Task ToggleSessionAsync(CancellationToken ct = default)
    {
        if (IsSessionActive)
        {
            await StopSessionAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await StartSessionAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task StartSessionAsync(CancellationToken ct = default)
    {
        if (IsSessionActive) return;

        LastErrorMessage = null;
        ConnectionState = LiveConnectionState.Connecting;
        StatusText = "連線中... (Connecting)";

        try
        {
            string apiKey = _settingsService.GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_currentSettings.EncryptedApiKey))
            {
                apiKey = _settingsService.DecryptSecret(_currentSettings.EncryptedApiKey);
            }

            if (string.IsNullOrWhiteSpace(apiKey) && _geminiClient is GeminiLiveClient)
            {
                throw new InvalidOperationException("尚未設定 Google AI Studio API Key，請先開啟設定輸入 API Key。");
            }

            var config = new LiveConfig
            {
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? "mock-session-key" : apiKey,
                Model = SettingsViewModel.SanitizeModel(_currentSettings.Model),
                Voice = SettingsViewModel.SanitizeVoice(_currentSettings.Voice),
                TargetLanguageCode = TargetLanguageCode,
                AutoPlayAudio = !ShouldMutePlayback,
                AudioSourceMode = (AudioSourceType)_currentAudioSource,
                MaxParagraphLength = _segmentationService.MaxCharacters
            };

            await _geminiClient.ConnectAsync(config, ct).ConfigureAwait(false);
            _audioEngine.StartCapture(_currentAudioSource);

            IsSessionActive = true;
            ConnectionState = LiveConnectionState.Connected;
            StatusText = "即時口譯進行中 (Live)";
        }
        catch (Exception ex)
        {
            IsSessionActive = false;
            ConnectionState = LiveConnectionState.Error;
            LastErrorMessage = ex.Message;
            StatusText = $"連線失敗: {ex.Message}";
        }
    }

    public async Task StopSessionAsync(CancellationToken ct = default)
    {
        try
        {
            _audioEngine.StopCapture();
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }

        try
        {
            await _geminiClient.DisconnectAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }

        lock (_turnLock)
        {
            _turnSourceBuilder.Clear();
            _turnTargetBuilder.Clear();
            _segmentationService.Reset();
        }

        Spectrum.Reset();
        CurrentRmsLevel = 0.0f;
        IsSessionActive = false;
        ConnectionState = LiveConnectionState.Disconnected;
        StatusText = "已停止 (Disconnected)";
        OnPropertyChanged(nameof(CurrentLevels));
    }

    public void SwitchAudioSource(AudioSource newSource)
    {
        if (_currentAudioSource == newSource && (!_audioEngine.IsCapturing || _audioEngine.CurrentSource == newSource))
        {
            return;
        }

        CurrentAudioSource = newSource;
        try
        {
            if (_audioEngine.IsCapturing)
            {
                _audioEngine.SwitchSource(newSource);
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            StatusText = $"音訊切換錯誤: {ex.Message}";
        }
    }

    public async Task SwitchAudioSourceAsync(AudioSource newSource)
    {
        CurrentAudioSource = newSource;
        try
        {
            if (_audioEngine.IsCapturing)
            {
                await _audioEngine.SwitchSourceAsync(newSource).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            StatusText = $"音訊切換錯誤: {ex.Message}";
        }
    }

    public void ToggleTheme()
    {
        Theme = string.Equals(_theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
    }

    public void AddTurnCard(TranslationTurn? turn)
    {
        if (turn == null) return;

        _uiDispatcher(() =>
        {
            lock (_historyLock)
            {
                HistoryCards.Add(turn);
            }
            TurnAdded?.Invoke(this, turn);
        });
    }

    public void ClearHistory()
    {
        _uiDispatcher(() =>
        {
            lock (_historyLock)
            {
                HistoryCards.Clear();
            }
        });
    }

    public string CopyTurn(TranslationTurn? turn)
    {
        if (turn == null) return string.Empty;

        string formatted = $"[{turn.Timestamp:HH:mm:ss}] {turn.SourceText} -> {turn.TargetText}";
        LastCopiedText = formatted;
        return formatted;
    }

    public void UpdateSpectrum(float[]? newBands)
    {
        Spectrum.UpdateSpectrum(newBands);
        OnPropertyChanged(nameof(CurrentLevels));
    }

    public void DecayAllSpectrum()
    {
        Spectrum.DecayAll();
        OnPropertyChanged(nameof(CurrentLevels));
    }

    public SettingsViewModel CreateSettingsViewModel()
    {
        Overlay.WriteToSettings(_currentSettings);
        var vm = new SettingsViewModel(_settingsService, _currentSettings);
        vm.SettingsSaved += (_, savedSettings) => ApplySettings(savedSettings);
        return vm;
    }

    public void ApplySettings(AppSettings? settings)
    {
        if (settings == null) return;

        _currentSettings = settings;
        SelectedLanguage = SupportedLanguages.GetByCodeOrDefault(settings.TargetLanguageCode);
        Theme = SettingsViewModel.SanitizeTheme(settings.Theme);
        _segmentationService.MaxCharacters = SettingsService.SanitizeParagraphLength(settings.MaxParagraphLength);

        var desiredSource = (AudioSource)settings.AudioSource;
        if (_currentAudioSource != desiredSource)
        {
            SwitchAudioSource(desiredSource);
        }

        Overlay.ApplySettings(settings);
        OnPropertyChanged(nameof(CurrentSettings));
        OnPropertyChanged(nameof(ShouldMutePlayback));
    }

    private void SubscribeClientEvents()
    {
        _geminiClient.ConnectionStateChanged += OnClientConnectionStateChanged;
        _geminiClient.SourceTranscriptReceived += OnPrimarySourceTranscript;
        _geminiClient.OnInputTranscriptionReceived += OnAltSourceTranscript;
        _geminiClient.TargetTranscriptReceived += OnPrimaryTargetTranscript;
        _geminiClient.OnOutputTranscriptionReceived += OnAltTargetTranscript;
        _geminiClient.AudioOutputReceived += OnPrimaryAudioOutput;
        _geminiClient.OnOutputAudioReceived += OnAltAudioOutput;
        _geminiClient.Interrupted += OnPrimaryInterrupted;
        _geminiClient.OnInterruptedReceived += OnAltInterrupted;
        _geminiClient.TurnCompleted += OnPrimaryTurnCompleted;
        _geminiClient.OnTurnCompletedReceived += OnAltTurnCompleted;
        _geminiClient.ErrorOccurred += OnPrimaryError;
        _geminiClient.OnErrorOccurred += OnAltError;
    }

    private void UnsubscribeClientEvents()
    {
        _geminiClient.ConnectionStateChanged -= OnClientConnectionStateChanged;
        _geminiClient.SourceTranscriptReceived -= OnPrimarySourceTranscript;
        _geminiClient.OnInputTranscriptionReceived -= OnAltSourceTranscript;
        _geminiClient.TargetTranscriptReceived -= OnPrimaryTargetTranscript;
        _geminiClient.OnOutputTranscriptionReceived -= OnAltTargetTranscript;
        _geminiClient.AudioOutputReceived -= OnPrimaryAudioOutput;
        _geminiClient.OnOutputAudioReceived -= OnAltAudioOutput;
        _geminiClient.Interrupted -= OnPrimaryInterrupted;
        _geminiClient.OnInterruptedReceived -= OnAltInterrupted;
        _geminiClient.TurnCompleted -= OnPrimaryTurnCompleted;
        _geminiClient.OnTurnCompletedReceived -= OnAltTurnCompleted;
        _geminiClient.ErrorOccurred -= OnPrimaryError;
        _geminiClient.OnErrorOccurred -= OnAltError;
    }

    private void SubscribeAudioEvents()
    {
        _audioEngine.OnAudioChunkCaptured += OnPrimaryAudioChunkCaptured;
        _audioEngine.AudioChunkAvailable += OnAltAudioChunkAvailable;
        _audioEngine.OnSpectrumCalculated += OnPrimarySpectrumCalculated;
        _audioEngine.SpectrumDataAvailable += OnAltSpectrumDataAvailable;
        _audioEngine.OnCaptureError += OnPrimaryCaptureError;
        _audioEngine.CaptureErrorOccurred += OnAltCaptureErrorOccurred;
    }

    private void UnsubscribeAudioEvents()
    {
        _audioEngine.OnAudioChunkCaptured -= OnPrimaryAudioChunkCaptured;
        _audioEngine.AudioChunkAvailable -= OnAltAudioChunkAvailable;
        _audioEngine.OnSpectrumCalculated -= OnPrimarySpectrumCalculated;
        _audioEngine.SpectrumDataAvailable -= OnAltSpectrumDataAvailable;
        _audioEngine.OnCaptureError -= OnPrimaryCaptureError;
        _audioEngine.CaptureErrorOccurred -= OnAltCaptureErrorOccurred;
    }

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(IsOverlayVisible));
        }
        else if (e.PropertyName == nameof(OverlayViewModel.IsClickThrough))
        {
            OnPropertyChanged(nameof(IsOverlayClickThrough));
        }
    }

    private void OnClientConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        ConnectionState = e.NewState;
        StatusText = e.NewState switch
        {
            LiveConnectionState.Connected => "即時口譯進行中 (Live)",
            LiveConnectionState.Connecting => "連線中... (Connecting)",
            LiveConnectionState.Reconnecting => "重新連線中... (Reconnecting)",
            LiveConnectionState.Error => $"連線錯誤: {e.Reason ?? "Unknown"}",
            _ => "準備就緒 (Ready)"
        };
    }

    private void OnPrimarySourceTranscript(object? sender, TranscriptEventArgs e)
    {
        _suppressNextAltSource = true;
        ProcessSourceTranscript(e.Text, e.IsFinal);
    }

    private void OnAltSourceTranscript(object? sender, string text)
    {
        if (_suppressNextAltSource)
        {
            _suppressNextAltSource = false;
            return;
        }
        ProcessSourceTranscript(text, false);
    }

    private void ProcessSourceTranscript(string? text, bool isFinal)
    {
        if (string.IsNullOrEmpty(text)) return;

        string snapshot;
        lock (_turnLock)
        {
            if (_turnSourceBuilder.Length > 0 && !char.IsWhiteSpace(_turnSourceBuilder[^1]) && !char.IsWhiteSpace(text[0]) &&
                text[0] <= 127 && _turnSourceBuilder[^1] <= 127)
            {
                _turnSourceBuilder.Append(' ');
            }
            _turnSourceBuilder.Append(text);
            snapshot = _turnSourceBuilder.ToString().Trim();
        }

        CurrentSourceTranscript = snapshot;
        Overlay.SourceText = snapshot;
    }

    private void OnPrimaryTargetTranscript(object? sender, TranscriptEventArgs e)
    {
        _suppressNextAltTarget = true;
        ProcessTargetTranscript(e.Text, e.IsFinal);
    }

    private void OnAltTargetTranscript(object? sender, string text)
    {
        if (_suppressNextAltTarget)
        {
            _suppressNextAltTarget = false;
            return;
        }
        ProcessTargetTranscript(text, false);
    }

    private void ProcessTargetTranscript(string? text, bool isFinal)
    {
        if (string.IsNullOrEmpty(text) && !isFinal) return;

        string activeSegment;
        string sourceSnapshot;

        lock (_turnLock)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _turnTargetBuilder.Append(text);
            }

            var segResult = _segmentationService.ProcessToken(text ?? string.Empty, isFinal);
            string fullTurnTarget = _turnTargetBuilder.ToString().Trim();

            var chunks = _segmentationService.ChunkText(fullTurnTarget, _segmentationService.MaxCharacters);
            if (chunks.Count > 0)
            {
                activeSegment = chunks[^1];
            }
            else if (segResult.HasCommitted)
            {
                activeSegment = segResult.CommittedText;
            }
            else
            {
                activeSegment = segResult.PendingText;
            }

            sourceSnapshot = _turnSourceBuilder.ToString().Trim();
        }

        if (!string.IsNullOrEmpty(activeSegment))
        {
            CurrentTargetTranscript = activeSegment;
            Overlay.UpdateSubtitle(
                activeSegment,
                string.IsNullOrWhiteSpace(sourceSnapshot) ? Overlay.SourceText : sourceSnapshot);
        }

        if (isFinal)
        {
            CommitPendingTurn();
        }
    }

    private void OnPrimaryAudioOutput(object? sender, AudioOutputEventArgs e)
    {
        _suppressNextAltAudioOut = true;
        ProcessAudioOutput(e.Pcm24kData);
    }

    private void OnAltAudioOutput(object? sender, byte[] pcm24kData)
    {
        if (_suppressNextAltAudioOut)
        {
            _suppressNextAltAudioOut = false;
            return;
        }
        ProcessAudioOutput(pcm24kData);
    }

    private void ProcessAudioOutput(byte[]? pcm24kData)
    {
        if (pcm24kData == null || pcm24kData.Length == 0) return;

        // Acoustic feedback protection: mute TTS speaker playback when SystemAudioLoopback is active
        if (ShouldMutePlayback)
        {
            return;
        }

        Interlocked.Increment(ref _playedAudioChunkCount);
        OnPropertyChanged(nameof(PlayedAudioChunkCount));
    }

    private void OnPrimaryInterrupted(object? sender, EventArgs e)
    {
        _suppressNextAltInterrupted = true;
        ProcessInterrupted();
    }

    private void OnAltInterrupted(object? sender, bool interrupted)
    {
        if (_suppressNextAltInterrupted)
        {
            _suppressNextAltInterrupted = false;
            return;
        }
        if (interrupted)
        {
            ProcessInterrupted();
        }
    }

    private void ProcessInterrupted()
    {
        lock (_turnLock)
        {
            _turnSourceBuilder.Clear();
            _turnTargetBuilder.Clear();
            _segmentationService.Reset();
        }

        CurrentSourceTranscript = string.Empty;
        CurrentTargetTranscript = string.Empty;
        StatusText = "語音中斷，重新聆聽中... (Interrupted)";
    }

    private void OnPrimaryTurnCompleted(object? sender, EventArgs e)
    {
        _suppressNextAltTurnComplete = true;
        ProcessTurnCompleted();
    }

    private void OnAltTurnCompleted(object? sender, bool completed)
    {
        if (_suppressNextAltTurnComplete)
        {
            _suppressNextAltTurnComplete = false;
            return;
        }
        if (completed)
        {
            ProcessTurnCompleted();
        }
    }

    private void ProcessTurnCompleted()
    {
        CommitPendingTurn();
    }

    private void CommitPendingTurn()
    {
        string finalSource;
        string finalTarget;

        lock (_turnLock)
        {
            var flushResult = _segmentationService.ProcessToken(string.Empty, isFinal: true);
            finalSource = _turnSourceBuilder.ToString().Trim();
            finalTarget = _turnTargetBuilder.ToString().Trim();

            if (string.IsNullOrEmpty(finalTarget) && flushResult.HasCommitted)
            {
                finalTarget = flushResult.CommittedText;
            }

            _turnSourceBuilder.Clear();
            _turnTargetBuilder.Clear();
            _segmentationService.Reset();
        }

        if (string.IsNullOrWhiteSpace(finalTarget) && string.IsNullOrWhiteSpace(finalSource))
        {
            return;
        }

        var turn = new TranslationTurn(
            sourceText: finalSource,
            targetText: finalTarget,
            isFinal: true,
            sourceLang: "auto",
            targetLang: TargetLanguageCode);

        AddTurnCard(turn);

        // Keep overlay displaying a segment within MaxCharacters
        var chunks = _segmentationService.ChunkText(finalTarget, _segmentationService.MaxCharacters);
        string overlayTarget = chunks.Count > 0 ? chunks[^1] : finalTarget;
        Overlay.UpdateSubtitle(overlayTarget, finalSource);
    }

    private void OnPrimaryError(object? sender, string message)
    {
        _suppressNextAltError = true;
        ProcessError(message);
    }

    private void OnAltError(object? sender, Exception ex)
    {
        if (_suppressNextAltError)
        {
            _suppressNextAltError = false;
            return;
        }
        ProcessError(ex?.Message ?? "Unknown Gemini error");
    }

    private void ProcessError(string message)
    {
        LastErrorMessage = message;
        StatusText = $"錯誤: {message}";
    }

    private void OnPrimaryAudioChunkCaptured(object? sender, byte[] chunk)
    {
        _suppressNextAltAudioChunk = true;
        ProcessCapturedAudioChunk(chunk);
    }

    private void OnAltAudioChunkAvailable(object? sender, AudioChunkEventArgs e)
    {
        if (_suppressNextAltAudioChunk)
        {
            _suppressNextAltAudioChunk = false;
            return;
        }
        ProcessCapturedAudioChunk(e.PcmChunk);
    }

    private void ProcessCapturedAudioChunk(byte[]? chunk)
    {
        if (chunk == null || chunk.Length == 0) return;

        float rms = _dspService.CalculateRms(chunk);
        CurrentRmsLevel = rms;

        if (_geminiClient.IsConnected || _isSessionActive)
        {
            _ = _geminiClient.SendAudioChunkAsync(chunk);
        }
    }

    private void OnPrimarySpectrumCalculated(object? sender, float[] bands)
    {
        _suppressNextAltSpectrum = true;
        UpdateSpectrum(bands);
    }

    private void OnAltSpectrumDataAvailable(object? sender, AudioSpectrumEventArgs e)
    {
        if (_suppressNextAltSpectrum)
        {
            _suppressNextAltSpectrum = false;
            return;
        }
        UpdateSpectrum(e.Bands);
    }

    private void OnPrimaryCaptureError(object? sender, Exception ex)
    {
        _suppressNextAltCaptureError = true;
        ProcessError(ex?.Message ?? "Audio capture error");
    }

    private void OnAltCaptureErrorOccurred(object? sender, string message)
    {
        if (_suppressNextAltCaptureError)
        {
            _suppressNextAltCaptureError = false;
            return;
        }
        ProcessError(message);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;

        Overlay.PropertyChanged -= OnOverlayPropertyChanged;
        UnsubscribeClientEvents();
        UnsubscribeAudioEvents();

        if (_ownsAudioEngine)
        {
            try { _audioEngine.Dispose(); } catch { }
        }

        if (_ownsGeminiClient)
        {
            try { _geminiClient.Dispose(); } catch { }
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;

        Overlay.PropertyChanged -= OnOverlayPropertyChanged;
        UnsubscribeClientEvents();
        UnsubscribeAudioEvents();

        if (_ownsAudioEngine)
        {
            try { await _audioEngine.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        if (_ownsGeminiClient)
        {
            try { await _geminiClient.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        GC.SuppressFinalize(this);
    }
}
