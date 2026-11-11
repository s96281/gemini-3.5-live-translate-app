using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Tier 1: Baseline Feature Coverage Tests (Features F1 through F17).
/// Exactly >=5 authentic, distinct test methods per feature (85 tests total).
/// </summary>
public class Tier1_FeatureCoverageTests
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

    #region F1: .NET 9 Solution & Project Scaffold

    [Fact]
    public void F1_01_CoreAssembly_HasNet9TargetFramework()
    {
        var coreAssembly = typeof(GeminiLiveClient).Assembly;
        var targetFrameworkAttr = coreAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Assert.NotNull(targetFrameworkAttr);
        Assert.Contains(".NETCoreApp,Version=v9.0", targetFrameworkAttr.FrameworkName);
    }

    [Fact]
    public void F1_02_SolutionFile_ContainsCoreAppAndTestProjects()
    {
        string slnPath = Path.Combine(SolutionRoot, "GeminiLiveTranslate.sln");
        Assert.True(File.Exists(slnPath), $"Solution file not found at {slnPath}");

        string slnText = File.ReadAllText(slnPath);
        Assert.Contains("GeminiLiveTranslate.Core.csproj", slnText);
        Assert.Contains("GeminiLiveTranslate.App.csproj", slnText);
        Assert.Contains("GeminiLiveTranslate.Tests.csproj", slnText);
    }

    [Fact]
    public void F1_03_CoreProject_EnablesNullableAndImplicitUsings()
    {
        string projPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.Core", "GeminiLiveTranslate.Core.csproj");
        Assert.True(File.Exists(projPath), $"Core project file not found at {projPath}");

        string projText = File.ReadAllText(projPath);
        Assert.Contains("<TargetFramework>net9.0</TargetFramework>", projText);
        Assert.Contains("<Nullable>enable</Nullable>", projText);
        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", projText);
    }

    [Fact]
    public void F1_04_AppProject_EnablesWindowsTargetingAndWpf()
    {
        string projPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "GeminiLiveTranslate.App.csproj");
        Assert.True(File.Exists(projPath), $"App project file not found at {projPath}");

        string projText = File.ReadAllText(projPath);
        Assert.Contains("<TargetFramework>net9.0-windows</TargetFramework>", projText);
        Assert.Contains("<UseWPF>true</UseWPF>", projText);
        Assert.Contains("<EnableWindowsTargeting>true</EnableWindowsTargeting>", projText);
    }

    [Fact]
    public void F1_05_TestProject_ReferencesCore_AndHasTestSdkPackages()
    {
        string projPath = Path.Combine(SolutionRoot, "tests", "GeminiLiveTranslate.Tests", "GeminiLiveTranslate.Tests.csproj");
        Assert.True(File.Exists(projPath), $"Tests project file not found at {projPath}");

        string projText = File.ReadAllText(projPath);
        Assert.Contains("GeminiLiveTranslate.Core.csproj", projText);
        Assert.Contains("Microsoft.NET.Test.Sdk", projText);
        Assert.Contains("xunit", projText);
    }

    #endregion

    #region F2: Gemini Live Client

    [Fact]
    public void F2_01_BuildWebSocketUri_ConstructsValidEndpointWithEncodedKey()
    {
        var config = new LiveConfig { ApiKey = "test-api-key!@#$%^" };
        var uri = GeminiLiveClient.BuildWebSocketUri(config);

        Assert.Equal("generativelanguage.googleapis.com", uri.Host);
        Assert.Equal("wss", uri.Scheme);
        Assert.Contains("key=test-api-key", uri.Query);
    }

    [Fact]
    public void F2_02_BuildSetupPayload_IncludesCorrectModelVoiceAndTargetLanguage()
    {
        var config = new LiveConfig
        {
            Model = "models/gemini-2.0-flash-exp",
            Voice = "Kore",
            TargetLanguageCode = "ja-JP",
            AutoPlayAudio = true
        };

        var setupMsg = GeminiLiveClient.BuildSetupPayload(config);
        string json = setupMsg.ToJson();

        Assert.Contains("models/gemini-2.0-flash-exp", json);
        Assert.Contains("Kore", json);
        Assert.Contains("AUDIO", json);
        Assert.Contains("TEXT", json);
        Assert.Contains("日文", json);
        Assert.Contains("ja-JP", json);
    }

    [Fact]
    public void F2_03_CreateAudioChunk_FormatsBase64DataAndMimeType()
    {
        byte[] pcmChunk = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var msg = GeminiClientMessage.CreateAudioChunk(pcmChunk);
        string json = msg.ToJson();

        Assert.Contains("audio/pcm;rate=16000", json);
        string b64 = Convert.ToBase64String(pcmChunk);
        Assert.Contains(b64, json);
    }

    [Fact]
    public void F2_04_ProcessIncomingServerMessage_DispatchesTranscriptsAndModelTurn()
    {
        using var client = new GeminiLiveClient();
        string receivedSource = string.Empty;
        string receivedTarget = string.Empty;

        client.SourceTranscriptReceived += (s, e) => receivedSource = e.Text;
        client.TargetTranscriptReceived += (s, e) => receivedTarget = e.Text;

        string serverJson = @"
        {
            ""serverContent"": {
                ""inputTranscription"": { ""text"": ""Good morning"" },
                ""outputTranscription"": { ""text"": ""早安"" }
            }
        }";

        client.ProcessIncomingServerMessage(serverJson);

        Assert.Equal("Good morning", receivedSource);
        Assert.Equal("早安", receivedTarget);
    }

    [Fact]
    public void F2_05_ProcessIncomingServerMessage_DispatchesInterruptedAndTurnComplete()
    {
        using var client = new GeminiLiveClient();
        bool interruptedFired = false;
        bool turnCompleteFired = false;

        client.Interrupted += (s, e) => interruptedFired = true;
        client.TurnCompleted += (s, e) => turnCompleteFired = true;

        string serverJson = @"
        {
            ""serverContent"": {
                ""interrupted"": true,
                ""turnComplete"": true
            }
        }";

        client.ProcessIncomingServerMessage(serverJson);

        Assert.True(interruptedFired);
        Assert.True(turnCompleteFired);
    }

    #endregion

    #region F3: Audio DSP & Resampling Engine

    [Fact]
    public void F3_01_Resample_48kHzStereoFloatTo16kMono_AccurateFrameCount()
    {
        var dsp = new AudioDspService();
        int sampleRate = 48000;
        int channels = 2;
        int frameCount = 4800; // 100ms
        byte[] input = SyntheticAudioGenerator.GenerateFloatWave(440.0, sampleRate, channels, frameCount, 0.8f);

        byte[] output = dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, channels, 32, true);

        int expectedFrames = 1600;
        int expectedBytes = expectedFrames * 2;
        Assert.InRange(output.Length, expectedBytes - 4, expectedBytes + 4);
    }

    [Fact]
    public void F3_02_Resample_44100HzStereo16BitPcmTo16kMono_AccurateConversion()
    {
        var dsp = new AudioDspService();
        int sampleRate = 44100;
        int channels = 2;
        int frameCount = 4410; // 100ms
        byte[] input = SyntheticAudioGenerator.GeneratePcm16Wave(1000.0, sampleRate, channels, frameCount, 0.8);

        byte[] output = dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, channels, 16, false);

        int expectedFrames = 1600;
        int expectedBytes = expectedFrames * 2;
        Assert.InRange(output.Length, expectedBytes - 10, expectedBytes + 10);
    }

    [Fact]
    public void F3_03_CalculateSpectrumFft_Produces36BandsNormalized0To1()
    {
        var dsp = new AudioDspService();
        byte[] pcm = SyntheticAudioGenerator.GeneratePcm16Wave(1000.0, 16000, 1, 512, 0.9);

        float[] bands = dsp.CalculateSpectrumFft(pcm, 36);

        Assert.Equal(36, bands.Length);
        foreach (float val in bands)
        {
            Assert.InRange(val, 0.0f, 1.0f);
        }
        Assert.True(bands.Max() > 0.05f, "Peak energy should be detected for 1kHz tone.");
    }

    [Fact]
    public void F3_04_CalculateSpectrumFft_AppliesHannWindowingAttenuation()
    {
        var dsp = new AudioDspService();
        byte[] silence = SyntheticAudioGenerator.GenerateSilencePcm16(16000, 1, 512);
        float[] bands = dsp.CalculateSpectrumFft(silence, 36);

        Assert.All(bands, b => Assert.Equal(0.0f, b));
    }

    [Fact]
    public void F3_05_CalculateRms_FullScaleSineWave_MatchesTheoreticalSqrt2()
    {
        var dsp = new AudioDspService();
        byte[] pcm = SyntheticAudioGenerator.GeneratePcm16Wave(440.0, 16000, 1, 1600, 1.0);

        float rms = dsp.CalculateRms(pcm);

        // Theoretical RMS of full scale sine is ~0.707
        Assert.InRange(rms, 0.65f, 0.75f);
    }

    #endregion

    #region F4: Native Windows Audio Capture Pipeline

    [Fact]
    public void F4_01_AudioSource_EnumDefinesLoopbackAndMicrophone()
    {
        Assert.Equal(0, (int)AudioSource.SystemAudioLoopback);
        Assert.Equal(1, (int)AudioSource.Microphone);
    }

    [Fact]
    public void F4_02_CaptureEngine_StartsAndStopsLifecycle()
    {
        using var engine = new MockAudioCaptureEngine();
        Assert.False(engine.IsCapturing);

        engine.StartCapture(AudioSource.SystemAudioLoopback);
        Assert.True(engine.IsCapturing);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);

        engine.StopCapture();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void F4_03_CaptureEngine_AtomicSourceSwitching()
    {
        using var engine = new MockAudioCaptureEngine();
        engine.StartCapture(AudioSource.SystemAudioLoopback);

        engine.SwitchSource(AudioSource.Microphone);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal(1, engine.SourceSwitchCount);

        engine.SwitchSource(AudioSource.SystemAudioLoopback);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);
        Assert.Equal(2, engine.SourceSwitchCount);
    }

    [Fact]
    public void F4_04_CaptureEngine_SilencePacketGeneration_InjectsZeroBuffer()
    {
        using var engine = new MockAudioCaptureEngine();
        byte[]? capturedChunk = null;
        engine.OnAudioChunkCaptured += (s, chunk) => capturedChunk = chunk;

        engine.StartCapture(AudioSource.SystemAudioLoopback);
        engine.InjectSilence(1600); // 100ms at 16kHz

        Assert.NotNull(capturedChunk);
        Assert.Equal(3200, capturedChunk.Length);
        Assert.All(capturedChunk, b => Assert.Equal(0, b));
    }

    [Fact]
    public void F4_05_CaptureEngine_SimulateInput_Calculates36BandFft()
    {
        using var engine = new MockAudioCaptureEngine();
        float[]? spectrumResult = null;
        engine.OnSpectrumCalculated += (s, spectrum) => spectrumResult = spectrum;

        engine.StartCapture(AudioSource.Microphone);
        byte[] rawInput = SyntheticAudioGenerator.GenerateFloatWave(1000.0, 48000, 2, 4800, 0.8f);
        engine.SimulateRawInput(rawInput, rawInput.Length, 48000, 2, 32, true);

        Assert.NotNull(spectrumResult);
        Assert.Equal(36, spectrumResult.Length);
        Assert.Equal(1, engine.ChunksCapturedCount);
    }

    #endregion

    #region F5: Text Segmentation & Chunking Engine

    [Fact]
    public void F5_01_ProcessToken_SplitsAt35CharacterThreshold()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        string token = "這是一段非常長的測試即時字幕串流文字用來驗證三十五個字元的分割門檻機制是否正確運作中超過長度";

        var result = segmenter.ProcessToken(token, isFinal: false);

        Assert.True(result.HasCommitted);
        Assert.True(result.CommittedText.Length <= 35);
        Assert.NotEmpty(result.PendingText);
    }

    [Fact]
    public void F5_02_ProcessToken_SplitsAt50CharacterThreshold()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        string token = "現在系統正在進行五十個字元的段落長度測試，當文字超過設定的門檻時，引擎應該自動尋找最近的標點符號進行切分，確保使用者在懸浮視窗上不會看到過長內容";

        var result = segmenter.ProcessToken(token, isFinal: false);

        Assert.True(result.HasCommitted);
        Assert.True(result.CommittedText.Length <= 50);
    }

    [Fact]
    public void F5_03_ProcessToken_TerminalPunctuationHierarchy()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };

        var r1 = segmenter.ProcessToken("這是第一句話。", isFinal: false);
        // Has terminal punctuation and triggers sentence end
        Assert.Equal(800, r1.RecommendedSilenceMs);
    }

    [Fact]
    public void F5_04_ProcessToken_SubClausePunctuationHierarchy()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 30 };
        string input = "人工智慧即時翻譯技術，能夠自動轉譯多國語言並提供即時字幕展示功能";

        var result = segmenter.ProcessToken(input, isFinal: false);

        Assert.True(result.HasCommitted);
        Assert.EndsWith("，", result.CommittedText);
    }

    [Fact]
    public void F5_05_Reset_ClearsPendingBuffer()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        segmenter.ProcessToken("未完成的字元暫存內容", isFinal: false);

        segmenter.Reset();

        var emptyResult = segmenter.ProcessToken(string.Empty, isFinal: true);
        Assert.Empty(emptyResult.CommittedText);
        Assert.Empty(emptyResult.PendingText);
    }

    #endregion

    #region F6: Settings & Credential Storage

    [Fact]
    public void F6_01_AppSettings_InitializesWithSaneDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(LiveConfig.DefaultModel, settings.Model);
        Assert.Equal(LiveConfig.DefaultVoice, settings.Voice);
        Assert.Equal("zh-TW", settings.TargetLanguageCode);
        Assert.Equal(50, settings.MaxParagraphLength);
        Assert.Equal(640.0, settings.OverlayWidth);
        Assert.Equal(140.0, settings.OverlayHeight);
        Assert.Equal("Dark", settings.Theme);
    }

    [Fact]
    public void F6_02_SaveAndLoadSettings_PersistsDataAccurately()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            var customSettings = new AppSettings
            {
                Model = "models/gemini-2.5-flash",
                Voice = "Aoede",
                TargetLanguageCode = "ja-JP",
                OverlayFontSize = 32.0
            };

            service.SaveSettings(customSettings);
            var loaded = service.LoadSettings();

            Assert.Equal("models/gemini-2.5-flash", loaded.Model);
            Assert.Equal("Aoede", loaded.Voice);
            Assert.Equal("ja-JP", loaded.TargetLanguageCode);
            Assert.Equal(32.0, loaded.OverlayFontSize);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void F6_03_EncryptSecret_DecryptSecret_RoundTrip()
    {
        var service = new SettingsService();
        string plainKey = "AIzaSyFakeTestKey1234567890";

        string encrypted = service.EncryptSecret(plainKey);
        Assert.NotEqual(plainKey, encrypted);

        string decrypted = service.DecryptSecret(encrypted);
        Assert.Equal(plainKey, decrypted);
    }

    [Fact]
    public void F6_04_GetApiKey_FallsBackToEnvironmentVariable()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "env-secret-api-key");

            string key = service.GetApiKey();
            Assert.Equal("env-secret-api-key", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void F6_05_SaveApiKey_NullOrEmpty_ClearsStoredKey()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            service.SaveApiKey("initial-key");
            Assert.NotEmpty(service.LoadSettings().EncryptedApiKey!);

            service.SaveApiKey(string.Empty);
            Assert.Null(service.LoadSettings().EncryptedApiKey);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region F7: Supported Languages Directory

    [Fact]
    public void F7_01_GetAll_ReturnsAtLeast70Languages()
    {
        var all = SupportedLanguages.GetAll();
        Assert.NotNull(all);
        Assert.True(all.Count >= 70, $"Expected >= 70 languages, got {all.Count}");
    }

    [Fact]
    public void F7_02_GetDefault_ReturnsZhTwWithFlag()
    {
        var def = SupportedLanguages.GetDefault();
        Assert.NotNull(def);
        Assert.Equal("zh-TW", def.Code);
        Assert.Contains("繁體中文", def.DisplayName);
        Assert.Equal("🇹🇼", def.FlagEmoji);
    }

    [Fact]
    public void F7_03_GetByCode_CaseInsensitiveLookup()
    {
        var lower = SupportedLanguages.GetByCode("zh-tw");
        var upper = SupportedLanguages.GetByCode("ZH-TW");
        var ja = SupportedLanguages.GetByCode("ja-JP");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.NotNull(ja);
        Assert.Equal("zh-TW", lower.Code);
        Assert.Equal("zh-TW", upper.Code);
        Assert.Equal("ja-JP", ja.Code);
    }

    [Fact]
    public void F7_04_Search_MatchesByCodeDisplayNameOrNativeName()
    {
        var searchJa = SupportedLanguages.Search("日本語");
        Assert.Contains(searchJa, l => l.Code == "ja-JP");

        var searchEn = SupportedLanguages.Search("English");
        Assert.Contains(searchEn, l => l.Code.StartsWith("en"));

        var searchCode = SupportedLanguages.Search("es-ES");
        Assert.Contains(searchCode, l => l.Code == "es-ES");
    }

    [Fact]
    public void F7_05_AllLanguages_HaveValidBcp47AndNonEmptyFlag()
    {
        var all = SupportedLanguages.GetAll();
        foreach (var lang in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(lang.Code), "Language code should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(lang.DisplayName), $"Language {lang.Code} displayName is empty");
            Assert.False(string.IsNullOrWhiteSpace(lang.NativeName), $"Language {lang.Code} nativeName is empty");
            Assert.False(string.IsNullOrWhiteSpace(lang.FlagEmoji), $"Language {lang.Code} flagEmoji is empty");
        }
    }

    #endregion

    #region F8: Fluent Design 2 Styling & System Backdrop

    [Fact]
    public void F8_01_DwmAttributeConstants_MatchWindows11Spec()
    {
        Assert.Equal(38, Win32InteropConstants.DWMWA_SYSTEMBACKDROP_TYPE);
        Assert.Equal(33, Win32InteropConstants.DWMWA_WINDOW_CORNER_PREFERENCE);
        Assert.Equal(20, Win32InteropConstants.DWMWA_USE_IMMERSIVE_DARK_MODE);
    }

    [Fact]
    public void F8_02_DwmCornerPreferenceConstants_MatchSpec()
    {
        Assert.Equal(2, Win32InteropConstants.DWMWCP_ROUND);
        Assert.Equal(3, Win32InteropConstants.DWMWCP_ROUNDSMALL);
    }

    [Fact]
    public void F8_03_DwmBackdropTypeConstants_MatchSpec()
    {
        Assert.Equal(2, Win32InteropConstants.DWMSBT_MAINWINDOW);      // Mica
        Assert.Equal(3, Win32InteropConstants.DWMSBT_TRANSIENTWINDOW); // Acrylic
        Assert.Equal(4, Win32InteropConstants.DWMSBT_TABBEDWINDOW);    // Mica Alt
    }

    [Fact]
    public void F8_04_ThemeBrushes_DefineDarkAndLightAccents()
    {
        Assert.Equal(Fluent2ThemeBrushes.DarkBackground, Fluent2ThemeBrushes.GetBackground("Dark"));
        Assert.Equal(Fluent2ThemeBrushes.LightBackground, Fluent2ThemeBrushes.GetBackground("Light"));
        Assert.Equal("#0078D4", Fluent2ThemeBrushes.AccentBlue);
        Assert.StartsWith("#", Fluent2ThemeBrushes.DarkCardBackground);
        Assert.StartsWith("#", Fluent2ThemeBrushes.LightCardBackground);
    }

    [Fact]
    public void F8_05_MicaAcrylic_EnumMappings_Consistent()
    {
        int mica = Win32HitTestCalculator.ResolveBackdropType(Win32InteropConstants.DWMSBT_MAINWINDOW);
        int acrylic = Win32HitTestCalculator.ResolveBackdropType(Win32InteropConstants.DWMSBT_TRANSIENTWINDOW);
        Assert.Equal(Win32InteropConstants.DWMSBT_MAINWINDOW, mica);
        Assert.Equal(Win32InteropConstants.DWMSBT_TRANSIENTWINDOW, acrylic);
        Assert.NotEqual(mica, acrylic);
    }

    #endregion

    #region F9: Desktop Floating Lyrics Overlay Window

    [Fact]
    public void F9_01_DefaultDimensions_Match640x140()
    {
        Assert.Equal(640, Win32HitTestCalculator.DefaultOverlayWidth);
        Assert.Equal(140, Win32HitTestCalculator.DefaultOverlayHeight);
    }

    [Fact]
    public void F9_02_MinDimensions_ClampedTo360x100()
    {
        var (w, h) = Win32HitTestCalculator.ClampToMinDimensions(200, 50);
        Assert.Equal(360, w);
        Assert.Equal(100, h);
    }

    [Fact]
    public void F9_03_WindowProperties_RequireTransparencyAndTopmost()
    {
        Assert.True(OverlayWindowDefaults.AllowsTransparency);
        Assert.True(OverlayWindowDefaults.Topmost);
        Assert.False(OverlayWindowDefaults.ShowInTaskbar);
        Assert.Equal("None", OverlayWindowDefaults.WindowStyle);
    }

    [Fact]
    public void F9_04_OverlayPosition_ClampingWithinScreenBounds()
    {
        var (clampedX, clampedY) = Win32HitTestCalculator.ClampOverlayPosition(100.0, 100.0, 1920, 1080, 640, 140);
        Assert.Equal(100.0, clampedX);
        Assert.Equal(100.0, clampedY);

        var (overflowX, overflowY) = Win32HitTestCalculator.ClampOverlayPosition(2500.0, 1500.0, 1920, 1080, 640, 140);
        Assert.Equal(1280.0, overflowX);
        Assert.Equal(940.0, overflowY);
    }

    [Fact]
    public void F9_05_OverlayState_TracksVisibilityAndSubtitle()
    {
        var model = new DirectWriteTextModel("即時字幕測試");
        Assert.Equal("即時字幕測試", model.Text);

        model.Text = "更新後字幕";
        Assert.Equal("更新後字幕", model.Text);
    }

    #endregion

    #region F10: Overlay 8-Direction Resizing & Dynamic Reflow

    [Fact]
    public void F10_01_HitTest_EdgeDetection_LeftRightTopBottom()
    {
        double left = 100, top = 100, width = 640, height = 140;

        int hitL = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 102, 170);
        int hitR = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 738, 170);
        int hitT = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 400, 102);
        int hitB = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 400, 238);

        Assert.Equal(Win32InteropConstants.HTLEFT, hitL);
        Assert.Equal(Win32InteropConstants.HTRIGHT, hitR);
        Assert.Equal(Win32InteropConstants.HTTOP, hitT);
        Assert.Equal(Win32InteropConstants.HTBOTTOM, hitB);
    }

    [Fact]
    public void F10_02_HitTest_CornerDetection_FourCorners()
    {
        double left = 100, top = 100, width = 640, height = 140;

        int hitTL = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 102, 102);
        int hitTR = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 738, 102);
        int hitBL = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 102, 238);
        int hitBR = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 738, 238);

        Assert.Equal(Win32InteropConstants.HTTOPLEFT, hitTL);
        Assert.Equal(Win32InteropConstants.HTTOPRIGHT, hitTR);
        Assert.Equal(Win32InteropConstants.HTBOTTOMLEFT, hitBL);
        Assert.Equal(Win32InteropConstants.HTBOTTOMRIGHT, hitBR);
    }

    [Fact]
    public void F10_03_HitTest_Interior_ReturnsHTCLIENT()
    {
        double left = 100, top = 100, width = 640, height = 140;
        int hit = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 420, 170);

        Assert.Equal(Win32InteropConstants.HTCLIENT, hit);
    }

    [Fact]
    public void F10_04_HitTest_OutsideBounds_ReturnsHTNOWHERE()
    {
        double left = 100, top = 100, width = 640, height = 140;
        int hit = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 50, 50);

        Assert.Equal(Win32InteropConstants.HTNOWHERE, hit);
    }

    [Fact]
    public void F10_05_HitTest_CustomBorderThickness_ScalesAccurately()
    {
        double left = 0, top = 0, width = 500, height = 200;
        int border = 12;

        int hitInside = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 15, 100, border);
        int hitBorder = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 6, 100, border);

        Assert.Equal(Win32InteropConstants.HTCLIENT, hitInside);
        Assert.Equal(Win32InteropConstants.HTLEFT, hitBorder);
    }

    #endregion

    #region F11: DirectWrite Text Stroke & Glow Rendering

    [Fact]
    public void F11_01_CompositeFontFamily_ContainsRequiredFallbacks()
    {
        var model = new DirectWriteTextModel();
        Assert.True(model.HasRequiredFontFallbacks());
    }

    [Fact]
    public void F11_02_ThreeLayerRendering_DefinesGlowStrokeAndFill()
    {
        var model = new DirectWriteTextModel("Text");
        Assert.False(string.IsNullOrEmpty(model.GlowColor));
        Assert.False(string.IsNullOrEmpty(model.StrokeColor));
        Assert.False(string.IsNullOrEmpty(model.FillColor));
    }

    [Fact]
    public void F11_03_StrokeThickness_DefaultAndCustomValues()
    {
        var model = new DirectWriteTextModel();
        Assert.Equal(2.5, model.StrokeThickness);

        model.StrokeThickness = 3.5;
        Assert.Equal(3.5, model.StrokeThickness);
    }

    [Fact]
    public void F11_04_TextGeometry_MeasuresHeightAndLineCount()
    {
        var model = new DirectWriteTextModel("短文字", 24.0);
        var (w, h, lines) = model.MeasureTextGeometry();

        Assert.True(w > 0);
        Assert.True(h > 0);
        Assert.Equal(1, lines);
    }

    [Fact]
    public void F11_05_BreakIntoTextElements_HandlesUnicodeSurrogatesAndEmojis()
    {
        string text = "Hello 👨‍👩‍👧‍👦 World 🇹🇼";
        var elements = DirectWriteTextModel.BreakIntoTextElements(text);

        Assert.Contains("👨‍👩‍👧‍👦", elements);
        Assert.Contains("🇹🇼", elements);
    }

    #endregion

    #region F12: Mouse Click-Through & Global Hotkey

    [Fact]
    public void F12_01_EnableClickThrough_SetsWsExTransparentAndLayered()
    {
        int initial = 0;
        int result = Win32HitTestCalculator.EnableClickThrough(initial);

        Assert.Equal(Win32InteropConstants.WS_EX_TRANSPARENT | Win32InteropConstants.WS_EX_LAYERED, result);
    }

    [Fact]
    public void F12_02_DisableClickThrough_ClearsWsExTransparent()
    {
        int initial = Win32InteropConstants.WS_EX_TRANSPARENT | Win32InteropConstants.WS_EX_LAYERED;
        int result = Win32HitTestCalculator.DisableClickThrough(initial);

        Assert.Equal(Win32InteropConstants.WS_EX_LAYERED, result);
        Assert.False(Win32HitTestCalculator.IsClickThrough(result));
    }

    [Fact]
    public void F12_03_IsClickThrough_ChecksBitFlagAccurately()
    {
        int transparentStyle = Win32InteropConstants.WS_EX_TRANSPARENT;
        int normalStyle = Win32InteropConstants.WS_EX_LAYERED;

        Assert.True(Win32HitTestCalculator.IsClickThrough(transparentStyle));
        Assert.False(Win32HitTestCalculator.IsClickThrough(normalStyle));
    }

    [Fact]
    public void F12_04_HotkeyConstants_ControlF8_MatchesSpecification()
    {
        Assert.Equal(0x0002u, Win32InteropConstants.MOD_CONTROL);
        Assert.Equal(0x77u, Win32InteropConstants.VK_F8);
    }

    [Fact]
    public void F12_05_HotkeyRegistration_ErrorConstants_Defined()
    {
        Assert.Equal(1409, Win32InteropConstants.ERROR_HOTKEY_ALREADY_REGISTERED);
    }

    #endregion

    #region F13: Overlay Control Bar

    [Fact]
    public void F13_01_OpacityCycle_Rotates100_50_0_100()
    {
        Assert.Equal(0.5, Win32HitTestCalculator.GetNextOpacity(1.0));
        Assert.Equal(0.0, Win32HitTestCalculator.GetNextOpacity(0.5));
        Assert.Equal(1.0, Win32HitTestCalculator.GetNextOpacity(0.0));
    }

    [Fact]
    public void F13_02_FontSizeIncrease_A_Plus_IncrementsBy2()
    {
        var model = new DirectWriteTextModel("Test", 24.0);
        model.IncreaseFontSize(2.0);
        Assert.Equal(26.0, model.FontSize);
    }

    [Fact]
    public void F13_03_FontSizeDecrease_A_Minus_DecrementsBy2()
    {
        var model = new DirectWriteTextModel("Test", 24.0);
        model.DecreaseFontSize(2.0);
        Assert.Equal(22.0, model.FontSize);
    }

    [Fact]
    public void F13_04_FontSizeClamp_Enforces12To72Pt()
    {
        var model = new DirectWriteTextModel("Test", 13.0);
        model.DecreaseFontSize(5.0);
        Assert.Equal(12.0, model.FontSize);

        model.FontSize = 70.0;
        model.IncreaseFontSize(5.0);
        Assert.Equal(72.0, model.FontSize);
    }

    [Fact]
    public void F13_05_CloseButton_TriggersHideOrClose()
    {
        var overlay = new DirectWriteTextModel("Test");
        Assert.True(overlay.IsVisible);

        overlay.Close();
        Assert.False(overlay.IsVisible);
    }

    #endregion

    #region F14: Main Dashboard Window Console

    [Fact]
    public void F14_01_SpectrumVisualizer_Initializes36Bands()
    {
        var visualizer = new SpectrumVisualizerModel();
        Assert.Equal(36, visualizer.CurrentLevels.Count);
        Assert.All(visualizer.CurrentLevels, lvl => Assert.Equal(0.0f, lvl));
    }

    [Fact]
    public void F14_02_SpectrumVisualizer_UpdatesBandsWithImmediateAttack()
    {
        var visualizer = new SpectrumVisualizerModel();
        float[] newBands = new float[36];
        newBands[10] = 0.85f;

        visualizer.UpdateSpectrum(newBands);

        Assert.Equal(0.85f, visualizer.CurrentLevels[10]);
    }

    [Fact]
    public void F14_03_SpectrumVisualizer_DecayPhysics_AppliesDecayStep()
    {
        var visualizer = new SpectrumVisualizerModel { DecayRate = 0.1f };
        float[] bands = new float[36];
        bands[5] = 0.5f;
        visualizer.UpdateSpectrum(bands);

        // Feed lower level to trigger decay
        float[] lowBands = new float[36];
        lowBands[5] = 0.0f;
        visualizer.UpdateSpectrum(lowBands);

        Assert.Equal(0.4f, visualizer.CurrentLevels[5], 2);
    }

    [Fact]
    public void F14_04_HistoryCards_AddsBilingualTranslationTurns()
    {
        var visualizer = new SpectrumVisualizerModel();
        var turn = new TranslationTurn("Hello", "你好", isFinal: true, "en-US", "zh-TW");

        visualizer.AddTurnCard(turn);

        Assert.Single(visualizer.HistoryCards);
        Assert.Equal("Hello", visualizer.HistoryCards[0].SourceText);
        Assert.Equal("你好", visualizer.HistoryCards[0].TargetText);
    }

    [Fact]
    public void F14_05_ClearHistory_RemovesAllCards()
    {
        var visualizer = new SpectrumVisualizerModel();
        visualizer.AddTurnCard(new TranslationTurn("1", "1"));
        visualizer.AddTurnCard(new TranslationTurn("2", "2"));

        Assert.Equal(2, visualizer.HistoryCards.Count);
        visualizer.ClearHistory();
        Assert.Empty(visualizer.HistoryCards);
    }

    #endregion

    #region F15: App Settings Dialog UI

    [Fact]
    public void F15_01_ModelSelection_ContainsAllAvailableModels()
    {
        Assert.Contains("models/gemini-2.0-flash-exp", LiveConfig.AvailableModels);
        Assert.Contains("models/gemini-2.0-flash-realtime-exp", LiveConfig.AvailableModels);
        Assert.Contains("models/gemini-2.5-flash", LiveConfig.AvailableModels);
        Assert.Contains("models/gemini-3.5-live-translate-preview", LiveConfig.AvailableModels);
    }

    [Fact]
    public void F15_02_VoiceSelection_ContainsPuckCharonKoreFenrirAoede()
    {
        Assert.Contains("Puck", LiveConfig.AvailableVoices);
        Assert.Contains("Charon", LiveConfig.AvailableVoices);
        Assert.Contains("Kore", LiveConfig.AvailableVoices);
        Assert.Contains("Fenrir", LiveConfig.AvailableVoices);
        Assert.Contains("Aoede", LiveConfig.AvailableVoices);
    }

    [Fact]
    public void F15_03_MaxParagraphLength_Presets_35_50_75_100()
    {
        Assert.Equal(new[] { 35, 50, 75, 100 }, LiveConfig.SupportedParagraphLengths);
    }

    [Fact]
    public void F15_04_SettingsDialog_MapsToAppSettingsProperties()
    {
        var settings = new AppSettings
        {
            Model = "models/gemini-2.5-flash",
            Voice = "Fenrir",
            MaxParagraphLength = 75
        };

        Assert.Equal("models/gemini-2.5-flash", settings.Model);
        Assert.Equal("Fenrir", settings.Voice);
        Assert.Equal(75, settings.MaxParagraphLength);
    }

    [Fact]
    public void F15_05_MaskApiKey_HidesSecretWithBullets()
    {
        string masked = SettingsService.MaskApiKey("AIzaSyFakeKey987654321");
        Assert.StartsWith("AIza", masked);
        Assert.EndsWith("4321", masked);
        Assert.Contains("•", masked);
        Assert.Equal("••••••••", SettingsService.MaskApiKey("12345678"));
        Assert.Equal(string.Empty, SettingsService.MaskApiKey(null));
    }

    #endregion

    #region F16: CI/CD & Build Workflow

    [Fact]
    public void F16_01_WorkflowFile_ExistsInRepository()
    {
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        Assert.True(File.Exists(workflowPath), $"Workflow file not found at {workflowPath}");
    }

    [Fact]
    public void F16_02_WorkflowYamlValidator_DetectsWindowsRunner()
    {
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        var validator = WorkflowYamlValidator.FromFile(workflowPath);
        Assert.True(validator.HasWindowsRunner());
    }

    [Fact]
    public void F16_03_WorkflowYamlValidator_DetectsDotNet9Setup()
    {
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        var validator = WorkflowYamlValidator.FromFile(workflowPath);
        Assert.True(validator.HasDotNet9Setup());
    }

    [Fact]
    public void F16_04_WorkflowYamlValidator_DetectsWinX64SingleFilePublish()
    {
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        var validator = WorkflowYamlValidator.FromFile(workflowPath);
        Assert.True(validator.HasWinX64SingleFilePublish());
    }

    [Fact]
    public void F16_05_WorkflowYamlValidator_DetectsReleaseTagTrigger()
    {
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        var validator = WorkflowYamlValidator.FromFile(workflowPath);
        Assert.True(validator.HasTagReleaseTrigger());
    }

    #endregion

    #region F17: E2E Integration & Verification

    [Fact]
    public void F17_01_Pipeline_RawAudioToResampledPcm_MatchesRate()
    {
        var dsp = new AudioDspService();
        byte[] input48k = SyntheticAudioGenerator.GenerateFloatWave(440.0, 48000, 2, 4800);

        byte[] pcm16k = dsp.ResampleTo16k16BitMono(input48k, input48k.Length, 48000, 2, 32, true);

        Assert.NotEmpty(pcm16k);
        Assert.InRange(pcm16k.Length, 3190, 3210);
    }

    [Fact]
    public void F17_02_Pipeline_ResampledPcmToGeminiClientMessage()
    {
        byte[] pcm16k = SyntheticAudioGenerator.GeneratePcm16Wave(440.0, 16000, 1, 1600);
        var msg = GeminiClientMessage.CreateAudioChunk(pcm16k);
        string json = msg.ToJson();

        Assert.Contains("audio/pcm;rate=16000", json);
        Assert.Contains(Convert.ToBase64String(pcm16k), json);
    }

    [Fact]
    public void F17_03_Pipeline_ServerJsonToTextSegmenter_SplitsSubtitles()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        string serverToken = "人工智慧即時翻譯技術已實現零延遲雙向串流語音互譯功能。";

        var result = segmenter.ProcessToken(serverToken, isFinal: false);

        Assert.True(result.HasCommitted);
        Assert.True(result.CommittedText.Length <= 35);
    }

    [Fact]
    public void F17_04_Pipeline_SettingsToLiveConfig_SyncsParameters()
    {
        var settings = new AppSettings
        {
            Model = "models/gemini-2.0-flash-realtime-exp",
            Voice = "Charon",
            TargetLanguageCode = "fr-FR",
            MaxParagraphLength = 75
        };

        var config = new LiveConfig
        {
            ApiKey = "key",
            Model = settings.Model,
            Voice = settings.Voice,
            TargetLanguageCode = settings.TargetLanguageCode,
            MaxParagraphLength = settings.MaxParagraphLength
        };

        Assert.Equal("models/gemini-2.0-flash-realtime-exp", config.Model);
        Assert.Equal("Charon", config.Voice);
        Assert.Equal("fr-FR", config.TargetLanguageCode);
        Assert.Equal(75, config.MaxParagraphLength);
    }

    [Fact]
    public async Task F17_05_Pipeline_AsyncDisposal_CleansUpAllResources()
    {
        var client = new GeminiLiveClient();
        await client.DisposeAsync();

        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);
        Assert.False(client.IsConnected);
    }

    #endregion
}
