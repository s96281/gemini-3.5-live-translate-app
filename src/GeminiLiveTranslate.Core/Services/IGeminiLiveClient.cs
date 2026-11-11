using System;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Bidirectional streaming client interface for Google Gemini Live API.
/// </summary>
public interface IGeminiLiveClient : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Current WebSocket connection state.
    /// </summary>
    LiveConnectionState CurrentState { get; }

    /// <summary>
    /// Indicates whether the client is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Active configuration for the current session.
    /// </summary>
    LiveConfig? CurrentConfig { get; }

    /// <summary>
    /// Initiates a WebSocket connection to Gemini Live endpoint.
    /// </summary>
    Task ConnectAsync(LiveConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the active session gracefully.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Overload for parameterless disconnect.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a 16kHz 16-bit mono PCM audio chunk to Gemini Live.
    /// </summary>
    Task SendAudioChunkAsync(byte[] pcm16kChunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a text message to Gemini Live.
    /// </summary>
    Task SendTextMessageAsync(string text, CancellationToken cancellationToken = default);

    // Primary events
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    event EventHandler<TranscriptEventArgs>? SourceTranscriptReceived;
    event EventHandler<TranscriptEventArgs>? TargetTranscriptReceived;
    event EventHandler<AudioOutputEventArgs>? AudioOutputReceived; // 24kHz PCM
    event EventHandler? Interrupted;
    event EventHandler? TurnCompleted;
    event EventHandler<string>? ErrorOccurred;

    // Alternative contract events
    event EventHandler<string>? OnInputTranscriptionReceived;
    event EventHandler<string>? OnOutputTranscriptionReceived;
    event EventHandler<byte[]>? OnOutputAudioReceived; // 24kHz PCM
    event EventHandler<bool>? OnInterruptedReceived;
    event EventHandler<bool>? OnTurnCompletedReceived;
    event EventHandler<Exception>? OnErrorOccurred;
}
