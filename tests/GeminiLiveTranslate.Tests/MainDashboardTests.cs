using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.App.Audio;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.App.ViewModels;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Milestone 4 (M4) Comprehensive Unit & Integration Test Suite for
/// Windows 11 Fluent Design 2 Main Dashboard Window, SpectrumEngine, SpectrumVisualizer,
/// MainViewModel, OverlayViewModel, SettingsViewModel, RelayCommand, and XAML contracts.
/// </summary>
public class MainDashboardTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "GeminiLiveTranslate.sln")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    }

    #region Test Doubles for ViewModel Isolation

    private sealed class FakeGeminiLiveClient : IGeminiLiveClient
    {
        public LiveConnectionState CurrentState { get; private set; } = LiveConnectionState.Disconnected;
        public bool IsConnected => CurrentState == LiveConnectionState.Connected;
        public LiveConfig? CurrentConfig { get; private set; }
        public List<byte[]> SentAudioChunks { get; } = new();
        public List<string> SentTextMessages { get; } = new();
        public bool ThrowOnConnect { get; set; }

        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
        public event EventHandler<TranscriptEventArgs>? SourceTranscriptReceived;
        public event EventHandler<TranscriptEventArgs>? TargetTranscriptReceived;
        public event EventHandler<AudioOutputEventArgs>? AudioOutputReceived;
        public event EventHandler? Interrupted;
        public event EventHandler? TurnCompleted;
        public event EventHandler<string>? ErrorOccurred;

        public event EventHandler<string>? OnInputTranscriptionReceived;
        public event EventHandler<string>? OnOutputTranscriptionReceived;
        public event EventHandler<byte[]>? OnOutputAudioReceived;
        public event EventHandler<bool>? OnInterruptedReceived;
        public event EventHandler<bool>? OnTurnCompletedReceived;
        public event EventHandler<Exception>? OnErrorOccurred;

        public Task ConnectAsync(LiveConfig config, CancellationToken cancellationToken = default)
        {
            if (ThrowOnConnect)
            {
                throw new InvalidOperationException("Simulated WebSocket connection failure.");
            }

            CurrentConfig = config;
            var old = CurrentState;
            CurrentState = LiveConnectionState.Connected;
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(old, CurrentState));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            var old = CurrentState;
            CurrentState = LiveConnectionState.Disconnected;
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(old, CurrentState));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => DisconnectAsync(CancellationToken.None);

        public Task SendAudioChunkAsync(byte[] pcm16kChunk, CancellationToken cancellationToken = default)
        {
            lock (SentAudioChunks)
            {
                SentAudioChunks.Add(pcm16kChunk);
            }
            return Task.CompletedTask;
        }

        public Task SendTextMessageAsync(string text, CancellationToken cancellationToken = default)
        {
            SentTextMessages.Add(text);
            return Task.CompletedTask;
        }

        public void EmitDualSourceTranscript(string text, bool isFinal = false)
        {
            SourceTranscriptReceived?.Invoke(this, new TranscriptEventArgs(text, isFinal));
            OnInputTranscriptionReceived?.Invoke(this, text);
        }

        public void EmitAltOnlySourceTranscript(string text)
        {
            OnInputTranscriptionReceived?.Invoke(this, text);
        }

        public void EmitDualTargetTranscript(string text, bool isFinal = false)
        {
            TargetTranscriptReceived?.Invoke(this, new TranscriptEventArgs(text, isFinal));
            OnOutputTranscriptionReceived?.Invoke(this, text);
        }

        public void EmitAltOnlyTargetTranscript(string text)
        {
            OnOutputTranscriptionReceived?.Invoke(this, text);
        }

        public void EmitDualAudioOutput(byte[] pcm24k)
        {
            AudioOutputReceived?.Invoke(this, new AudioOutputEventArgs(pcm24k, 24000));
            OnOutputAudioReceived?.Invoke(this, pcm24k);
        }

        public void EmitDualInterrupted()
        {
            Interrupted?.Invoke(this, EventArgs.Empty);
            OnInterruptedReceived?.Invoke(this, true);
        }

        public void EmitDualTurnCompleted()
        {
            TurnCompleted?.Invoke(this, EventArgs.Empty);
            OnTurnCompletedReceived?.Invoke(this, true);
        }

        public void EmitDualError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
            OnErrorOccurred?.Invoke(this, new InvalidOperationException(message));
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAudioCaptureEngine : GeminiLiveTranslate.App.Audio.IAudioCaptureEngine
    {
        public bool IsCapturing { get; private set; }
        public AudioSource CurrentSource { get; private set; } = AudioSource.SystemAudioLoopback;
        public string? CurrentDeviceId { get; private set; }
        public int SwitchCount { get; private set; }

        public event EventHandler<byte[]>? OnAudioChunkCaptured;
        public event EventHandler<float[]>? OnSpectrumCalculated;
        public event EventHandler<Exception>? OnCaptureError;
        public event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;
        public event EventHandler<AudioSpectrumEventArgs>? SpectrumDataAvailable;
        public event EventHandler<string>? CaptureErrorOccurred;

        public void StartCapture(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null)
        {
            CurrentSource = source;
            CurrentDeviceId = deviceId;
            IsCapturing = true;
        }

        public Task StartAsync(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null)
        {
            StartCapture(source, deviceId);
            return Task.CompletedTask;
        }

        public void StartCapture(AudioSourceType sourceType, string? deviceId = null)
            => StartCapture((AudioSource)sourceType, deviceId);

        public Task StartAsync(AudioSourceType sourceType, string? deviceId = null)
            => StartAsync((AudioSource)sourceType, deviceId);

        public void StopCapture() => IsCapturing = false;

        public Task StopAsync()
        {
            StopCapture();
            return Task.CompletedTask;
        }

        public void SwitchSource(AudioSource newSource, string? deviceId = null)
        {
            CurrentSource = newSource;
            CurrentDeviceId = deviceId;
            SwitchCount++;
        }

        public Task SwitchSourceAsync(AudioSource newSource, string? deviceId = null)
        {
            SwitchSource(newSource, deviceId);
            return Task.CompletedTask;
        }

        public void SwitchSource(AudioSourceType newSource, string? deviceId = null)
            => SwitchSource((AudioSource)newSource, deviceId);

        public Task SwitchSourceAsync(AudioSourceType newSource, string? deviceId = null)
            => SwitchSourceAsync((AudioSource)newSource, deviceId);

        public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices() => Array.Empty<AudioDeviceDescriptor>();
        public Task<IReadOnlyList<AudioDeviceDescriptor>> GetMicrophoneDevicesAsync()
            => Task.FromResult<IReadOnlyList<AudioDeviceDescriptor>>(Array.Empty<AudioDeviceDescriptor>());

        public void EmitDualAudioChunk(byte[] chunk)
        {
            OnAudioChunkCaptured?.Invoke(this, chunk);
            AudioChunkAvailable?.Invoke(this, new AudioChunkEventArgs(chunk));
        }

        public void EmitDualSpectrum(float[] bands)
        {
            OnSpectrumCalculated?.Invoke(this, bands);
            SpectrumDataAvailable?.Invoke(this, new AudioSpectrumEventArgs(bands));
        }

        public void EmitDualCaptureError(Exception ex)
        {
            OnCaptureError?.Invoke(this, ex);
            CaptureErrorOccurred?.Invoke(this, ex.Message);
        }

        public void Dispose() => IsCapturing = false;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    #endregion

    #region 1. SpectrumEngine Physics & Geometry Tests (16 Tests)

    [Fact]
    public void SpectrumEngine_DefaultInitialization_Has36ZeroedBands()
    {
        var engine = new SpectrumEngine();

        Assert.Equal(36, engine.BandCount);
        Assert.Equal(36, engine.CurrentLevels.Count);
        Assert.Equal(36, engine.PeakLevels.Count);
        Assert.All(engine.CurrentLevels, v => Assert.Equal(0.0f, v));
        Assert.All(engine.PeakLevels, v => Assert.Equal(0.0f, v));
    }

    [Fact]
    public void SpectrumEngine_UpdateSpectrum_ImmediateAttackOnRisingSignal()
    {
        var engine = new SpectrumEngine();
        float[] input = new float[36];
        input[0] = 0.25f;
        input[17] = 0.85f;
        input[35] = 1.0f;

        engine.UpdateSpectrum(input);

        Assert.Equal(0.25f, engine.CurrentLevels[0], 3);
        Assert.Equal(0.85f, engine.CurrentLevels[17], 3);
        Assert.Equal(1.0f, engine.CurrentLevels[35], 3);
        Assert.Equal(0.85f, engine.PeakLevels[17], 3);
    }

    [Fact]
    public void SpectrumEngine_UpdateSpectrum_SmoothDecayOnFallingSignal()
    {
        var engine = new SpectrumEngine { DecayRate = 0.10f, PeakDecayRate = 0.03f };
        float[] high = new float[36];
        high[10] = 0.80f;
        engine.UpdateSpectrum(high);

        float[] silent = new float[36];
        engine.UpdateSpectrum(silent);

        Assert.Equal(0.70f, engine.CurrentLevels[10], 3);
        Assert.Equal(0.77f, engine.PeakLevels[10], 3);
    }

    [Fact]
    public void SpectrumEngine_UpdateSpectrum_DecayStopsAtIntermediateTarget()
    {
        var engine = new SpectrumEngine { DecayRate = 0.20f };
        float[] high = new float[36];
        high[4] = 0.80f;
        engine.UpdateSpectrum(high);

        float[] medium = new float[36];
        medium[4] = 0.72f; // Only 0.08f below 0.80f, smaller than DecayRate 0.20f
        engine.UpdateSpectrum(medium);

        Assert.Equal(0.72f, engine.CurrentLevels[4], 3);
    }

    [Fact]
    public void SpectrumEngine_DecayAll_NeverProducesNegativeValuesAfter100Frames()
    {
        var engine = new SpectrumEngine { DecayRate = 0.08f, PeakDecayRate = 0.03f };
        float[] full = Enumerable.Repeat(1.0f, 36).ToArray();
        engine.UpdateSpectrum(full);

        for (int frame = 0; frame < 100; frame++)
        {
            engine.DecayAll();
            Assert.All(engine.CurrentLevels, lvl => Assert.InRange(lvl, 0.0f, 1.0f));
            Assert.All(engine.PeakLevels, pk => Assert.InRange(pk, 0.0f, 1.0f));
        }

        Assert.All(engine.CurrentLevels, lvl => Assert.Equal(0.0f, lvl));
        Assert.All(engine.PeakLevels, pk => Assert.Equal(0.0f, pk));
    }

    [Fact]
    public void SpectrumEngine_UpdateSpectrum_NullOrEmptyInput_TriggersDecayAll()
    {
        var engine = new SpectrumEngine { DecayRate = 0.1f };
        float[] bands = Enumerable.Repeat(0.5f, 36).ToArray();
        engine.UpdateSpectrum(bands);

        engine.UpdateSpectrum(null);
        Assert.Equal(0.4f, engine.CurrentLevels[0], 3);

        engine.UpdateSpectrum(Array.Empty<float>());
        Assert.Equal(0.3f, engine.CurrentLevels[0], 3);
    }

    [Fact]
    public void SpectrumEngine_SanitizesNaNInfinityAndOutOfRangeValues()
    {
        var engine = new SpectrumEngine();
        float[] corrupted = new float[36];
        corrupted[0] = float.NaN;
        corrupted[1] = float.PositiveInfinity;
        corrupted[2] = float.NegativeInfinity;
        corrupted[3] = -50.0f;
        corrupted[4] = 42.0f;

        engine.UpdateSpectrum(corrupted);

        Assert.Equal(0.0f, engine.CurrentLevels[0]);
        Assert.Equal(0.0f, engine.CurrentLevels[1]);
        Assert.Equal(0.0f, engine.CurrentLevels[2]);
        Assert.Equal(0.0f, engine.CurrentLevels[3]);
        Assert.Equal(1.0f, engine.CurrentLevels[4]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    public void SpectrumEngine_ResamplesArbitraryBinCountsTo36Bands(int inputBinCount)
    {
        var engine = new SpectrumEngine();
        float[] bins = Enumerable.Repeat(0.65f, inputBinCount).ToArray();

        engine.UpdateSpectrum(bins);

        Assert.Equal(36, engine.CurrentLevels.Count);
        Assert.All(engine.CurrentLevels, v => Assert.Equal(0.65f, v, 2));
    }

    [Fact]
    public void SpectrumEngine_Reset_ImmediatelyZeroesCurrentAndPeakLevels()
    {
        var engine = new SpectrumEngine();
        engine.UpdateSpectrum(Enumerable.Repeat(0.9f, 36).ToArray());

        engine.Reset();

        Assert.All(engine.CurrentLevels, v => Assert.Equal(0.0f, v));
        Assert.All(engine.PeakLevels, v => Assert.Equal(0.0f, v));
    }

    [Fact]
    public void SpectrumEngine_DecayRateProperty_SanitizesInvalidValues()
    {
        var engine = new SpectrumEngine();

        engine.DecayRate = float.NaN;
        Assert.Equal(SpectrumEngine.DefaultDecayRate, engine.DecayRate);

        engine.DecayRate = -1.0f;
        Assert.Equal(SpectrumEngine.DefaultDecayRate, engine.DecayRate);

        engine.PeakDecayRate = float.PositiveInfinity;
        Assert.Equal(SpectrumEngine.DefaultPeakDecayRate, engine.PeakDecayRate);
    }

    [Fact]
    public void SpectrumEngine_CalculateBarGeometries_Computes36ValidPillRects()
    {
        var engine = new SpectrumEngine();
        float[] levels = new float[36];
        levels[0] = 0.0f;
        levels[18] = 0.5f;
        levels[35] = 1.0f;
        engine.UpdateSpectrum(levels);

        var rects = engine.CalculateBarGeometries(720.0, 100.0, minBarHeight: 4.0);

        Assert.Equal(36, rects.Count);
        // Band 0 at 0.0 -> minBarHeight = 4.0, Y = 96.0
        Assert.Equal(4.0, rects[0].Height, 2);
        Assert.Equal(96.0, rects[0].Y, 2);
        // Band 18 at 0.5 -> Height = 50.0, Y = 50.0
        Assert.Equal(50.0, rects[18].Height, 2);
        Assert.Equal(50.0, rects[18].Y, 2);
        // Band 35 at 1.0 -> Height = 100.0, Y = 0.0
        Assert.Equal(100.0, rects[35].Height, 2);
        Assert.Equal(0.0, rects[35].Y, 2);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(720, 0)]
    [InlineData(-100, 50)]
    [InlineData(double.NaN, 100)]
    [InlineData(720, double.PositiveInfinity)]
    public void SpectrumEngine_CalculateBarGeometries_InvalidDimensions_ReturnsEmpty(double w, double h)
    {
        var engine = new SpectrumEngine();
        var rects = engine.CalculateBarGeometries(w, h);
        Assert.Empty(rects);
    }

    #endregion

    #region 2. RelayCommand & AsyncRelayCommand Tests (6 Tests)

    [Fact]
    public void RelayCommand_ExecutesActionWhenCanExecuteIsTrue()
    {
        int count = 0;
        var cmd = new RelayCommand(() => count++, () => true);

        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, count);
    }

    [Fact]
    public void RelayCommand_BlocksExecutionWhenCanExecuteIsFalse()
    {
        int count = 0;
        bool allowed = false;
        var cmd = new RelayCommand(() => count++, () => allowed);

        Assert.False(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(0, count);

        bool eventRaised = false;
        cmd.CanExecuteChanged += (_, _) => eventRaised = true;
        allowed = true;
        cmd.RaiseCanExecuteChanged();

        Assert.True(eventRaised);
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AsyncRelayCommand_PreventsReentrantExecutionAndNotifiesCanExecuteChanged()
    {
        var tcs = new TaskCompletionSource();
        int executions = 0;
        var cmd = new AsyncRelayCommand(async () =>
        {
            Interlocked.Increment(ref executions);
            await tcs.Task;
        });

        Assert.True(cmd.CanExecute(null));

        Task firstRun = cmd.ExecuteAsync();
        Assert.True(cmd.IsExecuting);
        Assert.False(cmd.CanExecute(null));

        // Second call while executing must be ignored
        await cmd.ExecuteAsync();
        Assert.Equal(1, executions);

        tcs.SetResult();
        await firstRun;

        Assert.False(cmd.IsExecuting);
        Assert.True(cmd.CanExecute(null));
    }

    #endregion

    #region 3. OverlayViewModel Tests (12 Tests)

    [Fact]
    public void OverlayViewModel_DefaultState_MatchesFluent2OverlaySpec()
    {
        var vm = new OverlayViewModel();

        Assert.Equal("即時口譯字幕準備就緒", vm.TargetText);
        Assert.Equal("Original voice transcription will appear here...", vm.SourceText);
        Assert.Equal(24.0, vm.FontSize);
        Assert.Equal(1.0, vm.Opacity);
        Assert.Equal(1, vm.BackgroundAlphaMode);
        Assert.False(vm.IsClickThrough);
        Assert.False(vm.IsVisible);
        Assert.Equal("桌面懸浮字幕", vm.StatusBadgeText);
    }

    [Fact]
    public void OverlayViewModel_UpdateSubtitleAndUpdateFromTurn_UpdatesTextsAndNotifications()
    {
        var vm = new OverlayViewModel();
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.UpdateSubtitle("歡迎收看直播", "Welcome to the stream");
        Assert.Equal("歡迎收看直播", vm.TargetText);
        Assert.Equal("Welcome to the stream", vm.SourceText);
        Assert.True(vm.HasSourceText);
        Assert.Contains(nameof(OverlayViewModel.TargetText), changedProps);
        Assert.Contains(nameof(OverlayViewModel.SourceText), changedProps);

        var turn = new TranslationTurn("Good evening", "晚安", true, "en-US", "zh-TW");
        vm.UpdateFromTurn(turn);
        Assert.Equal("晚安", vm.TargetText);
        Assert.Equal("Good evening", vm.SourceText);
    }

    [Fact]
    public void OverlayViewModel_FontSizeScaling_ClampsStrictlyTo12And72()
    {
        var vm = new OverlayViewModel { FontSize = 24.0 };

        vm.IncreaseFontSizeCommand.Execute(null);
        Assert.Equal(26.0, vm.FontSize);

        vm.DecreaseFontSizeCommand.Execute(null);
        Assert.Equal(24.0, vm.FontSize);

        vm.FontSize = 71.0;
        vm.IncreaseFontSize(5.0);
        Assert.Equal(72.0, vm.FontSize);

        vm.FontSize = 13.0;
        vm.DecreaseFontSize(5.0);
        Assert.Equal(12.0, vm.FontSize);
    }

    [Fact]
    public void OverlayViewModel_CycleBackgroundOpacity_CyclesOpacityAndModeColors()
    {
        var vm = new OverlayViewModel { Opacity = 1.0, BackgroundAlphaMode = 0 };

        vm.CycleBackgroundOpacityCommand.Execute(null);
        Assert.Equal(0.5, vm.Opacity);
        Assert.Equal(1, vm.BackgroundAlphaMode);
        Assert.Equal(Win32Constants.FrostedSlateBackground, vm.BackgroundHex);

        vm.CycleBackgroundOpacityCommand.Execute(null);
        Assert.Equal(0.0, vm.Opacity);
        Assert.Equal(2, vm.BackgroundAlphaMode);
        Assert.Equal(Win32Constants.TransparentBackground, vm.BackgroundHex);

        vm.CycleBackgroundOpacityCommand.Execute(null);
        Assert.Equal(1.0, vm.Opacity);
        Assert.Equal(0, vm.BackgroundAlphaMode);
        Assert.Equal(Win32Constants.DarkSlateBackground, vm.BackgroundHex);
    }

    [Fact]
    public void OverlayViewModel_ClickThroughToggle_UpdatesStatusBadgeAndFiresEvent()
    {
        var vm = new OverlayViewModel();
        bool? lastEventState = null;
        vm.ClickThroughChanged += state => lastEventState = state;

        vm.ToggleClickThroughCommand.Execute(null);
        Assert.True(vm.IsClickThrough);
        Assert.Equal(true, lastEventState);
        Assert.Contains("Ctrl+F8", vm.StatusBadgeText);

        vm.SetClickThrough(false);
        Assert.False(vm.IsClickThrough);
        Assert.Equal(false, lastEventState);
        Assert.Equal("桌面懸浮字幕", vm.StatusBadgeText);
    }

    [Fact]
    public void OverlayViewModel_SettingsRoundTrip_PreservesFontSizeOpacityAndClickThrough()
    {
        var settings = new AppSettings
        {
            OverlayFontSize = 32.0,
            OverlayOpacity = 0.5,
            OverlayClickThrough = true
        };

        var vm = new OverlayViewModel();
        vm.ApplySettings(settings);

        Assert.Equal(32.0, vm.FontSize);
        Assert.Equal(0.5, vm.Opacity);
        Assert.True(vm.IsClickThrough);

        vm.FontSize = 40.0;
        vm.Opacity = 1.0;
        vm.IsClickThrough = false;

        var targetSettings = new AppSettings();
        vm.WriteToSettings(targetSettings);

        Assert.Equal(40.0, targetSettings.OverlayFontSize);
        Assert.Equal(1.0, targetSettings.OverlayOpacity);
        Assert.False(targetSettings.OverlayClickThrough);
    }

    #endregion

    #region 4. SettingsViewModel Tests (14 Tests)

    [Fact]
    public void SettingsViewModel_LoadsExistingSettingsAndApiKeyFromService()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempPath);
            service.SaveSettings(new AppSettings
            {
                Model = "models/gemini-2.5-flash",
                Voice = "Fenrir",
                MaxParagraphLength = 75,
                TargetLanguageCode = "ja-JP",
                AutoPlayAudio = true,
                Theme = "Light"
            });
            service.SaveApiKey("AIzaSyTestSecretKey987654");

            var vm = new SettingsViewModel(service);

            Assert.Equal("AIzaSyTestSecretKey987654", vm.ApiKey);
            Assert.StartsWith("AIza", vm.MaskedApiKey);
            Assert.EndsWith("7654", vm.MaskedApiKey);
            Assert.Contains("•", vm.MaskedApiKey);
            Assert.Equal("models/gemini-2.5-flash", vm.SelectedModel);
            Assert.Equal("Fenrir", vm.SelectedVoice);
            Assert.Equal(75, vm.SelectedParagraphLength);
            Assert.Equal("ja-JP", vm.TargetLanguageCode);
            Assert.True(vm.AutoPlayAudio);
            Assert.Equal("Light", vm.SelectedTheme);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SettingsViewModel_ToggleRevealApiKey_SwitchesRevealedState()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_test_{Guid.NewGuid():N}.json");
        try
        {
            var vm = new SettingsViewModel(new SettingsService(tempPath));
            Assert.False(vm.IsApiKeyRevealed);

            vm.ToggleRevealApiKeyCommand.Execute(null);
            Assert.True(vm.IsApiKeyRevealed);

            vm.ToggleRevealApiKeyCommand.Execute(null);
            Assert.False(vm.IsApiKeyRevealed);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SettingsViewModel_Cancel_NeverMutatesOriginalSettingsOrDisk()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempPath);
            var original = new AppSettings
            {
                Model = "models/gemini-2.0-flash-exp",
                Voice = "Puck",
                MaxParagraphLength = 50,
                TargetLanguageCode = "zh-TW",
                AutoPlayAudio = false,
                Theme = "Dark"
            };
            service.SaveSettings(original);
            service.SaveApiKey("AIzaOriginalKey123456");

            var vm = new SettingsViewModel(service, original);

            // Mutate every draft property in the dialog ViewModel
            vm.ApiKey = "AIzaMutatedKey999999";
            vm.SelectedModel = "models/gemini-3.5-live-translate-preview";
            vm.SelectedVoice = "Aoede";
            vm.SelectedParagraphLength = 100;
            vm.TargetLanguageCode = "fr-FR";
            vm.AutoPlayAudio = true;
            vm.SelectedTheme = "Light";

            bool cancelFired = false;
            vm.SettingsCancelled += (_, _) => cancelFired = true;

            // Execute Cancel
            vm.CancelCommand.Execute(null);

            Assert.True(cancelFired);
            Assert.False(vm.IsSaved);

            // Verify original in-memory object was NEVER mutated
            Assert.Equal("models/gemini-2.0-flash-exp", original.Model);
            Assert.Equal("Puck", original.Voice);
            Assert.Equal(50, original.MaxParagraphLength);
            Assert.Equal("zh-TW", original.TargetLanguageCode);
            Assert.False(original.AutoPlayAudio);
            Assert.Equal("Dark", original.Theme);

            // Verify persisted disk settings and API key were NEVER mutated
            var reloaded = service.LoadSettings();
            Assert.Equal("models/gemini-2.0-flash-exp", reloaded.Model);
            Assert.Equal("Puck", reloaded.Voice);
            Assert.Equal(50, reloaded.MaxParagraphLength);
            Assert.Equal("AIzaOriginalKey123456", service.GetApiKey());
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SettingsViewModel_Save_EncryptsApiKeyAndPersistsAllSettings()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempPath);
            var vm = new SettingsViewModel(service)
            {
                ApiKey = "AIzaSySuperSecretKey2026",
                SelectedModel = "models/gemini-3.5-live-translate-preview",
                SelectedVoice = "Kore",
                SelectedParagraphLength = 35,
                TargetLanguageCode = "ko-KR",
                AutoPlayAudio = true,
                SelectedTheme = "Light"
            };

            AppSettings? eventSettings = null;
            vm.SettingsSaved += (_, s) => eventSettings = s;

            vm.SaveCommand.Execute(null);

            Assert.True(vm.IsSaved);
            Assert.NotNull(eventSettings);
            Assert.Equal("models/gemini-3.5-live-translate-preview", eventSettings.Model);
            Assert.Equal("Kore", eventSettings.Voice);
            Assert.Equal(35, eventSettings.MaxParagraphLength);
            Assert.Equal("ko-KR", eventSettings.TargetLanguageCode);
            Assert.True(eventSettings.AutoPlayAudio);
            Assert.Equal("Light", eventSettings.Theme);

            // Verify encrypted key on disk is not plaintext
            string rawJson = File.ReadAllText(tempPath);
            Assert.DoesNotContain("AIzaSySuperSecretKey2026", rawJson);
            Assert.Equal("AIzaSySuperSecretKey2026", service.GetApiKey());
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(null, LiveConfig.DefaultModel)]
    [InlineData("", LiveConfig.DefaultModel)]
    [InlineData("   ", LiveConfig.DefaultModel)]
    [InlineData("invalid/model-name", LiveConfig.DefaultModel)]
    [InlineData("models/gemini-2.5-flash", "models/gemini-2.5-flash")]
    public void SettingsViewModel_SanitizesModelSelection(string? inputModel, string expectedModel)
    {
        Assert.Equal(expectedModel, SettingsViewModel.SanitizeModel(inputModel));
    }

    [Theory]
    [InlineData(null, "Puck")]
    [InlineData("", "Puck")]
    [InlineData("UnknownVoice", "Puck")]
    [InlineData("Charon", "Charon")]
    [InlineData("Aoede", "Aoede")]
    public void SettingsViewModel_SanitizesVoiceSelection(string? inputVoice, string expectedVoice)
    {
        Assert.Equal(expectedVoice, SettingsViewModel.SanitizeVoice(inputVoice));
    }

    #endregion

    #region 5. MainViewModel Integration & Concurrency Tests (20 Tests)

    private static MainViewModel CreateTestMainViewModel(
        out FakeGeminiLiveClient client,
        out FakeAudioCaptureEngine audio,
        out SettingsService settingsService,
        out string tempSettingsPath)
    {
        tempSettingsPath = Path.Combine(Path.GetTempPath(), $"glt_main_{Guid.NewGuid():N}.json");
        settingsService = new SettingsService(tempSettingsPath);
        settingsService.SaveApiKey("AIzaSyValidTestKey123456");

        client = new FakeGeminiLiveClient();
        audio = new FakeAudioCaptureEngine();
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        var dsp = new AudioDspService();

        return new MainViewModel(client, audio, segmenter, settingsService, dsp);
    }

    [Fact]
    public async Task MainViewModel_StartAndStopSession_ManagesClientAndAudioEngineLifecycle()
    {
        using var vm = CreateTestMainViewModel(out var client, out var audio, out _, out string tempPath);
        try
        {
            Assert.False(vm.IsSessionActive);
            Assert.Equal(LiveConnectionState.Disconnected, vm.ConnectionState);

            await vm.StartSessionAsync();

            Assert.True(vm.IsSessionActive);
            Assert.True(vm.IsConnected);
            Assert.Equal(LiveConnectionState.Connected, vm.ConnectionState);
            Assert.True(audio.IsCapturing);
            Assert.NotNull(client.CurrentConfig);
            Assert.Equal("zh-TW", client.CurrentConfig.TargetLanguageCode);

            await vm.StopSessionAsync();

            Assert.False(vm.IsSessionActive);
            Assert.False(vm.IsConnected);
            Assert.Equal(LiveConnectionState.Disconnected, vm.ConnectionState);
            Assert.False(audio.IsCapturing);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MainViewModel_AudioChunkCaptured_CalculatesRmsAndForwardsToGeminiClient()
    {
        using var vm = CreateTestMainViewModel(out var client, out var audio, out _, out string tempPath);
        try
        {
            await vm.StartSessionAsync();

            byte[] pcm16k = SyntheticAudioGenerator.GeneratePcm16Wave(440.0, 16000, 1, 1600, 0.8);
            audio.EmitDualAudioChunk(pcm16k);

            // Verify deduplication: even though both OnAudioChunkCaptured and AudioChunkAvailable fired,
            // only 1 chunk was sent to GeminiLiveClient!
            Assert.Single(client.SentAudioChunks);
            Assert.True(vm.CurrentRmsLevel > 0.1f);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_SpectrumCalculated_DeduplicatesDualEventsAndUpdates36Bands()
    {
        using var vm = CreateTestMainViewModel(out _, out var audio, out _, out string tempPath);
        try
        {
            float[] bands = Enumerable.Repeat(0.75f, 36).ToArray();
            audio.EmitDualSpectrum(bands);

            Assert.Equal(36, vm.CurrentLevels.Count);
            Assert.All(vm.CurrentLevels, b => Assert.Equal(0.75f, b, 2));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_DualTranscriptAndTurnEvents_DeduplicatedIntoSingleTurnCard()
    {
        using var vm = CreateTestMainViewModel(out var client, out _, out _, out string tempPath);
        try
        {
            client.EmitDualSourceTranscript("Good morning everyone");
            client.EmitDualTargetTranscript("大家早安", isFinal: false);
            client.EmitDualTurnCompleted();

            Assert.Single(vm.HistoryCards);
            Assert.Equal("Good morning everyone", vm.HistoryCards[0].SourceText);
            Assert.Equal("大家早安", vm.HistoryCards[0].TargetText);
            Assert.Equal("大家早安", vm.Overlay.TargetText);
            Assert.Equal("Good morning everyone", vm.Overlay.SourceText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_RealGeminiLiveClient_ServerJsonDispatchesSingleCardWithoutDuplication()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_real_{Guid.NewGuid():N}.json");
        try
        {
            using var realClient = new GeminiLiveClient();
            using var audio = new FakeAudioCaptureEngine();
            var settingsService = new SettingsService(tempPath);
            using var vm = new MainViewModel(realClient, audio, new TextSegmentationService(), settingsService, new AudioDspService());

            string json = @"
            {
                ""serverContent"": {
                    ""inputTranscription"": { ""text"": ""How are you doing today?"" },
                    ""outputTranscription"": { ""text"": ""你今天過得好嗎？"", ""isFinal"": true },
                    ""turnComplete"": true
                }
            }";

            realClient.ProcessIncomingServerMessage(json);

            Assert.Single(vm.HistoryCards);
            Assert.Equal("How are you doing today?", vm.HistoryCards[0].SourceText);
            Assert.Equal("你今天過得好嗎？", vm.HistoryCards[0].TargetText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_AltOnlyEvents_SupportedForContractMocks()
    {
        using var vm = CreateTestMainViewModel(out var client, out _, out _, out string tempPath);
        try
        {
            client.EmitAltOnlySourceTranscript("Testing alt source");
            client.EmitAltOnlyTargetTranscript("測試替代目標事件");
            client.EmitDualTurnCompleted();

            Assert.Single(vm.HistoryCards);
            Assert.Equal("Testing alt source", vm.HistoryCards[0].SourceText);
            Assert.Equal("測試替代目標事件", vm.HistoryCards[0].TargetText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void MainViewModel_HardParagraphThreshold_EnforcesOverlaySubtitleMaxLength(int maxChars)
    {
        using var vm = CreateTestMainViewModel(out var client, out _, out _, out string tempPath);
        try
        {
            var settings = vm.CurrentSettings;
            settings.MaxParagraphLength = maxChars;
            vm.ApplySettings(settings);

            string longTranslation = new string('中', 140);
            client.EmitDualTargetTranscript(longTranslation, isFinal: false);

            Assert.True(vm.CurrentTargetTranscript.Length <= maxChars,
                $"Expected CurrentTargetTranscript length <= {maxChars}, got {vm.CurrentTargetTranscript.Length}");
            Assert.True(vm.Overlay.TargetText.Length <= maxChars,
                $"Expected Overlay.TargetText length <= {maxChars}, got {vm.Overlay.TargetText.Length}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MainViewModel_AudioSourceQuickSwitch_TogglesLoopbackAndMicrophoneAndMutesTtsInLoopback()
    {
        using var vm = CreateTestMainViewModel(out var client, out var audio, out _, out string tempPath);
        try
        {
            var settings = vm.CurrentSettings;
            settings.AutoPlayAudio = true;
            vm.ApplySettings(settings);

            await vm.StartSessionAsync();

            // Default is SystemAudioLoopback -> ShouldMutePlayback MUST be true to prevent acoustic feedback!
            Assert.Equal(AudioSource.SystemAudioLoopback, vm.CurrentAudioSource);
            Assert.True(vm.IsSystemAudioLoopback);
            Assert.False(vm.IsMicrophoneSource);
            Assert.True(vm.ShouldMutePlayback);

            // Emit 24kHz TTS audio while in SystemAudioLoopback -> must be muted (0 chunks played)
            client.EmitDualAudioOutput(new byte[4800]);
            Assert.Equal(0, vm.PlayedAudioChunkCount);

            // Switch to Microphone via property setter
            vm.IsMicrophoneSource = true;
            Assert.Equal(AudioSource.Microphone, vm.CurrentAudioSource);
            Assert.Equal(AudioSource.Microphone, audio.CurrentSource);
            Assert.False(vm.ShouldMutePlayback);

            // Emit 24kHz TTS audio while in Microphone mode -> unmuted (1 chunk played)
            client.EmitDualAudioOutput(new byte[4800]);
            Assert.Equal(1, vm.PlayedAudioChunkCount);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_InterruptedEvent_ClearsActiveTurnTranscripts()
    {
        using var vm = CreateTestMainViewModel(out var client, out _, out _, out string tempPath);
        try
        {
            client.EmitDualSourceTranscript("Partial source sentence");
            client.EmitDualTargetTranscript("部分翻譯句子", isFinal: false);
            Assert.NotEmpty(vm.CurrentSourceTranscript);
            Assert.NotEmpty(vm.CurrentTargetTranscript);

            client.EmitDualInterrupted();

            Assert.Equal(string.Empty, vm.CurrentSourceTranscript);
            Assert.Equal(string.Empty, vm.CurrentTargetTranscript);
            Assert.Contains("中斷", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_CopyTurnAndClearHistory_FormatsClipboardStringAndClearsCollection()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            var turn1 = new TranslationTurn("Hello", "你好", true, "en-US", "zh-TW");
            var turn2 = new TranslationTurn("Thank you", "謝謝", true, "en-US", "zh-TW");

            vm.AddTurnCard(turn1);
            vm.AddTurnCard(turn2);
            Assert.Equal(2, vm.HistoryCards.Count);

            vm.CopyTurnCommand.Execute(turn1);
            Assert.Contains("Hello -> 你好", vm.LastCopiedText);

            vm.ClearHistoryCommand.Execute(null);
            Assert.Empty(vm.HistoryCards);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_ThemeSwitch_TogglesDarkAndLightFluent2Palettes()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            Assert.Equal("Dark", vm.Theme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal("#0A0D14", vm.BackgroundBrushHex);
            Assert.Equal("#121622", vm.CardBackgroundBrushHex);

            vm.ToggleThemeCommand.Execute(null);

            Assert.Equal("Light", vm.Theme);
            Assert.False(vm.IsDarkTheme);
            Assert.Equal("#F3F3F3", vm.BackgroundBrushHex);
            Assert.Equal("#FFFFFF", vm.CardBackgroundBrushHex);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_OverlaySynchronization_SyncsVisibilityAndClickThrough()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            Assert.False(vm.IsOverlayVisible);
            Assert.False(vm.IsOverlayClickThrough);

            vm.ToggleOverlayCommand.Execute(null);
            Assert.True(vm.IsOverlayVisible);
            Assert.True(vm.Overlay.IsVisible);

            vm.ToggleOverlayClickThroughCommand.Execute(null);
            Assert.True(vm.IsOverlayClickThrough);
            Assert.True(vm.Overlay.IsClickThrough);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_CreateSettingsViewModel_SavingSettingsPropagatesToMainViewModel()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            var settingsVm = vm.CreateSettingsViewModel();
            settingsVm.SelectedModel = "models/gemini-2.5-flash";
            settingsVm.SelectedVoice = "Aoede";
            settingsVm.SelectedParagraphLength = 100;
            settingsVm.TargetLanguageCode = "de-DE";
            settingsVm.SelectedTheme = "Light";

            settingsVm.Save();

            Assert.Equal("de-DE", vm.TargetLanguageCode);
            Assert.Equal("Light", vm.Theme);
            Assert.Equal("models/gemini-2.5-flash", vm.CurrentSettings.Model);
            Assert.Equal("Aoede", vm.CurrentSettings.Voice);
            Assert.Equal(100, vm.CurrentSettings.MaxParagraphLength);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MainViewModel_ConcurrentTurnAdditionsAndReads_ThreadSafeUnderContention()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            int workerCount = 8;
            int turnsPerWorker = 25;
            var tasks = new Task[workerCount];

            for (int w = 0; w < workerCount; w++)
            {
                int workerId = w;
                tasks[w] = Task.Run(() =>
                {
                    for (int i = 0; i < turnsPerWorker; i++)
                    {
                        vm.AddTurnCard(new TranslationTurn($"Src-{workerId}-{i}", $"Tgt-{workerId}-{i}"));
                    }
                });
            }

            await Task.WhenAll(tasks);

            Assert.Equal(workerCount * turnsPerWorker, vm.HistoryCards.Count);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    #endregion

    #region 6. Fluent 2 XAML & Source Structural Contract Verification Tests (8 Tests)

    [Fact]
    public void MainWindowXaml_ExistsInViewsAndRemovedFromRootStub()
    {
        string viewsMainWindowXaml = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "MainWindow.xaml");
        string viewsMainWindowCs = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "MainWindow.xaml.cs");
        string oldRootStubXaml = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "MainWindow.xaml");

        Assert.True(File.Exists(viewsMainWindowXaml), "Views/MainWindow.xaml must exist.");
        Assert.True(File.Exists(viewsMainWindowCs), "Views/MainWindow.xaml.cs must exist.");
        Assert.False(File.Exists(oldRootStubXaml), "Old stub src/GeminiLiveTranslate.App/MainWindow.xaml must be removed.");
    }

    [Fact]
    public void MainWindowXaml_ContainsVirtualizedHistoryStreamAndSpectrumVisualizer()
    {
        string xamlPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Class=\"GeminiLiveTranslate.App.Views.MainWindow\"", xaml);
        Assert.Contains("controls:SpectrumVisualizer", xaml);
        Assert.Contains("IsVirtualizing=\"True\"", xaml);
        Assert.Contains("VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("ScrollUnit=\"Pixel\"", xaml);
        Assert.Contains("CanContentScroll=\"True\"", xaml);
        Assert.Contains("IsSystemAudioLoopback", xaml);
        Assert.Contains("IsMicrophoneSource", xaml);
        Assert.Contains("SelectedLanguage", xaml);
    }

    [Fact]
    public void MainWindowCodeBehind_AppliesFluent2DwmMicaAndRoundedCorners()
    {
        string csPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "MainWindow.xaml.cs");
        string code = File.ReadAllText(csPath);

        Assert.Contains("DWMWA_USE_IMMERSIVE_DARK_MODE", code);
        Assert.Contains("DWMWA_WINDOW_CORNER_PREFERENCE", code);
        Assert.Contains("DWMWCP_ROUND", code);
        Assert.Contains("DwmExtendFrameIntoClientArea", code);
        Assert.Contains("DWMWA_SYSTEMBACKDROP_TYPE", code);
        Assert.Contains("DWMSBT_MAINWINDOW", code);
        Assert.Contains("FloatingOverlayWindow", code);
        Assert.Contains("SettingsDialog", code);
    }

    [Fact]
    public void SettingsDialogXamlAndCodeBehind_ContainAllRequiredControlsAndBindings()
    {
        string xamlPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "SettingsDialog.xaml");
        string csPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Views", "SettingsDialog.xaml.cs");

        string xaml = File.ReadAllText(xamlPath);
        string cs = File.ReadAllText(csPath);

        Assert.Contains("x:Class=\"GeminiLiveTranslate.App.Views.SettingsDialog\"", xaml);
        Assert.Contains("ApiKeyPasswordBox", xaml);
        Assert.Contains("MaskedApiKey", xaml);
        Assert.Contains("AvailableModels", xaml);
        Assert.Contains("SelectedModel", xaml);
        Assert.Contains("AvailableVoices", xaml);
        Assert.Contains("SelectedVoice", xaml);
        Assert.Contains("SupportedParagraphLengths", xaml);
        Assert.Contains("SelectedParagraphLength", xaml);
        Assert.Contains("AutoPlayAudio", xaml);
        Assert.Contains("DWMSBT_TRANSIENTWINDOW", cs);
    }

    [Fact]
    public void SpectrumVisualizerControl_DefinesDependencyPropertiesAndHardwareRendering()
    {
        string csPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "Controls", "SpectrumVisualizer.cs");
        string code = File.ReadAllText(csPath);

        Assert.Contains("class SpectrumVisualizer : FrameworkElement", code);
        Assert.Contains("DecayRateProperty", code);
        Assert.Contains("RmsLevelProperty", code);
        Assert.Contains("IsActiveProperty", code);
        Assert.Contains("SpectrumDataProperty", code);
        Assert.Contains("DrawRoundedRectangle", code);
        Assert.Contains("CompositionTarget.Rendering", code);
    }

    [Fact]
    public void AppXaml_PointsStartupUriToViewsMainWindow()
    {
        string appXamlPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "App.xaml");
        string appXaml = File.ReadAllText(appXamlPath);

        Assert.Contains("StartupUri=\"Views/MainWindow.xaml\"", appXaml);
        Assert.Contains("Segoe Fluent Icons", appXaml);
    }

    [Fact]
    public async Task MainViewModel_StartSession_WhenClientThrows_TransitionsToErrorStateAndSetsMessage()
    {
        using var vm = CreateTestMainViewModel(out var client, out var audio, out _, out string tempPath);
        try
        {
            client.ThrowOnConnect = true;

            await vm.StartSessionAsync();

            Assert.False(vm.IsSessionActive);
            Assert.Equal(LiveConnectionState.Error, vm.ConnectionState);
            Assert.NotNull(vm.LastErrorMessage);
            Assert.Contains("Simulated WebSocket connection failure", vm.LastErrorMessage);
            Assert.False(audio.IsCapturing);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MainViewModel_StartSession_WithRealClientAndEmptyApiKey_ReportsMissingKeyError()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_nokey_{Guid.NewGuid():N}.json");
        string? prevGeminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        string? prevGoogleKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", null);

            using var realClient = new GeminiLiveClient();
            using var audio = new FakeAudioCaptureEngine();
            var settingsService = new SettingsService(tempPath);
            using var vm = new MainViewModel(realClient, audio, new TextSegmentationService(), settingsService, new AudioDspService());

            await vm.StartSessionAsync();

            Assert.False(vm.IsSessionActive);
            Assert.Equal(LiveConnectionState.Error, vm.ConnectionState);
            Assert.NotNull(vm.LastErrorMessage);
            Assert.Contains("API Key", vm.LastErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", prevGeminiKey);
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", prevGoogleKey);
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_ClientAndAudioCaptureErrors_DeduplicatedAndReflectedInStatusText()
    {
        using var vm = CreateTestMainViewModel(out var client, out var audio, out _, out string tempPath);
        try
        {
            client.EmitDualError("WebSocket 1006 abnormal closure");
            Assert.Equal("WebSocket 1006 abnormal closure", vm.LastErrorMessage);
            Assert.Contains("WebSocket 1006", vm.StatusText);

            audio.EmitDualCaptureError(new InvalidOperationException("WASAPI endpoint disconnected"));
            Assert.Equal("WASAPI endpoint disconnected", vm.LastErrorMessage);
            Assert.Contains("WASAPI endpoint disconnected", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_SwitchAudioSourceCommand_SupportsEnumStringAndToggleModes()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            Assert.Equal(AudioSource.SystemAudioLoopback, vm.CurrentAudioSource);

            // Toggle mode (null parameter)
            vm.SwitchAudioSourceCommand.Execute(null);
            Assert.Equal(AudioSource.Microphone, vm.CurrentAudioSource);

            // String parameter mode
            vm.SwitchAudioSourceCommand.Execute("SystemAudioLoopback");
            Assert.Equal(AudioSource.SystemAudioLoopback, vm.CurrentAudioSource);

            // Enum parameter mode
            vm.SwitchAudioSourceCommand.Execute(AudioSource.Microphone);
            Assert.Equal(AudioSource.Microphone, vm.CurrentAudioSource);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MainViewModel_ToggleSessionCommand_StartsAndStopsSession()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            var asyncCmd = Assert.IsType<AsyncRelayCommand>(vm.ToggleSessionCommand);

            await asyncCmd.ExecuteAsync();
            Assert.True(vm.IsSessionActive);

            await asyncCmd.ExecuteAsync();
            Assert.False(vm.IsSessionActive);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_OpenSettingsCommand_FiresOpenSettingsRequestedEvent()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            bool requested = false;
            vm.OpenSettingsRequested += (_, _) => requested = true;

            vm.OpenSettingsCommand.Execute(null);

            Assert.True(requested);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainViewModel_DecayAllSpectrum_DecaysCurrentLevelsAndNotifies()
    {
        using var vm = CreateTestMainViewModel(out _, out _, out _, out string tempPath);
        try
        {
            vm.UpdateSpectrum(Enumerable.Repeat(0.6f, 36).ToArray());
            Assert.Equal(0.6f, vm.CurrentLevels[0], 2);

            vm.DecayAllSpectrum();
            Assert.True(vm.CurrentLevels[0] < 0.6f);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task SpectrumEngine_ConcurrentUpdateDecayAndGeometry_ThreadSafeWithoutCorruption()
    {
        var engine = new SpectrumEngine();
        var tasks = new List<Task>();

        for (int t = 0; t < 6; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(() =>
            {
                var rng = new Random(threadId * 101);
                for (int i = 0; i < 100; i++)
                {
                    float[] bands = new float[36];
                    for (int b = 0; b < 36; b++)
                    {
                        bands[b] = (float)rng.NextDouble();
                    }
                    engine.UpdateSpectrum(bands);
                    engine.DecayAll();
                    var rects = engine.CalculateBarGeometries(720.0, 68.0);
                    Assert.Equal(36, rects.Count);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.All(engine.CurrentLevels, lvl => Assert.InRange(lvl, 0.0f, 1.0f));
        Assert.All(engine.PeakLevels, pk => Assert.InRange(pk, 0.0f, 1.0f));
    }

    [Theory]
    [InlineData(-10, 50)]
    [InlineData(0, 50)]
    [InlineData(35, 35)]
    [InlineData(50, 50)]
    [InlineData(75, 75)]
    [InlineData(100, 100)]
    public void SettingsViewModel_ParagraphLength_SanitizesInvalidValuesToDefault50(int inputLen, int expectedLen)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"glt_len_{Guid.NewGuid():N}.json");
        try
        {
            var vm = new SettingsViewModel(new SettingsService(tempPath))
            {
                SelectedParagraphLength = inputLen
            };
            Assert.Equal(expectedLen, vm.SelectedParagraphLength);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void OverlayViewModel_VisibilityAndCloseCommands_ToggleAndCloseCleanly()
    {
        var vm = new OverlayViewModel();
        bool? lastVis = null;
        vm.VisibilityChanged += v => lastVis = v;

        vm.Show();
        Assert.True(vm.IsVisible);
        Assert.Equal(true, lastVis);

        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsVisible);
        Assert.Equal(false, lastVis);

        vm.ToggleVisibilityCommand.Execute(null);
        Assert.True(vm.IsVisible);
    }

    #endregion
}
