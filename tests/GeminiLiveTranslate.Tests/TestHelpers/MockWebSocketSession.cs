using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Opaque-box mock session simulating bidirectional WebSocket message exchange with Gemini Live endpoint.
/// </summary>
public class MockWebSocketSession
{
    private readonly List<GeminiClientMessage> _sentClientMessages = new();
    private readonly List<string> _rawSentJson = new();

    public bool IsConnected { get; private set; }
    public IReadOnlyList<GeminiClientMessage> SentClientMessages => _sentClientMessages;
    public IReadOnlyList<string> RawSentJson => _rawSentJson;
    public int AudioChunksSentCount { get; private set; }
    public int TextMessagesSentCount { get; private set; }

    public event Action<string>? OnServerJsonEmitted;

    public void Connect()
    {
        IsConnected = true;
    }

    public void Disconnect()
    {
        IsConnected = false;
    }

    /// <summary>
    /// Records an outbound client message sent over the socket.
    /// </summary>
    public void RecordClientMessage(GeminiClientMessage message)
    {
        if (message == null) return;
        _sentClientMessages.Add(message);
        string json = message.ToJson();
        _rawSentJson.Add(json);

        if (message.RealtimeInput?.MediaChunks != null && message.RealtimeInput.MediaChunks.Count > 0)
        {
            AudioChunksSentCount++;
        }
        if (message.ClientContent?.Turns != null && message.ClientContent.Turns.Count > 0)
        {
            TextMessagesSentCount++;
        }
    }

    /// <summary>
    /// Simulates server sending SetupComplete.
    /// </summary>
    public void EmitSetupComplete()
    {
        string json = @"{ ""setupComplete"": {} }";
        OnServerJsonEmitted?.Invoke(json);
    }

    /// <summary>
    /// Simulates server sending transcription and translated text.
    /// </summary>
    public void EmitBilingualTurn(string sourceTranscript, string targetTranscript, bool isFinal = true, byte[]? pcm24kAudio = null)
    {
        string audioPart = string.Empty;
        if (pcm24kAudio != null && pcm24kAudio.Length > 0)
        {
            string b64 = Convert.ToBase64String(pcm24kAudio);
            audioPart = $@", {{ ""inlineData"": {{ ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""{b64}"" }} }}";
        }

        string json = $@"
        {{
            ""serverContent"": {{
                ""inputTranscription"": {{
                    ""text"": ""{EscapeJson(sourceTranscript)}"",
                    ""isFinal"": {(isFinal ? "true" : "false")}
                }},
                ""outputTranscription"": {{
                    ""text"": ""{EscapeJson(targetTranscript)}"",
                    ""isFinal"": {(isFinal ? "true" : "false")}
                }},
                ""modelTurn"": {{
                    ""parts"": [
                        {{ ""text"": ""{EscapeJson(targetTranscript)}"" }}
                        {audioPart}
                    ]
                }},
                ""turnComplete"": {(isFinal ? "true" : "false")},
                ""interrupted"": false
            }}
        }}";

        OnServerJsonEmitted?.Invoke(json);
    }

    /// <summary>
    /// Simulates server barge-in interruption.
    /// </summary>
    public void EmitInterruption()
    {
        string json = @"{ ""serverContent"": { ""interrupted"": true } }";
        OnServerJsonEmitted?.Invoke(json);
    }

    /// <summary>
    /// Simulates server session expiration notice.
    /// </summary>
    public void EmitGoAway(string timeLeft = "30s")
    {
        string json = $@"{{ ""goAway"": {{ ""timeLeft"": ""{timeLeft}"" }} }}";
        OnServerJsonEmitted?.Invoke(json);
    }

    private static string EscapeJson(string str)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
