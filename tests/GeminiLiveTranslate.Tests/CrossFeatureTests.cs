using System;
using System.IO;
using System.Text.Json;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Tier 3 & Tier 4 Cross-Feature Interaction & Pipeline Scenarios.
/// </summary>
public class CrossFeatureTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public CrossFeatureTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"cross_feature_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }

    [Fact]
    public void Scenario1_AudioCaptureDspToGeminiUplinkPipeline()
    {
        // 1. Simulate 48kHz float stereo captured from WASAPI Loopback
        var dsp = new AudioDspService();
        int sampleRate = 48000;
        int channels = 2;
        int frameCount = 4800; // 100ms
        byte[] rawLoopback = new byte[frameCount * channels * 4];

        for (int i = 0; i < frameCount; i++)
        {
            float s = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.7f;
            byte[] b = BitConverter.GetBytes(s);
            Array.Copy(b, 0, rawLoopback, i * 8, 4);
            Array.Copy(b, 0, rawLoopback, i * 8 + 4, 4);
        }

        // 2. Resample to 16kHz 16-bit mono PCM
        byte[] pcm16k = dsp.ResampleTo16k16BitMono(rawLoopback, rawLoopback.Length, 48000, 2, 32, true);
        Assert.NotEmpty(pcm16k);
        Assert.InRange(pcm16k.Length, 3190, 3210);

        // 3. Compute 36-band FFT spectrum and RMS
        float[] bands = dsp.CalculateSpectrumFft(pcm16k, bandCount: 36);
        float rms = dsp.CalculateRms(pcm16k);

        Assert.Equal(36, bands.Length);
        Assert.True(rms > 0.3f, $"RMS {rms} should reflect strong 440Hz tone");

        // 4. Wrap into GeminiClientMessage audio chunk
        var clientMsg = GeminiClientMessage.CreateAudioChunk(pcm16k);
        string json = clientMsg.ToJson();
        Assert.Contains("audio/pcm;rate=16000", json);
        Assert.Contains(Convert.ToBase64String(pcm16k), json);
    }

    [Fact]
    public void Scenario2_SettingsToClientSetupPipeline()
    {
        // 1. Configure settings with specific model, voice, and language
        var settingsService = new SettingsService(_tempSettingsPath);
        var settings = new AppSettings
        {
            Model = "models/gemini-2.0-flash-realtime-exp",
            Voice = "Fenrir",
            TargetLanguageCode = "de-DE",
            AutoPlayAudio = true,
            MaxParagraphLength = 75
        };
        settingsService.SaveSettings(settings);
        settingsService.SaveApiKey("AIzaSyPipelineKey_987654");

        // 2. Load settings and create LiveConfig
        var loaded = settingsService.LoadSettings();
        string apiKey = settingsService.GetApiKey();

        var config = new LiveConfig(apiKey, loaded.Model, loaded.Voice, loaded.TargetLanguageCode)
        {
            AutoPlayAudio = loaded.AutoPlayAudio,
            MaxParagraphLength = loaded.MaxParagraphLength
        };

        // 3. Verify WebSocket URI and Setup Payload
        var uri = GeminiLiveClient.BuildWebSocketUri(config);
        Assert.Contains("key=AIzaSyPipelineKey_987654", uri.Query);

        var setup = GeminiLiveClient.BuildSetupPayload(config);
        Assert.Equal("models/gemini-2.0-flash-realtime-exp", setup.Setup?.Model);
        Assert.Equal("Fenrir", setup.Setup?.GenerationConfig?.SpeechConfig?.VoiceConfig?.PrebuiltVoiceConfig?.VoiceName);

        var langInfo = SupportedLanguages.GetByCode("de-DE");
        Assert.NotNull(langInfo);
        string instruction = setup.Setup?.SystemInstruction?.Parts?[0].Text ?? "";
        Assert.Contains(langInfo.DisplayName, instruction);
        Assert.Contains("de-DE", instruction);
    }

    [Fact]
    public void Scenario3_DownstreamStreamingToSegmentationAndTurnsPipeline()
    {
        using var client = new GeminiLiveClient();
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        string accumulatedSource = "";
        string accumulatedTarget = "";
        bool turnFinished = false;

        client.SourceTranscriptReceived += (s, e) => accumulatedSource += e.Text;
        client.TargetTranscriptReceived += (s, e) =>
        {
            accumulatedTarget += e.Text;
            segmenter.ProcessToken(e.Text, e.IsFinal);
        };
        client.TurnCompleted += (s, e) => turnFinished = true;

        // Simulate multi-token streaming from Gemini server
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""inputTranscription"": { ""text"": ""Artificial intelligence "" } } }");
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""inputTranscription"": { ""text"": ""is transforming modern medicine."" } } }");

        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""outputTranscription"": { ""text"": ""人工智慧"" } } }");
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""outputTranscription"": { ""text"": ""正在改變現代醫療。"" } } }");
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""turnComplete"": true } }");

        Assert.Equal("Artificial intelligence is transforming modern medicine.", accumulatedSource);
        Assert.Equal("人工智慧正在改變現代醫療。", accumulatedTarget);
        Assert.True(turnFinished);

        // Segment target into subtitles
        var chunks = segmenter.ChunkText(accumulatedTarget, 35);
        Assert.NotEmpty(chunks);
        Assert.Equal("人工智慧正在改變現代醫療。", chunks[0]);

        // Construct TranslationTurn record
        var turn = new TranslationTurn(accumulatedSource, accumulatedTarget, isFinal: true, "en-US", "zh-TW");
        Assert.NotNull(turn.Id);
        Assert.Equal(accumulatedSource, turn.SourceText);
        Assert.Equal(accumulatedTarget, turn.TargetText);
        Assert.True(turn.IsFinal);
    }

    [Fact]
    public void Scenario4_BargeInInterruptionFlushPipeline()
    {
        using var client = new GeminiLiveClient();
        var segmenter = new TextSegmentationService();

        bool interruptedReceived = false;
        client.Interrupted += (s, e) =>
        {
            interruptedReceived = true;
            segmenter.Reset(); // flush pending subtitles on interruption
        };

        // Stream partial token
        segmenter.ProcessToken("正在播報這段被中斷的語音...", isFinal: false);

        // Server signals barge-in interruption
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""interrupted"": true } }");

        Assert.True(interruptedReceived);

        // Verify segmenter pending state was cleared
        var checkResult = segmenter.ProcessToken("", isFinal: false);
        Assert.Empty(checkResult.PendingText);
    }

    [Fact]
    public void Scenario5_SupportedLanguagesSearchAndFallbackPipeline()
    {
        // Search by query
        var searchResults = SupportedLanguages.Search("Korean");
        // Korean is "韓文 (韓國)" / "한국어" / "ko-KR"
        var koResults = SupportedLanguages.Search("ko-KR");
        Assert.Single(koResults);
        Assert.Equal("ko-KR", koResults[0].Code);
        Assert.Equal("🇰🇷", koResults[0].FlagEmoji);

        // Fallback for non-existent language
        var fallback = SupportedLanguages.GetByCodeOrDefault("invalid-xyz");
        Assert.Equal("zh-TW", fallback.Code);
        Assert.Equal("🇹🇼", fallback.FlagEmoji);
    }
}
