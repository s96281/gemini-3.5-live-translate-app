using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Challenger 2 Stress, Boundary, Adversarial, and Security Test Suite.
/// Probes extreme edge cases in Protocol, WebSocket Client, Text Segmentation, and Settings Storage.
/// </summary>
public class Challenger2_StressAndAdversarialTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "GeminiLive_Challenger2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    #region 1. Gemini Protocol & WebSocket Client Stress Tests

    [Theory]
    [InlineData("{ Not Valid Json! }")]
    [InlineData("{\"serverContent\": {\"modelTurn\":")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a raw string\"")]
    [InlineData("123456789")]
    [InlineData("")]
    [InlineData("   \t\r\n   ")]
    [InlineData("{}")]
    public void Protocol_MalformedOrIncompleteJson_ClientHandlesGracefullyWithoutCrash(string malformedJson)
    {
        using var client = new GeminiLiveClient();
        bool errorRaised = false;
        client.ErrorOccurred += (s, err) => errorRaised = true;
        client.OnErrorOccurred += (s, ex) => errorRaised = true;

        // Must not throw unhandled exception out of ProcessIncomingServerMessage
        var exception = Record.Exception(() => client.ProcessIncomingServerMessage(malformedJson));
        Assert.Null(exception);

        // For non-empty invalid JSON syntax, error event should be dispatched
        if (!string.IsNullOrWhiteSpace(malformedJson) && malformedJson.StartsWith("{ Not"))
        {
            Assert.True(errorRaised);
        }
    }

    [Fact]
    public void Protocol_UnexpectedFutureJsonFields_DeserializesSafelyIgnoringUnknownProperties()
    {
        string jsonWithUnknownFields = @"{
            ""serverContent"": {
                ""modelTurn"": {
                    ""parts"": [
                        {
                            ""text"": ""Hello future world!"",
                            ""futureAIConfidenceScore"": 0.998,
                            ""quantumTelemetry"": { ""qubits"": 128 }
                        }
                    ],
                    ""unrecognizedModelMetadata"": ""alpha-v3""
                },
                ""unknownLiveFeature"": true
            },
            ""globalProtocolVersion"": ""3.5-experimental""
        }";

        using var client = new GeminiLiveClient();
        string receivedText = string.Empty;
        client.TargetTranscriptReceived += (s, e) => receivedText += e.Text;

        client.ProcessIncomingServerMessage(jsonWithUnknownFields);

        Assert.Equal("Hello future world!", receivedText);
    }

    [Fact]
    public void Protocol_MissingOptionalFields_NullModelTurnAndParts_DoesNotThrow()
    {
        using var client = new GeminiLiveClient();

        // 1. null serverContent
        client.ProcessIncomingServerMessage("{\"serverContent\": null}");

        // 2. null modelTurn
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"modelTurn\": null}}");

        // 3. empty parts
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"modelTurn\": {\"parts\": []}}}");

        // 4. parts containing a null element
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"modelTurn\": {\"parts\": [null]}}}");

        // 5. part with null text and null inlineData
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"modelTurn\": {\"parts\": [{\"text\": null, \"inlineData\": null}]}}}");

        // 6. null inputTranscription and null outputTranscription
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"inputTranscription\": null, \"outputTranscription\": null}}");

        // 7. turnComplete only
        bool turnCompleted = false;
        client.TurnCompleted += (s, e) => turnCompleted = true;
        client.ProcessIncomingServerMessage("{\"serverContent\": {\"turnComplete\": true}}");
        Assert.True(turnCompleted);
    }

    [Fact]
    public void Protocol_CorruptedBase64AudioInInlineData_CatchesFormatExceptionSafely()
    {
        using var client = new GeminiLiveClient();
        bool audioReceived = false;
        client.AudioOutputReceived += (s, e) => audioReceived = true;

        string corruptedBase64Json = @"{
            ""serverContent"": {
                ""modelTurn"": {
                    ""parts"": [
                        {
                            ""inlineData"": {
                                ""mimeType"": ""audio/pcm;rate=24000"",
                                ""data"": ""!!NotAValidBase64String&&%%""
                            }
                        }
                    ]
                }
            }
        }";

        // Should not throw out of ProcessIncomingServerMessage
        var exception = Record.Exception(() => client.ProcessIncomingServerMessage(corruptedBase64Json));
        Assert.Null(exception);
        Assert.False(audioReceived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(64 * 1024)]     // 64 KB
    [InlineData(128 * 1024)]    // 128 KB
    public void Protocol_AudioChunk_VariousSizesAndBase64Padding_SerializesValidly(int byteCount)
    {
        byte[] pcmData = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            pcmData[i] = (byte)(i % 256);
        }

        var message = GeminiClientMessage.CreateAudioChunk(pcmData);
        string json = message.ToJson();

        Assert.NotNull(json);
        Assert.Contains("audio/pcm;rate=16000", json);

        using var doc = JsonDocument.Parse(json);
        var mediaChunk = doc.RootElement
            .GetProperty("realtimeInput")
            .GetProperty("mediaChunks")[0];

        string b64 = mediaChunk.GetProperty("data").GetString()!;
        byte[] decoded = Convert.FromBase64String(b64);
        Assert.Equal(pcmData, decoded);
    }

    [Fact]
    public void Protocol_MultipleMediaChunks_SerializesAndDeserializesRoundTrip()
    {
        var msg = new GeminiClientMessage
        {
            RealtimeInput = new GeminiRealtimeInput
            {
                MediaChunks = new List<GeminiMediaChunk>
                {
                    new() { MimeType = "audio/pcm;rate=16000", Data = Convert.ToBase64String(new byte[] { 1, 2, 3 }) },
                    new() { MimeType = "audio/pcm;rate=16000", Data = Convert.ToBase64String(new byte[] { 4, 5, 6 }) },
                    new() { MimeType = "audio/pcm;rate=16000", Data = Convert.ToBase64String(new byte[] { 7, 8, 9 }) }
                }
            }
        };

        string json = msg.ToJson();
        using var doc = JsonDocument.Parse(json);
        var chunks = doc.RootElement.GetProperty("realtimeInput").GetProperty("mediaChunks");
        Assert.Equal(3, chunks.GetArrayLength());
    }

    [Fact]
    public async Task Client_RapidConcurrentSendAudioChunkAsync_DoesNotDeadlockOrThrow()
    {
        using var client = new GeminiLiveClient();
        // Disconnected client should gracefully discard chunks without throwing or deadlocking
        byte[] chunk = new byte[640]; // 20ms of 16kHz 16-bit mono

        var tasks = new Task[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await client.SendAudioChunkAsync(chunk);
            });
        }

        await Task.WhenAll(tasks);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Client_DisconnectWhileDisconnected_IsIdempotentAndSafe()
    {
        using var client = new GeminiLiveClient();
        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);

        // Multiple calls to DisconnectAsync must complete cleanly without throwing
        await client.DisconnectAsync();
        await client.DisconnectAsync();
        await client.DisconnectAsync();

        Assert.Equal(LiveConnectionState.Disconnected, client.CurrentState);
    }

    [Fact]
    public async Task Client_DisposeAndDisposeAsync_CanBeCalledConcurrentlySafely()
    {
        var client = new GeminiLiveClient();

        var t1 = Task.Run(async () => await client.DisposeAsync());
        var t2 = Task.Run(() => client.Dispose());

        await Task.WhenAll(t1, t2);
        Assert.False(client.IsConnected);
    }

    #endregion

    #region 2. Text Segmentation Stress & Adversarial Tests

    [Fact]
    public void Segmentation_500ConsecutiveChineseCharactersWithoutPunctuation_SplitsStrictlyWithinLimit()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        string text = new string('繁', 500);

        var chunks = segmenter.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35, $"Chunk exceeded 35 chars: {chunk.Length}");
        }

        // Entire original text must be fully preserved across chunks
        string reconstructed = string.Concat(chunks);
        Assert.Equal(text, reconstructed);
    }

    [Fact]
    public void Segmentation_1000ConsecutiveLatinCharactersWithoutSpaces_SplitsIntoExactThresholdChunks()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        string text = new string('X', 1000);

        var chunks = segmenter.ChunkText(text, maxCharacters: 50);

        Assert.Equal(20, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(50, c.Length));
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void Segmentation_ExclusivelyPunctuationMarks_PreservesContentWithoutLooping()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        string punctuationOnly = "！？。，、、、、？？！";

        var chunks = segmenter.ChunkText(punctuationOnly, maxCharacters: 35);
        Assert.NotEmpty(chunks);
        Assert.Equal(punctuationOnly, string.Concat(chunks));

        // Progressive ProcessToken on pure punctuation string
        segmenter.Reset();
        var res1 = segmenter.ProcessToken(punctuationOnly, isFinal: false);
        Assert.NotNull(res1);

        segmenter.Reset();
        var res2 = segmenter.ProcessToken(punctuationOnly, isFinal: true);
        Assert.True(res2.HasCommitted);
        Assert.Equal(punctuationOnly, res2.CommittedText);
    }

    [Fact]
    public void Segmentation_ContinuousPeriodEllipses_DoesNotThrowOrHang()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 20 };
        string dots = new string('.', 100);

        var chunks = segmenter.ChunkText(dots, maxCharacters: 20);
        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 20);
        }
    }

    [Fact]
    public void Segmentation_ComplexMultiCodePointEmojis_NeverSplitsSurrogatePairsInHalf()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        // 34 regular Chinese characters + 1 emoji (which is 2 UTF-16 chars: high + low surrogate)
        // Total string length = 36 chars.
        // If cut naively at 35, the emoji would be sliced into lonely high surrogate and low surrogate!
        string prefix = new string('中', 34);
        string rocketEmoji = "🚀"; // U+1F680 (\uD83D\uDE80)
        string testStr = prefix + rocketEmoji;

        var chunks = segmenter.ChunkText(testStr, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35, $"Chunk length {chunk.Length} exceeded 35");

            // Verify UTF-16 surrogate validity: no chunk must end with an unpaired high surrogate or start with an unpaired low surrogate
            if (chunk.Length > 0)
            {
                Assert.False(char.IsHighSurrogate(chunk[^1]), "Chunk ended with an unpaired high surrogate!");
                Assert.False(char.IsLowSurrogate(chunk[0]), "Chunk started with an unpaired low surrogate!");
            }
        }

        // Full emoji must remain intact in one of the chunks
        Assert.Contains(rocketEmoji, string.Concat(chunks));
    }

    [Fact]
    public void Segmentation_FamilyAndProfessionZWJEmojis_PreservesTextSafely()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        // Family emoji: 👨‍👩‍👧‍👦 (U+1F468 U+200D U+1F469 U+200D U+1F467 U+200D U+1F466)
        // Technologist: 🧑‍💻
        // Flags: 🇹🇼 🇯🇵
        string input = "家庭符號 👨‍👩‍👧‍👦 與工程師 🧑‍💻 以及國旗 🇹🇼🇯🇵，在即時字幕中必須清晰完整！";

        var chunks = segmenter.ChunkText(input, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35);
            // Verify no broken surrogate pairs
            for (int i = 0; i < chunk.Length; i++)
            {
                if (char.IsHighSurrogate(chunk[i]))
                {
                    Assert.True(i + 1 < chunk.Length && char.IsLowSurrogate(chunk[i + 1]),
                        "High surrogate not followed by low surrogate!");
                    i++;
                }
                else
                {
                    Assert.False(char.IsLowSurrogate(chunk[i]), "Lonely low surrogate detected!");
                }
            }
        }
    }

    [Fact]
    public void Segmentation_MixedScriptsAndControlCharacters_HandledWithoutExceptions()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 40 };

        // Chinese + Japanese + Arabic (RTL) + English + Emojis + Control chars (\r, \t, \0)
        string mixed = "繁體中文\t日本語\r\nالعربية (مرحبا) and English with Emoji 🌍 and null\0char.";

        var chunks = segmenter.ChunkText(mixed, maxCharacters: 40);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 40);
        }
    }

    [Theory]
    [InlineData(34, 35, 1)]  // 34 chars with limit 35 -> 1 chunk
    [InlineData(35, 35, 1)]  // 35 chars with limit 35 -> 1 chunk
    [InlineData(36, 35, 2)]  // 36 chars with limit 35 -> 2 chunks
    [InlineData(49, 50, 1)]  // 49 chars with limit 50 -> 1 chunk
    [InlineData(50, 50, 1)]  // 50 chars with limit 50 -> 1 chunk
    [InlineData(51, 50, 2)]  // 51 chars with limit 50 -> 2 chunks
    [InlineData(74, 75, 1)]  // 74 chars with limit 75 -> 1 chunk
    [InlineData(75, 75, 1)]  // 75 chars with limit 75 -> 1 chunk
    [InlineData(76, 75, 2)]  // 76 chars with limit 75 -> 2 chunks
    [InlineData(99, 100, 1)] // 99 chars with limit 100 -> 1 chunk
    [InlineData(100, 100, 1)]// 100 chars with limit 100 -> 1 chunk
    [InlineData(101, 100, 2)]// 101 chars with limit 100 -> 2 chunks
    public void Segmentation_ExactBoundaryThresholds_SplitsAtPreciseCount(int textLength, int limit, int expectedChunks)
    {
        var segmenter = new TextSegmentationService { MaxCharacters = limit };
        string text = new string('A', textLength);

        var chunks = segmenter.ChunkText(text, maxCharacters: limit);

        Assert.Equal(expectedChunks, chunks.Count);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= limit);
        }
    }

    [Fact]
    public void Segmentation_UnlimitedThresholdZeroOrNegative_ReturnsEntireTextUnsplit()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 0 };
        string text = "這是一段非常長的測試文字，包含標點符號。即使長度超過一百個字元，也不應該被切分成多個區塊！";

        var chunksZero = segmenter.ChunkText(text, maxCharacters: 0);
        Assert.Single(chunksZero);
        Assert.Equal(text, chunksZero[0]);

        var chunksNegative = segmenter.ChunkText(text, maxCharacters: -10);
        Assert.Single(chunksNegative);
        Assert.Equal(text, chunksNegative[0]);
    }

    [Fact]
    public void Segmentation_ProgressiveTokenStreaming_ChineseLongToken_StreamsCorrectly()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        // Streaming a 100-character sentence without punctuation
        string chunk100 = new string('字', 100);
        var r1 = segmenter.ProcessToken(chunk100, isFinal: false);

        Assert.True(r1.HasCommitted);
        Assert.True(r1.CommittedText.Length <= 35);
        Assert.NotEmpty(r1.PendingText);

        // Finalize remaining stream
        var r2 = segmenter.ProcessToken(string.Empty, isFinal: true);
        Assert.NotEmpty(r2.CommittedText);
        Assert.Empty(r2.PendingText);
    }

    #endregion

    #region 3. Settings & Security Stress Tests

    [Fact]
    public void Settings_CorruptedJsonFileOnDisk_LoadsDefaultsGracefully()
    {
        string dir = CreateTempDirectory();
        string settingsFile = Path.Combine(dir, "settings.json");

        // Write corrupt JSON syntax
        File.WriteAllText(settingsFile, "{ \"ApiKey\": \"abc\", \"MaxParagraphLength\": INVALID_JSON }", Encoding.UTF8);

        var service = new SettingsService(settingsFile);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        // Should have returned defaults
        Assert.Equal("models/gemini-2.0-flash-exp", settings.Model);
        Assert.Equal("Puck", settings.Voice);
        Assert.Equal(50, settings.MaxParagraphLength);
        Assert.False(settings.AutoPlayAudio);
    }

    [Fact]
    public void Settings_BinaryGarbageFileOnDisk_LoadsDefaultsGracefully()
    {
        string dir = CreateTempDirectory();
        string settingsFile = Path.Combine(dir, "settings.json");

        // Write random binary garbage
        File.WriteAllBytes(settingsFile, new byte[] { 0xFF, 0x00, 0xFE, 0x12, 0xDE, 0xAD, 0xBE, 0xEF });

        var service = new SettingsService(settingsFile);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal("models/gemini-2.0-flash-exp", settings.Model);
    }

    [Fact]
    public void Settings_EmptySettingsFile_LoadsDefaultsGracefully()
    {
        string dir = CreateTempDirectory();
        string settingsFile = Path.Combine(dir, "settings.json");

        File.WriteAllText(settingsFile, string.Empty);

        var service = new SettingsService(settingsFile);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(50, settings.MaxParagraphLength);
    }

    [Fact]
    public void Settings_SaveSettings_DeepNestedParentDirectories_CreatedAutomatically()
    {
        string dir = CreateTempDirectory();
        string deepPath = Path.Combine(dir, "deeply", "nested", "config", "path", "settings.json");

        var service = new SettingsService(deepPath);
        var settings = new AppSettings
        {
            Model = "models/gemini-2.0-flash-exp",
            Voice = "Kore",
            MaxParagraphLength = 75
        };

        service.SaveSettings(settings);

        Assert.True(File.Exists(deepPath));
        var loaded = service.LoadSettings();
        Assert.Equal("Kore", loaded.Voice);
        Assert.Equal(75, loaded.MaxParagraphLength);
    }

    [Fact]
    public void Settings_GetApiKey_PrecedenceAndFallbackHierarchy()
    {
        string dir = CreateTempDirectory();
        string settingsFile = Path.Combine(dir, "settings.json");
        var service = new SettingsService(settingsFile);

        string? originalGemini = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        string? originalGoogle = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

        try
        {
            // Case 1: No file key, no env vars -> returns empty
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", null);
            service.SaveApiKey(string.Empty);
            Assert.Equal(string.Empty, service.GetApiKey());

            // Case 2: Only GOOGLE_API_KEY set
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", "google-key-fallback");
            Assert.Equal("google-key-fallback", service.GetApiKey());

            // Case 3: Both GEMINI_API_KEY and GOOGLE_API_KEY set -> GEMINI_API_KEY takes priority
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "gemini-key-priority");
            Assert.Equal("gemini-key-priority", service.GetApiKey());

            // Case 4: File contains encrypted key -> File key takes priority over all env vars
            service.SaveApiKey("file-stored-key");
            Assert.Equal("file-stored-key", service.GetApiKey());

            // Case 5: Wiping file key -> Falls back back to GEMINI_API_KEY
            service.SaveApiKey(string.Empty);
            Assert.Equal("gemini-key-priority", service.GetApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", originalGemini);
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", originalGoogle);
        }
    }

    [Fact]
    public void Settings_EncryptAndDecrypt_AdversarialInputs_HandledSafely()
    {
        var service = new SettingsService();

        // Null and empty inputs
        Assert.Equal(string.Empty, service.EncryptSecret(string.Empty));
        Assert.Equal(string.Empty, service.DecryptSecret(null));
        Assert.Equal(string.Empty, service.DecryptSecret(string.Empty));
        Assert.Equal(string.Empty, service.DecryptSecret("   \t  "));

        // Malformed base64 marker
        string result = service.DecryptSecret("b64:@@InvalidBase64@@");
        Assert.Equal(string.Empty, result);

        // Very long key (10,000 characters)
        string longKey = new string('K', 10000);
        string encrypted = service.EncryptSecret(longKey);
        string decrypted = service.DecryptSecret(encrypted);
        Assert.Equal(longKey, decrypted);
    }

    [Fact]
    public void Settings_ReadOnlySettingsFile_ThrowsUnauthorizedAccessExceptionWhenSaving()
    {
        string dir = CreateTempDirectory();
        string settingsFile = Path.Combine(dir, "settings.json");
        File.WriteAllText(settingsFile, "{}", Encoding.UTF8);

        // Mark file as read-only
        File.SetAttributes(settingsFile, FileAttributes.ReadOnly);
        try
        {
            var service = new SettingsService(settingsFile);
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                service.SaveSettings(new AppSettings());
            });
        }
        finally
        {
            File.SetAttributes(settingsFile, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Settings_DirectoryWithoutWritePermission_ThrowsWhenCreating()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string dir = CreateTempDirectory();
        string lockedDir = Path.Combine(dir, "locked_dir");
        Directory.CreateDirectory(lockedDir);
        string settingsFile = Path.Combine(lockedDir, "settings.json");

        // Remove write permission from locked_dir
        File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var service = new SettingsService(settingsFile);
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                service.SaveSettings(new AppSettings());
            });
        }
        finally
        {
            File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Protocol_ServerBargeInAndGoAway_DispatchesEventsAccurately()
    {
        using var client = new GeminiLiveClient();
        int interruptedCount = 0;
        client.Interrupted += (s, e) => interruptedCount++;

        // Rapid bursts of interruptions
        for (int i = 0; i < 10; i++)
        {
            client.ProcessIncomingServerMessage("{\"serverContent\": {\"interrupted\": true}}");
        }
        Assert.Equal(10, interruptedCount);

        // GoAway message
        var exception = Record.Exception(() =>
        {
            client.ProcessIncomingServerMessage("{\"goAway\": {\"timeLeft\": \"15s\"}}");
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Segmentation_AlignChunks_MismatchedLength_AlignsWithoutExceptions()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        string source = "This is a short sentence.";
        string target = "這是一段很長很長的目標譯文，用來測試在雙語不平衡長度的情況下，分段對齊演算法是否能夠穩定運作並處理超長譯文。";

        var (sourceChunks, targetChunks) = segmenter.AlignChunks(source, target, maxCharacters: 35);

        Assert.NotEmpty(sourceChunks);
        Assert.NotEmpty(targetChunks);
        Assert.Single(sourceChunks);
        Assert.True(targetChunks.Count > 1);
        Assert.All(targetChunks, c => Assert.True(c.Length <= 35));
    }

    #endregion
}
