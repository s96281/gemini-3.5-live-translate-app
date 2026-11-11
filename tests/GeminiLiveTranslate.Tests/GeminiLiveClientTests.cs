using System;
using System.Text.Json;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class GeminiLiveClientTests
{
    [Fact]
    public void BuildWebSocketUri_FormatsEndpointAndQueryParameterProperly()
    {
        var config = new LiveConfig("AIzaSyTest_Key_123");
        var uri = GeminiLiveClient.BuildWebSocketUri(config);

        Assert.NotNull(uri);
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("generativelanguage.googleapis.com", uri.Host);
        Assert.Contains("key=AIzaSyTest_Key_123", uri.Query);
    }

    [Fact]
    public void BuildWebSocketUri_NullOrEmptyApiKey_ThrowsArgumentException()
    {
        var emptyConfig = new LiveConfig("");
        Assert.Throws<ArgumentException>(() => GeminiLiveClient.BuildWebSocketUri(emptyConfig));
    }

    [Fact]
    public void BuildSetupPayload_ContainsTargetLanguageAndVoiceSettings()
    {
        var config = new LiveConfig("dummy-key", "models/gemini-2.0-flash-exp", "Kore", "ja-JP")
        {
            AutoPlayAudio = true
        };

        var setupMsg = GeminiLiveClient.BuildSetupPayload(config);
        Assert.NotNull(setupMsg.Setup);
        Assert.Equal("models/gemini-2.0-flash-exp", setupMsg.Setup.Model);

        var voiceName = setupMsg.Setup.GenerationConfig?.SpeechConfig?.VoiceConfig?.PrebuiltVoiceConfig?.VoiceName;
        Assert.Equal("Kore", voiceName);

        var sysInstruction = setupMsg.Setup.SystemInstruction?.Parts?[0].Text;
        Assert.NotNull(sysInstruction);
        Assert.Contains("日文", sysInstruction);
        Assert.Contains("ja-JP", sysInstruction);
    }

    [Fact]
    public void InitialState_IsDisconnected()
    {
        using var client = new GeminiLiveClient();
        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void ProcessIncomingServerMessage_DispatchesTranscriptionEvents()
    {
        using var client = new GeminiLiveClient();

        string? receivedSource = null;
        string? receivedTarget = null;
        string? altSource = null;
        string? altTarget = null;

        client.SourceTranscriptReceived += (s, e) => receivedSource = e.Text;
        client.TargetTranscriptReceived += (s, e) => receivedTarget = e.Text;
        client.OnInputTranscriptionReceived += (s, e) => altSource = e;
        client.OnOutputTranscriptionReceived += (s, e) => altTarget = e;

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
        Assert.Equal("Good morning", altSource);
        Assert.Equal("早安", altTarget);
    }

    [Fact]
    public void ProcessIncomingServerMessage_DispatchesModelTurnAudioAndText()
    {
        using var client = new GeminiLiveClient();

        byte[] originalPcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        string base64 = Convert.ToBase64String(originalPcm);

        byte[]? receivedAudio = null;
        string? receivedText = null;

        client.AudioOutputReceived += (s, e) => receivedAudio = e.Pcm24kData;
        client.TargetTranscriptReceived += (s, e) => receivedText = e.Text;

        string serverJson = $@"
        {{
            ""serverContent"": {{
                ""modelTurn"": {{
                    ""parts"": [
                        {{ ""text"": ""Hello world"" }},
                        {{
                            ""inlineData"": {{
                                ""mimeType"": ""audio/pcm;rate=24000"",
                                ""data"": ""{base64}""
                            }}
                        }}
                    ]
                }}
            }}
        }}";

        client.ProcessIncomingServerMessage(serverJson);

        Assert.Equal("Hello world", receivedText);
        Assert.NotNull(receivedAudio);
        Assert.Equal(originalPcm, receivedAudio);
    }

    [Fact]
    public void ProcessIncomingServerMessage_DispatchesInterruptedAndTurnComplete()
    {
        using var client = new GeminiLiveClient();

        bool interrupted = false;
        bool turnComplete = false;
        bool altInterrupted = false;
        bool altTurnComplete = false;

        client.Interrupted += (s, e) => interrupted = true;
        client.TurnCompleted += (s, e) => turnComplete = true;
        client.OnInterruptedReceived += (s, e) => altInterrupted = e;
        client.OnTurnCompletedReceived += (s, e) => altTurnComplete = e;

        string serverJson = @"
        {
            ""serverContent"": {
                ""interrupted"": true,
                ""turnComplete"": true
            }
        }";

        client.ProcessIncomingServerMessage(serverJson);

        Assert.True(interrupted);
        Assert.True(turnComplete);
        Assert.True(altInterrupted);
        Assert.True(altTurnComplete);
    }

    [Fact]
    public void ProcessIncomingServerMessage_MalformedJson_FiresErrorEvent()
    {
        using var client = new GeminiLiveClient();

        string? errorMsg = null;
        client.ErrorOccurred += (s, e) => errorMsg = e;

        client.ProcessIncomingServerMessage("{{{ invalid json ");

        Assert.NotNull(errorMsg);
    }

    [Fact]
    public async Task DisposeAsync_CleansUpGracefully()
    {
        var client = new GeminiLiveClient();
        await client.DisposeAsync();
        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);
    }

    [Fact]
    public async Task SendAudioChunkAsync_WhenDisconnected_DoesNotThrow()
    {
        using var client = new GeminiLiveClient();
        byte[] dummyPcm = new byte[3200];
        var exception = await Record.ExceptionAsync(() => client.SendAudioChunkAsync(dummyPcm));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SendTextMessageAsync_WhenDisconnected_DoesNotThrow()
    {
        using var client = new GeminiLiveClient();
        var exception = await Record.ExceptionAsync(() => client.SendTextMessageAsync("Test message"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisconnectAsync_WhenAlreadyDisconnected_IsIdempotent()
    {
        using var client = new GeminiLiveClient();
        await client.DisconnectAsync();
        await client.DisconnectAsync();
        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);
    }
}
