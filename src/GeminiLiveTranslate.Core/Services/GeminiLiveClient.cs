using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Production ClientWebSocket implementation for Google Gemini Live Multimodal streaming API.
/// </summary>
public class GeminiLiveClient : IGeminiLiveClient
{
    public const string DefaultLiveEndpoint = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _sessionCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _isDisposed;

    public LiveConnectionState CurrentState { get; private set; } = LiveConnectionState.Disconnected;
    public bool IsConnected => CurrentState == LiveConnectionState.Connected && _webSocket?.State == WebSocketState.Open;
    public LiveConfig? CurrentConfig { get; private set; }

    // Primary events
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<TranscriptEventArgs>? SourceTranscriptReceived;
    public event EventHandler<TranscriptEventArgs>? TargetTranscriptReceived;
    public event EventHandler<AudioOutputEventArgs>? AudioOutputReceived;
    public event EventHandler? Interrupted;
    public event EventHandler? TurnCompleted;
    public event EventHandler<string>? ErrorOccurred;

    // Alternative contract events
    public event EventHandler<string>? OnInputTranscriptionReceived;
    public event EventHandler<string>? OnOutputTranscriptionReceived;
    public event EventHandler<byte[]>? OnOutputAudioReceived;
    public event EventHandler<bool>? OnInterruptedReceived;
    public event EventHandler<bool>? OnTurnCompletedReceived;
    public event EventHandler<Exception>? OnErrorOccurred;

