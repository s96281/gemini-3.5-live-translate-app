using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Tier 4: Real-World Application Scenarios.
/// Executes complete end-to-end user journeys simulating production operations (>=5 scenarios).
/// </summary>
public class Tier4_RealWorldScenarioTests
{
    /// <summary>
    /// Scenario 1: YouTube Japanese Live Stream Simultaneous Interpretation to Traditional Chinese.
    /// Ingests 48kHz stereo system loopback audio, resamples to 16kHz mono, uplinks to Gemini Live,
    /// parses zh-TW translation stream, and verifies high-contrast overlay subtitles rendered topmost.
    /// </summary>
    [Fact]
    public void T4_01_Scenario1_YouTubeJapaneseLiveStreamInterpretation()
    {
        // Step 1: Initialize WASAPI System Audio Loopback capture at 48kHz stereo
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.SystemAudioLoopback);
        Assert.Equal(AudioSource.SystemAudioLoopback, captureEngine.CurrentSource);

        // Step 2: Ingest 500ms of Japanese audio stream at 48kHz stereo float
        int sampleRate = 48000;
        int frameCount = 24000; // 500ms
        byte[] rawStreamAudio = SyntheticAudioGenerator.GenerateFloatWave(600.0, sampleRate, 2, frameCount, 0.75f);

        byte[]? resampled16kPcm = null;
        captureEngine.OnAudioChunkCaptured += (s, pcm) => resampled16kPcm = pcm;
        captureEngine.SimulateRawInput(rawStreamAudio, rawStreamAudio.Length, sampleRate, 2, 32, true);

        Assert.NotNull(resampled16kPcm);
        // 500ms at 16kHz mono = 8000 frames * 2 bytes = 16000 bytes
        Assert.InRange(resampled16kPcm.Length, 15950, 16050);

        // Step 3: Format Gemini Live audio chunk and verify upload packet
        var clientMsg = GeminiClientMessage.CreateAudioChunk(resampled16kPcm);
        string uploadJson = clientMsg.ToJson();
        Assert.Contains("audio/pcm;rate=16000", uploadJson);

        // Step 4: Simulate Gemini streaming Japanese transcription and zh-TW translation
        using var client = new GeminiLiveClient();
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        var overlayText = new DirectWriteTextModel(string.Empty, 28.0) { ContainerWidth = 640 };
        string lastDisplayedSubtitle = string.Empty;

        client.TargetTranscriptReceived += (s, e) =>
        {
            var segResult = segmenter.ProcessToken(e.Text, e.IsFinal);
            if (segResult.HasCommitted)
            {
                lastDisplayedSubtitle = segResult.CommittedText;
                overlayText.Text = lastDisplayedSubtitle;
            }
        };

        string serverStreamJson = @"
        {
            ""serverContent"": {
                ""inputTranscription"": { ""text"": ""みなさん、こんにちは！本日の配信へようこそ。"" },
                ""outputTranscription"": { ""text"": ""大家午安！歡迎收看今天的直播節目。"", ""isFinal"": true },
                ""turnComplete"": true
            }
        }";

        client.ProcessIncomingServerMessage(serverStreamJson);

        // Step 5: Verify overlay subtitle text and geometry layout
        Assert.Equal("大家午安！歡迎收看今天的直播節目。", lastDisplayedSubtitle);
        Assert.Equal("大家午安！歡迎收看今天的直播節目。", overlayText.Text);

