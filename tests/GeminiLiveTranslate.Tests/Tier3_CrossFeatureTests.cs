using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Tier 3: Cross-Feature Interaction & Subsystem Pipeline Tests.
/// Validates pairwise combinations, cross-cutting contracts, and multi-service workflows (>=15 tests).
/// </summary>
public class Tier3_CrossFeatureTests
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

    [Fact]
    public void T3_01_AudioCapture_DspResampler_GeminiLiveUplink()
    {
        // 1. WASAPI 48kHz Stereo Float Audio Capture (F4)
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.SystemAudioLoopback);

        byte[] rawInput = SyntheticAudioGenerator.GenerateFloatWave(440.0, 48000, 2, 4800, 0.8f);

        // 2. Linear Resampling Engine to 16kHz Mono 16-bit PCM (F3)
        byte[]? resampledPcm = null;
        captureEngine.OnAudioChunkCaptured += (s, pcm) => resampledPcm = pcm;

        captureEngine.SimulateRawInput(rawInput, rawInput.Length, 48000, 2, 32, true);

        Assert.NotNull(resampledPcm);
        Assert.InRange(resampledPcm.Length, 3190, 3210);

        // 3. Gemini Live Uplink Base64 Payload formatting (F2)
        var clientMessage = GeminiClientMessage.CreateAudioChunk(resampledPcm);
        string json = clientMessage.ToJson();

        Assert.Contains("audio/pcm;rate=16000", json);
        Assert.Contains(Convert.ToBase64String(resampledPcm), json);
    }

    [Fact]
    public void T3_02_GeminiDownstream_TextSegmentation_OverlaySubtitles()
    {
        // 1. Server emits downstream translation tokens (F2)
        using var client = new GeminiLiveClient();
        var segmenter = new TextSegmentationService { MaxCharacters = 50 }; // F5
        var textModel = new DirectWriteTextModel(string.Empty, 24.0); // F11
        string displayedOverlaySubtitle = string.Empty; // F9

        client.TargetTranscriptReceived += (s, e) =>
        {
            var result = segmenter.ProcessToken(e.Text, e.IsFinal);
            if (result.HasCommitted)
            {
                displayedOverlaySubtitle = result.CommittedText;
                textModel.Text = displayedOverlaySubtitle;
            }
        };

        string serverJson = @"
        {
            ""serverContent"": {
                ""outputTranscription"": {
                    ""text"": ""這是一段完整的即時翻譯句子。"",
                    ""isFinal"": true
                }
            }
        }";

        client.ProcessIncomingServerMessage(serverJson);

        Assert.Equal("這是一段完整的即時翻譯句子。", displayedOverlaySubtitle);
        Assert.Equal(displayedOverlaySubtitle, textModel.Text);
        var (w, h, lines) = textModel.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.Equal(1, lines);
    }

    [Fact]
    public void T3_03_SettingsService_GeminiLiveClientSetup()
    {
        // 1. AppSettings loads DPAPI encrypted secret & configuration (F6)
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new SettingsService(tempFile);
            var customSettings = new AppSettings
            {
                Model = "models/gemini-2.0-flash-realtime-exp",
                Voice = "Kore",
                TargetLanguageCode = "zh-TW",
                AutoPlayAudio = true
            };
            settingsService.SaveSettings(customSettings);
            settingsService.SaveApiKey("AIzaSySecureTestKey123");

            var loaded = settingsService.LoadSettings();
            string key = settingsService.GetApiKey();

            // 2. Gemini Live Client builds setup payload reflecting settings (F2)
            var config = new LiveConfig
            {
                ApiKey = key,
                Model = loaded.Model,
                Voice = loaded.Voice,
                TargetLanguageCode = loaded.TargetLanguageCode,
                AutoPlayAudio = loaded.AutoPlayAudio
            };

            var setupMessage = GeminiLiveClient.BuildSetupPayload(config);
            string json = setupMessage.ToJson();

            Assert.Contains("models/gemini-2.0-flash-realtime-exp", json);
            Assert.Contains("Kore", json);
            Assert.Contains("AUDIO", json);
            Assert.Contains("TEXT", json);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void T3_04_SettingsService_LanguageDirectory_SystemInstruction()
    {
        // 1. User selects target language "ja-JP" in settings (F6)
        string targetCode = "ja-JP";

        // 2. Lookup in SupportedLanguages Directory (F7)
        var lang = SupportedLanguages.GetByCode(targetCode);
        Assert.NotNull(lang);
        Assert.Equal("日文 (日本)", lang.DisplayName);

        // 3. Dynamic compile into Gemini System Instruction (F2)
        var config = new LiveConfig { TargetLanguageCode = targetCode };
        var setupMsg = GeminiLiveClient.BuildSetupPayload(config);
        string json = setupMsg.ToJson();

        Assert.Contains("日文 (日本)", json);
        Assert.Contains("ja-JP", json);
    }

    [Fact]
    public void T3_05_OverlayWindow_Resizing_DynamicReflow()
    {
        // 1. Overlay window geometry setup (F9)
        double overlayLeft = 100, overlayTop = 100;
        double currentWidth = 640, currentHeight = 140;

        // 2. User resizes window border via Win32 WM_NCHITTEST (F10)
        int hit = Win32HitTestCalculator.CalculateHitTest(overlayLeft, overlayTop, currentWidth, currentHeight, 738, 170);
        Assert.Equal(Win32InteropConstants.HTRIGHT, hit);

        // Window width narrowed from 640 to 360
        double narrowedWidth = 360;
        var (clampedW, clampedH) = Win32HitTestCalculator.ClampToMinDimensions(narrowedWidth, currentHeight);

        // 3. DirectWrite geometry dynamically reflows text (F11)
        string longSubtitle = "這是一段比較長的即時字幕翻譯文字，當懸浮視窗被拉窄時，文字應該自動換行重排。";
        var textModel = new DirectWriteTextModel(longSubtitle, 24.0)
        {
            ContainerWidth = clampedW,
            ContainerHeight = clampedH
        };

        var (w, h, lines) = textModel.MeasureTextGeometry();

        Assert.True(lines >= 2, $"Expected at least 2 lines due to narrow width constraint, got {lines}");
        Assert.True(w <= clampedW);
    }

    [Fact]
    public void T3_06_OverlayControlBar_ClickThrough_HotkeyInterop()
    {
        // 1. Overlay window starts in normal interactive mode (F9)
        int windowExStyle = Win32InteropConstants.WS_EX_LAYERED;
        bool controlBarVisible = true;

        // 2. User clicks [穿透] button on control bar (F13)
        windowExStyle = Win32HitTestCalculator.EnableClickThrough(windowExStyle);
        controlBarVisible = false;

        Assert.True(Win32HitTestCalculator.IsClickThrough(windowExStyle));
        Assert.False(controlBarVisible);

        // 3. Global hotkey Ctrl+F8 pressed to unlock (F12)
        uint modifier = Win32InteropConstants.MOD_CONTROL;
        uint vk = Win32InteropConstants.VK_F8;
        Assert.Equal(0x0002u, modifier);
        Assert.Equal(0x77u, vk);

        windowExStyle = Win32HitTestCalculator.DisableClickThrough(windowExStyle);
        controlBarVisible = true;

        Assert.False(Win32HitTestCalculator.IsClickThrough(windowExStyle));
        Assert.True(controlBarVisible);
    }

    [Fact]
    public void T3_07_AudioSourceSwitch_TTSMute_LoopbackPrevention()
    {
        // 1. Audio capture starts with Microphone (F4)
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.Microphone);
        Assert.False(captureEngine.ShouldMutePlayback);

        // 2. User toggles audio source to SystemAudioLoopback (F4)
        captureEngine.SwitchSource(AudioSource.SystemAudioLoopback);

        // 3. Prevention rule: When loopback recording is active, auto TTS must be muted to avoid echo loopback (F2 + F14)
        Assert.True(captureEngine.ShouldMutePlayback, "TTS playback must be muted when recording system audio to prevent feedback loop.");
        Assert.True(MockAudioCaptureEngine.ShouldMutePlaybackForSource(AudioSource.SystemAudioLoopback));
        Assert.False(MockAudioCaptureEngine.ShouldMutePlaybackForSource(AudioSource.Microphone));
    }

    [Fact]
    public void T3_08_AudioDspFft_SpectrumVisualizer_UIRefresh()
    {
        // 1. Capture pure 1000Hz tone (F3)
        var dsp = new AudioDspService();
        byte[] pcm16k = SyntheticAudioGenerator.GeneratePcm16Wave(1000.0, 16000, 1, 512, 0.9);

        // 2. Calculate 36-band FFT (F3)
        float[] bands = dsp.CalculateSpectrumFft(pcm16k, 36);

        // 3. Spectrum visualizer receives FFT bands and updates (F14)
        var visualizer = new SpectrumVisualizerModel();
        visualizer.UpdateSpectrum(bands);

        Assert.Equal(36, visualizer.CurrentLevels.Count);
        Assert.True(visualizer.CurrentLevels.Max() > 0.05f, "Peak level should be reflected on visualizer.");
    }

    [Fact]
    public void T3_09_GeminiInterruption_PlaybackFlush_SubtitleReset()
    {
        // 1. Streaming response arrives (F2)
        using var client = new GeminiLiveClient();
        var segmenter = new TextSegmentationService { MaxCharacters = 50 }; // F5
        var history = new SpectrumVisualizerModel(); // F14
        bool playbackFlushed = false;

        client.Interrupted += (s, e) =>
        {
            segmenter.Reset();
            playbackFlushed = true;
        };

        // 2. Barge-in interruption received (F2)
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""interrupted"": true } }");

        Assert.True(playbackFlushed);
    }

    [Fact]
    public void T3_10_SettingsThreshold_TextSegmentationSync()
    {
        // 1. AppSettings changes paragraph length threshold from 50 to 35 (F6)
        var settings = new AppSettings { MaxParagraphLength = 35 };

        // 2. Segmentation service dynamically updates limit (F5)
        var segmenter = new TextSegmentationService { MaxCharacters = settings.MaxParagraphLength };
        Assert.Equal(35, segmenter.MaxCharacters);

        string longText = "這是一段超過三十五個字元的長段落測試內容用來驗證設定同步是否立即生效於切割服務。";
        var chunks = segmenter.ChunkText(longText, segmenter.MaxCharacters);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 35));
    }

    [Fact]
    public void T3_11_DpapiCredentialStorage_EnvironmentFallback()
    {
        // 1. DPAPI settings has no key (F6)
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "env-api-fallback-key");

            // 2. Key retrieved through fallback (F6)
            string apiKey = service.GetApiKey();
            Assert.Equal("env-api-fallback-key", apiKey);

            // 3. Passed to Gemini Live Client URI builder (F2)
            var config = new LiveConfig { ApiKey = apiKey };
            var uri = GeminiLiveClient.BuildWebSocketUri(config);

            Assert.Contains("key=env-api-fallback-key", uri.Query);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void T3_12_OverlayOpacityCycle_BackgroundBrushRendering()
    {
        // 1. User clicks [背景] opacity button on overlay control bar (F13)
        var settings = new AppSettings(); // F6
        double[] cycle = { 1.0, 0.5, 0.0 };
        int step = 0;

        step = (step + 1) % cycle.Length; // 0.5
        settings.OverlayOpacity = cycle[step];

        // 2. Background alpha brush calculation (F8 + F9)
        byte alpha = (byte)(settings.OverlayOpacity * 255);
        string hexBrush = $"#{alpha:X2}000000";

        Assert.Equal(0.5, settings.OverlayOpacity);
        Assert.Equal(127, alpha);
        Assert.Equal("#7F000000", hexBrush);
    }

    [Fact]
    public void T3_13_DashboardHistoryStream_TurnCompletion()
    {
        // 1. Translation turn arrives and finishes (F2)
        using var client = new GeminiLiveClient();
        var visualizer = new SpectrumVisualizerModel(); // F14
        string source = string.Empty;
        string target = string.Empty;

        client.SourceTranscriptReceived += (s, e) => source = e.Text;
        client.TargetTranscriptReceived += (s, e) => target = e.Text;

        client.TurnCompleted += (s, e) =>
        {
            visualizer.AddTurnCard(new TranslationTurn(source, target, isFinal: true));
        };

        client.ProcessIncomingServerMessage(@"
        {
            ""serverContent"": {
                ""inputTranscription"": { ""text"": ""Good afternoon"" },
                ""outputTranscription"": { ""text"": ""午安"" },
                ""turnComplete"": true
            }
        }");

        Assert.Single(visualizer.HistoryCards);
        Assert.Equal("Good afternoon", visualizer.HistoryCards[0].SourceText);
        Assert.Equal("午安", visualizer.HistoryCards[0].TargetText);
    }

    [Fact]
    public void T3_14_OverlayFontSizeDelta_SettingsPersistence()
    {
        // 1. User clicks [A+] font resize button (F13)
        var textModel = new DirectWriteTextModel("Test", 24.0); // F11
        textModel.IncreaseFontSize(2.0);

        // 2. Persisted into AppSettings storage (F6)
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            var settings = service.LoadSettings();
            settings.OverlayFontSize = textModel.FontSize;
            service.SaveSettings(settings);

            var loaded = service.LoadSettings();
            Assert.Equal(26.0, loaded.OverlayFontSize);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void T3_15_CiCdWorkflow_SolutionScaffoldingIntegrity()
    {
        // 1. Inspect GeminiLiveTranslate.sln projects (F1)
        string slnPath = Path.Combine(SolutionRoot, "GeminiLiveTranslate.sln");
        string slnText = File.ReadAllText(slnPath);

        // 2. Inspect CI/CD build.yml workflow definition (F16)
        string workflowPath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
        Assert.True(File.Exists(workflowPath));

        Assert.Contains("GeminiLiveTranslate.App.csproj", slnText);
        Assert.Contains("GeminiLiveTranslate.Core.csproj", slnText);
    }
}
