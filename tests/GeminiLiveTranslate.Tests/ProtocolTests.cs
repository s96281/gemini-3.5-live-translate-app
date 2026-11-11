using System;
using System.Text.Json;
using GeminiLiveTranslate.Core.Models;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class ProtocolTests
{
    [Fact]
    public void GeminiClientMessage_CreateSetup_SerializesCorrectly()
    {
        var msg = GeminiClientMessage.CreateSetup(
            model: "models/gemini-2.0-flash-exp",
            voice: "Puck",
            systemInstructionText: "Translate to Traditional Chinese",
            includeAudio: true);

        string json = msg.ToJson();
        Assert.NotNull(json);
        Assert.Contains("models/gemini-2.0-flash-exp", json);
        Assert.Contains("Puck", json);
        Assert.Contains("Translate to Traditional Chinese", json);
        Assert.Contains("AUDIO", json);
        Assert.Contains("TEXT", json);

        // Verify valid JSON deserialization
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("setup", out var setup));
        Assert.Equal("models/gemini-2.0-flash-exp", setup.GetProperty("model").GetString());
        var voiceName = setup.GetProperty("generationConfig")
            .GetProperty("speechConfig")
            .GetProperty("voiceConfig")
            .GetProperty("prebuiltVoiceConfig")
            .GetProperty("voiceName").GetString();
        Assert.Equal("Puck", voiceName);
    }

    [Fact]
    public void GeminiClientMessage_CreateAudioChunk_FormatsBase64Properly()
    {
        byte[] testPcm = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
        var msg = GeminiClientMessage.CreateAudioChunk(testPcm);

        string json = msg.ToJson();
        Assert.NotNull(json);
        Assert.Contains("audio/pcm;rate=16000", json);
        string expectedB64 = Convert.ToBase64String(testPcm);
        Assert.Contains(expectedB64, json);

        using var doc = JsonDocument.Parse(json);
        var mediaChunks = doc.RootElement.GetProperty("realtimeInput").GetProperty("mediaChunks");
        Assert.Equal(1, mediaChunks.GetArrayLength());
        Assert.Equal("audio/pcm;rate=16000", mediaChunks[0].GetProperty("mimeType").GetString());
        Assert.Equal(expectedB64, mediaChunks[0].GetProperty("data").GetString());
    }

    [Fact]
    public void GeminiClientMessage_CreateTextMessage_SerializesTurnProperly()
    {
        var msg = GeminiClientMessage.CreateTextMessage("Hello Gemini", turnComplete: true);
        string json = msg.ToJson();
        Assert.NotNull(json);
        Assert.Contains("Hello Gemini", json);
        Assert.Contains("\"turnComplete\":true", json);

        using var doc = JsonDocument.Parse(json);
        var turns = doc.RootElement.GetProperty("clientContent").GetProperty("turns");
        Assert.Equal("user", turns[0].GetProperty("role").GetString());
        Assert.Equal("Hello Gemini", turns[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void GeminiServerMessage_SetupComplete_DeserializesSuccessfully()
    {
        string json = @"{ ""setupComplete"": {} }";
        var msg = GeminiServerMessage.FromJson(json);

        Assert.NotNull(msg);
        Assert.NotNull(msg.SetupComplete);
        Assert.Null(msg.ServerContent);
        Assert.Null(msg.GoAway);
    }

    [Fact]
    public void GeminiServerMessage_ServerContent_WithModelTurnAudio_DeserializesCorrectly()
    {
        byte[] rawAudio = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        string base64Audio = Convert.ToBase64String(rawAudio);

        string json = $@"
        {{
            ""serverContent"": {{
                ""modelTurn"": {{
                    ""parts"": [
                        {{ ""text"": ""這是一段翻譯"" }},
                        {{
                            ""inlineData"": {{
                                ""mimeType"": ""audio/pcm;rate=24000"",
                                ""data"": ""{base64Audio}""
                            }}
                        }}
                    ]
                }},
                ""turnComplete"": false,
                ""interrupted"": false
            }}
        }}";

        var msg = GeminiServerMessage.FromJson(json);
        Assert.NotNull(msg);
        Assert.NotNull(msg.ServerContent);
        Assert.NotNull(msg.ServerContent.ModelTurn);
        Assert.Equal(2, msg.ServerContent.ModelTurn.Parts!.Count);
        Assert.Equal("這是一段翻譯", msg.ServerContent.ModelTurn.Parts[0].Text);

        var inline = msg.ServerContent.ModelTurn.Parts[1].InlineData;
        Assert.NotNull(inline);
        Assert.Equal("audio/pcm;rate=24000", inline.MimeType);
        byte[] decoded = Convert.FromBase64String(inline.Data!);
        Assert.Equal(rawAudio, decoded);
    }

    [Fact]
    public void GeminiServerMessage_TranscriptionsAndInterrupted_DeserializesCorrectly()
    {
        string json = @"
        {
            ""serverContent"": {
                ""inputTranscription"": {
                    ""text"": ""How is the weather today?"",
                    ""isFinal"": true
                },
                ""outputTranscription"": {
                    ""text"": ""今天天氣如何？"",
                    ""isFinal"": true
                },
                ""interrupted"": true,
                ""turnComplete"": true
            }
        }";

        var msg = GeminiServerMessage.FromJson(json);
        Assert.NotNull(msg);
        Assert.NotNull(msg.ServerContent);
        Assert.Equal("How is the weather today?", msg.ServerContent.InputTranscription?.Text);
        Assert.True(msg.ServerContent.InputTranscription?.IsFinal);
        Assert.Equal("今天天氣如何？", msg.ServerContent.OutputTranscription?.Text);
        Assert.True(msg.ServerContent.OutputTranscription?.IsFinal);
        Assert.True(msg.ServerContent.Interrupted);
        Assert.True(msg.ServerContent.TurnComplete);
    }

    [Fact]
    public void GeminiServerMessage_GoAway_DeserializesCorrectly()
    {
        string json = @"
        {
            ""goAway"": {
                ""timeLeft"": ""15s""
            }
        }";

        var msg = GeminiServerMessage.FromJson(json);
        Assert.NotNull(msg);
        Assert.NotNull(msg.GoAway);
        Assert.Equal("15s", msg.GoAway.TimeLeft);
    }

    [Fact]
    public void GeminiServerMessage_OversizedPayload_HandlesWithoutErrors()
    {
        // Simulate a 128KB audio payload
        byte[] largeData = new byte[128 * 1024];
        new Random(42).NextBytes(largeData);
        string b64 = Convert.ToBase64String(largeData);

        string json = $@"{{ ""serverContent"": {{ ""modelTurn"": {{ ""parts"": [ {{ ""inlineData"": {{ ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""{b64}"" }} }} ] }} }} }}";
        var msg = GeminiServerMessage.FromJson(json);

        Assert.NotNull(msg);
        Assert.NotNull(msg.ServerContent?.ModelTurn?.Parts?[0].InlineData?.Data);
        Assert.Equal(b64.Length, msg.ServerContent.ModelTurn.Parts[0].InlineData!.Data!.Length);
    }

    [Fact]
    public void GeminiServerMessage_EmptyServerContent_DeserializesGracefully()
    {
        string json = @"{ ""serverContent"": {} }";
        var msg = GeminiServerMessage.FromJson(json);

        Assert.NotNull(msg);
        Assert.NotNull(msg.ServerContent);
        Assert.Null(msg.ServerContent.ModelTurn);
        Assert.Null(msg.ServerContent.InputTranscription);
    }

    [Fact]
    public void TranslationTurn_SerializationRoundtrip_MaintainsDataIntegrity()
    {
        var turn = new TranslationTurn("Hello world", "你好，世界", isFinal: true, "en-US", "zh-TW");
        string json = JsonSerializer.Serialize(turn, GeminiJsonContext.DefaultOptions);

        var deserialized = JsonSerializer.Deserialize<TranslationTurn>(json, GeminiJsonContext.DefaultOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(turn.Id, deserialized.Id);
        Assert.Equal(turn.SourceText, deserialized.SourceText);
        Assert.Equal(turn.TargetText, deserialized.TargetText);
        Assert.True(deserialized.IsFinal);
        Assert.Equal("en-US", deserialized.SourceLanguage);
        Assert.Equal("zh-TW", deserialized.TargetLanguage);
    }
}