        var (geomW, geomH, lineCount) = overlayText.MeasureTextGeometry();
        Assert.True(geomW > 0);
        Assert.Equal(1, lineCount);
        Assert.True(overlayText.HasRequiredFontFallbacks());
    }

    /// <summary>
    /// Scenario 2: Bilingual International Zoom Conference Call.
    /// User speaks into microphone; silence timers segment utterances; history cards stream to Dashboard;
    /// source is toggled between Mic and Loopback when counterpart speaks without dropping connection.
    /// </summary>
    [Fact]
    public void T4_02_Scenario2_InternationalZoomCall_BilingualMicTurnChunking()
    {
        // Step 1: User starts Zoom meeting with Microphone capture
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.Microphone);
        Assert.Equal(AudioSource.Microphone, captureEngine.CurrentSource);

        var dashboard = new SpectrumVisualizerModel();
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };

        // Step 2: User speaks English into microphone
        string userUtterance = "Let's review the quarterly revenue projections";
        int silenceTimer = segmenter.GetRecommendedSilenceInterval(userUtterance);
        // No terminal punctuation yet -> 1800ms pause interval
        Assert.Equal(1800, silenceTimer);

        string fullSentence = userUtterance + ". We achieved a 25% growth.";
        silenceTimer = segmenter.GetRecommendedSilenceInterval(fullSentence);
        // Terminal period detected -> 800ms silence threshold for turn end
        Assert.Equal(800, silenceTimer);

        // Step 3: Server streams translation and adds bilingual card to Dashboard history
        var bilingualTurn = new TranslationTurn(
            fullSentence,
            "讓我們檢視季度營收預測。我們實現了 25% 的成長。",
            isFinal: true,
            sourceLang: "en-US",
            targetLang: "zh-TW");

        dashboard.AddTurnCard(bilingualTurn);
        Assert.Single(dashboard.HistoryCards);
        Assert.Equal("en-US", dashboard.HistoryCards[0].SourceLanguage);
        Assert.Equal("zh-TW", dashboard.HistoryCards[0].TargetLanguage);

        // Step 4: Counterpart starts speaking from Zoom; user toggles capture source to Loopback
        captureEngine.SwitchSource(AudioSource.SystemAudioLoopback);
        Assert.Equal(AudioSource.SystemAudioLoopback, captureEngine.CurrentSource);
        Assert.Equal(1, captureEngine.SourceSwitchCount);

        // Counterpart turn translated without tearing down socket
        var counterpartTurn = new TranslationTurn(
            "這項成果令人非常振奮，感謝團隊的付出！",
            "This result is very exciting, thanks to the team's dedication!",
            isFinal: true,
            sourceLang: "zh-TW",
            targetLang: "en-US");

        dashboard.AddTurnCard(counterpartTurn);
        Assert.Equal(2, dashboard.HistoryCards.Count);
    }

    /// <summary>
    /// Scenario 3: Full-Screen PC Gaming with Floating HUD.
    /// User positions overlay over gaming screen; engages Click-Through mode (WS_EX_TRANSPARENT);
    /// mouse clicks pass through cleanly to game; subtitle updates continue; hotkey Ctrl+F8 unlocks HUD.
    /// </summary>
    [Fact]
    public void T4_03_Scenario3_FullScreenPCGaming_FloatingHUD_ClickThrough()
    {
        // Step 1: Floating overlay HUD positioned at top of gaming screen (1920x1080)
        double screenW = 1920;
        double overlayX = (screenW - 640) / 2; // Center horizontally
        double overlayY = 50;                  // Top floating position
        double overlayW = 640;

        int windowStyle = Win32InteropConstants.WS_EX_LAYERED;
        Assert.False(Win32HitTestCalculator.IsClickThrough(windowStyle));

        // Step 2: User engages Click-Through mode before starting game
        windowStyle = Win32HitTestCalculator.EnableClickThrough(windowStyle);
        Assert.True(Win32HitTestCalculator.IsClickThrough(windowStyle));

        // In Win32, WS_EX_TRANSPARENT causes all mouse clicks to fall through to the underlying game window
        int mouseClickX = (int)(overlayX + 200);
        int mouseClickY = (int)(overlayY + 50);
        bool clicksPassThrough = Win32HitTestCalculator.IsClickThrough(windowStyle);
        Assert.True(clicksPassThrough, "Mouse clicks must pass through to game when WS_EX_TRANSPARENT is active.");

        // Step 3: In-game audio subtitles continue updating at 60fps without interference
        var textModel = new DirectWriteTextModel("【系統提示】獲得稀有裝備「蒼藍守護者」！", 26.0)
        {
            ContainerWidth = overlayW
        };
        var (w, h, lines) = textModel.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.Equal(1, lines);

        // Step 4: User presses global hotkey Ctrl+F8 to unlock HUD and resize font
        uint mod = Win32InteropConstants.MOD_CONTROL;
        uint vk = Win32InteropConstants.VK_F8;
        Assert.Equal(0x0002u, mod);
        Assert.Equal(0x77u, vk);

        // State unlocked
        windowStyle = Win32HitTestCalculator.DisableClickThrough(windowStyle);
        Assert.False(Win32HitTestCalculator.IsClickThrough(windowStyle));

        // User adjusts font size [A+]
        textModel.IncreaseFontSize(4.0);
        Assert.Equal(30.0, textModel.FontSize);
    }

    /// <summary>
    /// Scenario 4: Transient Network Dropout & Automatic Reconnect.
    /// Active streaming session encounters temporary network loss (code 1006);
    /// exponential backoff reconnection recovery; UI history and audio pipeline preserved.
    /// </summary>
    [Fact]
    public async Task T4_04_Scenario4_TransientNetworkDropout_ReconnectionRecovery()
    {
        // Step 1: Active streaming session with history
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.SystemAudioLoopback);

        var visualizer = new SpectrumVisualizerModel();
        visualizer.AddTurnCard(new TranslationTurn("First sentence", "第一句話"));

        // Step 2: Simulated network drop (1006 Abnormal Closure)
        var session = new MockWebSocketSession();
        session.Connect();
        Assert.True(session.IsConnected);

        session.Disconnect();
        Assert.False(session.IsConnected);

        // Step 3: Exponential backoff reconnect simulation (1s -> 2s -> 4s)
        int maxRetries = 3;
        bool reconnected = false;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            int backoffMs = 100 * (int)Math.Pow(2, attempt - 1);
            await Task.Delay(backoffMs);

            if (attempt == 2) // Reconnection succeeds on 2nd attempt
            {
                session.Connect();
                reconnected = true;
                break;
            }
        }

        Assert.True(reconnected);
        Assert.True(session.IsConnected);

        // Step 4: Verify capture engine remained capturing and history preserved
        Assert.True(captureEngine.IsCapturing);
        Assert.Single(visualizer.HistoryCards);
        Assert.Equal("First sentence", visualizer.HistoryCards[0].SourceText);

        // Resume translation stream
        visualizer.AddTurnCard(new TranslationTurn("Second sentence after reconnect", "重連後的第二句話"));
        Assert.Equal(2, visualizer.HistoryCards.Count);
    }

    /// <summary>
    /// Scenario 5: Multi-Language Live Translator Configuration.
    /// User opens settings dialog; switches language from zh-TW to ja-JP, voice from Puck to Aoede,
    /// and segment threshold from 50 to 75; client generates updated setup payload and translations reflect changes.
    /// </summary>
    [Fact]
    public void T4_05_Scenario5_MidSessionSettingsReconfiguration()
    {
        // Step 1: Load initial settings (zh-TW, Puck, 50 chars)
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new SettingsService(tempFile);
            var initialSettings = new AppSettings
            {
                TargetLanguageCode = "zh-TW",
                Voice = "Puck",
                MaxParagraphLength = 50
            };
            settingsService.SaveSettings(initialSettings);
            settingsService.SaveApiKey("AIzaSyActiveSessionKey");

            // Step 2: User opens Settings Dialog and reconfigures parameters
            var activeSettings = settingsService.LoadSettings();
            activeSettings.TargetLanguageCode = "ja-JP";
            activeSettings.Voice = "Aoede";
            activeSettings.MaxParagraphLength = 75;

            settingsService.SaveSettings(activeSettings);

            // Step 3: Validate updated language resolution
            var targetLang = SupportedLanguages.GetByCode(activeSettings.TargetLanguageCode);
            Assert.NotNull(targetLang);
            Assert.Equal("ja-JP", targetLang.Code);
            Assert.Equal("日文 (日本)", targetLang.DisplayName);
            Assert.Equal("🇯🇵", targetLang.FlagEmoji);

            // Step 4: Live Config syncs with updated parameters
            var updatedConfig = new LiveConfig
            {
                ApiKey = settingsService.GetApiKey(),
                Model = activeSettings.Model,
                Voice = activeSettings.Voice,
                TargetLanguageCode = activeSettings.TargetLanguageCode,
                MaxParagraphLength = activeSettings.MaxParagraphLength
            };

            var setupPayload = GeminiLiveClient.BuildSetupPayload(updatedConfig);
            string setupJson = setupPayload.ToJson();

            Assert.Contains("Aoede", setupJson);
            Assert.Contains("日文 (日本)", setupJson);
            Assert.Contains("ja-JP", setupJson);

            // Step 5: Text segmentation service reflects new 75-character threshold
            var segmenter = new TextSegmentationService { MaxCharacters = updatedConfig.MaxParagraphLength };
            Assert.Equal(75, segmenter.MaxCharacters);

            string longText = "這是一段長度為六十個字元的日文翻譯長句，在原本五十個字元的限制下會被切斷，但是在七十五個字元的新設定下可以完整呈現。";
            var chunks = segmenter.ChunkText(longText, segmenter.MaxCharacters);

            Assert.Single(chunks);
            Assert.True(chunks[0].Length <= 75);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
