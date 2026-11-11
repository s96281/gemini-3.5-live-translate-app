using System;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Audio capture source type.
/// </summary>
public enum AudioSourceType
{
    SystemAudioLoopback = 0,
    Microphone = 1
}

/// <summary>
/// Alias for AudioSourceType to match contracts.
/// </summary>
public enum AudioSource
{
    SystemAudioLoopback = 0,
    Microphone = 1
}

/// <summary>
/// Connection state for Gemini Live WebSocket stream.
/// </summary>
public enum LiveConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Error = 4
}

/// <summary>
/// Event arguments for raw audio chunk.
/// </summary>
public class AudioChunkEventArgs : EventArgs
{
    public byte[] PcmChunk { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    public AudioChunkEventArgs(byte[] pcmChunk, int sampleRate = 16000, int channels = 1)
    {
        PcmChunk = pcmChunk ?? throw new ArgumentNullException(nameof(pcmChunk));
        SampleRate = sampleRate;
        Channels = channels;
    }
}

/// <summary>
/// Event arguments for FFT audio spectrum calculation.
/// </summary>
public class AudioSpectrumEventArgs : EventArgs
{
    public float[] Bands { get; }

    public AudioSpectrumEventArgs(float[] bands)
    {
        Bands = bands ?? throw new ArgumentNullException(nameof(bands));
    }
}

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public LiveConnectionState OldState { get; }
    public LiveConnectionState NewState { get; }
    public string? Reason { get; }

    public ConnectionStateChangedEventArgs(LiveConnectionState oldState, LiveConnectionState newState, string? reason = null)
    {
        OldState = oldState;
        NewState = newState;
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for transcript text tokens or sentences.
/// </summary>
public class TranscriptEventArgs : EventArgs
{
    public string Text { get; }
    public bool IsFinal { get; }
    public string? Language { get; }

    public TranscriptEventArgs(string text, bool isFinal = false, string? language = null)
    {
        Text = text ?? string.Empty;
        IsFinal = isFinal;
        Language = language;
    }
}

/// <summary>
/// Event arguments for decoded 24kHz PCM audio output.
/// </summary>
public class AudioOutputEventArgs : EventArgs
{
    public byte[] Pcm24kData { get; }
    public int SampleRate { get; }

    public AudioOutputEventArgs(byte[] pcm24kData, int sampleRate = 24000)
    {
        Pcm24kData = pcm24kData ?? throw new ArgumentNullException(nameof(pcm24kData));
        SampleRate = sampleRate;
    }
}