    /// <summary>
    /// Builds the Gemini Live WebSocket Uri with the API key query parameter.
    /// </summary>
    public static Uri BuildWebSocketUri(LiveConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(config));
        }

        string uriString = $"{DefaultLiveEndpoint}?key={Uri.EscapeDataString(config.ApiKey.Trim())}";
        return new Uri(uriString);
    }

    /// <summary>
    /// Constructs the initial Gemini setup payload for simultaneous translation.
    /// </summary>
    public static GeminiClientMessage BuildSetupPayload(LiveConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var targetLang = SupportedLanguages.GetByCodeOrDefault(config.TargetLanguageCode);
        string systemInstruction =
            $"You are an expert real-time simultaneous interpreter. " +
            $"Translate all incoming speech accurately, naturally, and immediately into {targetLang.DisplayName} ({targetLang.NativeName}, {targetLang.Code}). " +
            $"Output only the translation in {targetLang.NativeName}. Do not include conversational filler, meta-explanations, or notes.";

        return GeminiClientMessage.CreateSetup(
            model: config.Model,
            voice: config.Voice,
            systemInstructionText: systemInstruction,
            includeAudio: config.AutoPlayAudio);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(LiveConfig config, CancellationToken cancellationToken = default)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ArgumentException("API key is required to connect to Gemini Live.", nameof(config));
        }

        if (IsConnected)
        {
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        CurrentConfig = config;
        SetState(LiveConnectionState.Connecting);

        try
        {
            _sessionCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts.Token);

            _webSocket = new ClientWebSocket();
            Uri targetUri = BuildWebSocketUri(config);

            await _webSocket.ConnectAsync(targetUri, linkedCts.Token).ConfigureAwait(false);
            SetState(LiveConnectionState.Connected);

            // Send initial setup payload
            var setupMessage = BuildSetupPayload(config);
            string setupJson = setupMessage.ToJson();
            byte[] setupBytes = Encoding.UTF8.GetBytes(setupJson);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(setupBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                linkedCts.Token).ConfigureAwait(false);

            // Launch continuous receive loop
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_sessionCts.Token));
        }
        catch (Exception ex)
        {
            SetState(LiveConnectionState.Error, ex.Message);
            RaiseError(ex);
            CleanupSocket();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentState == LiveConnectionState.Disconnected)
        {
            return;
        }

        SetState(LiveConnectionState.Disconnected);

        try
        {
            _sessionCts?.Cancel();

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnected",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Socket closure exceptions during shutdown are non-critical
            Console.WriteLine($"[GeminiLiveClient] Disconnect note: {ex.Message}");
        }
        finally
        {
            CleanupSocket();
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync() => DisconnectAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async Task SendAudioChunkAsync(byte[] pcm16kChunk, CancellationToken cancellationToken = default)
    {
        if (pcm16kChunk == null || pcm16kChunk.Length == 0)
        {
            return;
        }

        if (!IsConnected || _webSocket == null)
        {
            return;
        }

        var audioMessage = GeminiClientMessage.CreateAudioChunk(pcm16kChunk);
        string json = audioMessage.ToJson();
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[GeminiLiveClient] WebSocket exception during send: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Socket disposed during disconnect
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SendTextMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!IsConnected || _webSocket == null)
        {
            return;
        }

        var textMessage = GeminiClientMessage.CreateTextMessage(text);
        string json = textMessage.ToJson();
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[GeminiLiveClient] WebSocket exception during send: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Socket disposed during disconnect
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Processes incoming server JSON and dispatches relevant events.
    /// Exposes message deserialization and event dispatching for verification and automated tests.
    /// </summary>
    public void ProcessIncomingServerMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var msg = GeminiServerMessage.FromJson(json);
            if (msg != null)
            {
                HandleServerMessage(msg);
            }
        }
        catch (Exception ex)
        {
            RaiseError(ex);
        }
    }

    /// <summary>
    /// Handles parsed server message components.
    /// </summary>
    public void HandleServerMessage(GeminiServerMessage msg)
    {
        if (msg == null) return;

        if (msg.ServerContent != null)
        {
            var sc = msg.ServerContent;

            // Handle interruption (barge-in)
            if (sc.Interrupted == true)
            {
                Interrupted?.Invoke(this, EventArgs.Empty);
                OnInterruptedReceived?.Invoke(this, true);
            }

            // Handle input transcription (speech-to-text of user/source audio)
            if (sc.InputTranscription != null && !string.IsNullOrEmpty(sc.InputTranscription.Text))
            {
                var eventArgs = new TranscriptEventArgs(sc.InputTranscription.Text, sc.InputTranscription.IsFinal ?? false);
                SourceTranscriptReceived?.Invoke(this, eventArgs);
                OnInputTranscriptionReceived?.Invoke(this, sc.InputTranscription.Text);
            }

            // Handle output transcription (translated text)
            if (sc.OutputTranscription != null && !string.IsNullOrEmpty(sc.OutputTranscription.Text))
            {
                var eventArgs = new TranscriptEventArgs(sc.OutputTranscription.Text, sc.OutputTranscription.IsFinal ?? false);
                TargetTranscriptReceived?.Invoke(this, eventArgs);
                OnOutputTranscriptionReceived?.Invoke(this, sc.OutputTranscription.Text);
            }

            // Handle model turn parts (text and 24kHz audio)
            if (sc.ModelTurn?.Parts != null)
            {
                foreach (var part in sc.ModelTurn.Parts)
                {
                    if (part == null) continue;

                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        var eventArgs = new TranscriptEventArgs(part.Text, false);
                        TargetTranscriptReceived?.Invoke(this, eventArgs);
                        OnOutputTranscriptionReceived?.Invoke(this, part.Text);
                    }

                    if (part.InlineData != null && !string.IsNullOrEmpty(part.InlineData.Data))
                    {
                        try
                        {
                            byte[] audioBytes = Convert.FromBase64String(part.InlineData.Data);
                            var eventArgs = new AudioOutputEventArgs(audioBytes, 24000);
                            AudioOutputReceived?.Invoke(this, eventArgs);
                            OnOutputAudioReceived?.Invoke(this, audioBytes);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[GeminiLiveClient] Failed to decode 24kHz audio base64: {ex.Message}");
                        }
                    }
                }
            }

            // Handle turn complete
            if (sc.TurnComplete == true)
            {
                TurnCompleted?.Invoke(this, EventArgs.Empty);
                OnTurnCompletedReceived?.Invoke(this, true);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        using var ms = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SetState(LiveConnectionState.Disconnected, "Server closed WebSocket connection.");
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    ProcessIncomingServerMessage(json);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetState(LiveConnectionState.Error, ex.Message);
                RaiseError(ex);
            }
        }
    }

    private void SetState(LiveConnectionState newState, string? reason = null)
    {
        if (CurrentState != newState)
        {
            var oldState = CurrentState;
            CurrentState = newState;
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState, reason));
        }
    }

    private void RaiseError(Exception ex)
    {
        ErrorOccurred?.Invoke(this, ex.Message);
        OnErrorOccurred?.Invoke(this, ex);
    }

    private void CleanupSocket()
    {
        try
        {
            _webSocket?.Dispose();
        }
        catch
        {
        }
        _webSocket = null;

        try
        {
            _sessionCts?.Dispose();
        }
        catch
        {
        }
        _sessionCts = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _sessionCts?.Cancel();
        }
        catch { }

        CleanupSocket();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch { }

        CleanupSocket();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
