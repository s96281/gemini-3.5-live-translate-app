using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Top-level client message sent over Gemini Live WebSocket.
/// </summary>
public class GeminiClientMessage
{
    [JsonPropertyName("setup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiSetupPayload? Setup { get; set; }

    [JsonPropertyName("realtimeInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiRealtimeInput? RealtimeInput { get; set; }

    [JsonPropertyName("clientContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiClientContent? ClientContent { get; set; }

    public static GeminiClientMessage CreateSetup(
        string model,
        string voice,
        string systemInstructionText,
        bool includeAudio = true)
    {
        var modalities = includeAudio
            ? new List<string> { "AUDIO", "TEXT" }
            : new List<string> { "TEXT" };

        return new GeminiClientMessage
        {
            Setup = new GeminiSetupPayload
            {
                Model = model,
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseModalities = modalities,
                    SpeechConfig = new GeminiSpeechConfig
                    {
                        VoiceConfig = new GeminiVoiceConfig
                        {
                            PrebuiltVoiceConfig = new GeminiPrebuiltVoiceConfig
                            {
                                VoiceName = voice
                            }
                        }
                    }
                },
                SystemInstruction = new GeminiContent
                {
                    Parts = new List<GeminiPart>
                    {
                        new GeminiPart { Text = systemInstructionText }
                    }
                }
            }
        };
    }

    public static GeminiClientMessage CreateAudioChunk(byte[] pcm16kBytes)
    {
        string base64Data = Convert.ToBase64String(pcm16kBytes);
        return new GeminiClientMessage
        {
            RealtimeInput = new GeminiRealtimeInput
            {
                MediaChunks = new List<GeminiMediaChunk>
                {
                    new GeminiMediaChunk
                    {
                        MimeType = "audio/pcm;rate=16000",
                        Data = base64Data
                    }
                }
            }
        };
    }

    public static GeminiClientMessage CreateTextMessage(string text, bool turnComplete = true)
    {
        return new GeminiClientMessage
        {
            ClientContent = new GeminiClientContent
            {
                Turns = new List<GeminiTurn>
                {
                    new GeminiTurn
                    {
                        Role = "user",
                        Parts = new List<GeminiPart>
                        {
                            new GeminiPart { Text = text }
                        }
                    }
                },
                TurnComplete = turnComplete
            }
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, GeminiJsonContext.DefaultOptions);
    }
}

public class GeminiSetupPayload
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("generationConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiGenerationConfig? GenerationConfig { get; set; }

    [JsonPropertyName("systemInstruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiContent? SystemInstruction { get; set; }
}

public class GeminiGenerationConfig
{
    [JsonPropertyName("responseModalities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ResponseModalities { get; set; }

    [JsonPropertyName("speechConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiSpeechConfig? SpeechConfig { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }
}

public class GeminiSpeechConfig
{
    [JsonPropertyName("voiceConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiVoiceConfig? VoiceConfig { get; set; }
}

public class GeminiVoiceConfig
{
    [JsonPropertyName("prebuiltVoiceConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiPrebuiltVoiceConfig? PrebuiltVoiceConfig { get; set; }
}

public class GeminiPrebuiltVoiceConfig
{
    [JsonPropertyName("voiceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VoiceName { get; set; }
}

public class GeminiContent
{
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GeminiPart>? Parts { get; set; }
}

public class GeminiPart
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("inlineData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; set; }
}

public class GeminiInlineData
{
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }
}

public class GeminiRealtimeInput
{
    [JsonPropertyName("mediaChunks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GeminiMediaChunk>? MediaChunks { get; set; }
}

public class GeminiMediaChunk
{
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }
}

public class GeminiClientContent
{
    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GeminiTurn>? Turns { get; set; }

    [JsonPropertyName("turnComplete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TurnComplete { get; set; }
}

public class GeminiTurn
{
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GeminiPart>? Parts { get; set; }
}

/// <summary>
/// Top-level server message received from Gemini Live WebSocket.
/// </summary>
public class GeminiServerMessage
{
    [JsonPropertyName("setupComplete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiSetupComplete? SetupComplete { get; set; }

    [JsonPropertyName("serverContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiServerContent? ServerContent { get; set; }

    [JsonPropertyName("toolCall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ToolCall { get; set; }

    [JsonPropertyName("toolCallCancellation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ToolCallCancellation { get; set; }

    [JsonPropertyName("goAway")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiGoAway? GoAway { get; set; }

    public static GeminiServerMessage? FromJson(string json)
    {
        return JsonSerializer.Deserialize<GeminiServerMessage>(json, GeminiJsonContext.DefaultOptions);
    }
}

public class GeminiSetupComplete
{
}

public class GeminiServerContent
{
    [JsonPropertyName("modelTurn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiContent? ModelTurn { get; set; }

    [JsonPropertyName("turnComplete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TurnComplete { get; set; }

    [JsonPropertyName("interrupted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Interrupted { get; set; }

    [JsonPropertyName("inputTranscription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiTranscription? InputTranscription { get; set; }

    [JsonPropertyName("outputTranscription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiTranscription? OutputTranscription { get; set; }
}

public class GeminiTranscription
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("isFinal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsFinal { get; set; }
}

public class GeminiGoAway
{
    [JsonPropertyName("timeLeft")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimeLeft { get; set; }
}

public static class GeminiJsonContext
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };
}
