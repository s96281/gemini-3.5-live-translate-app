using System;
using NAudio.Wave;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Abstraction over NAudio's WasapiCapture to allow unit testing and cross-platform simulation.
/// </summary>
public interface IWasapiCaptureClient : IDisposable
{
    /// <summary>
    /// Gets the native wave format of the active capture endpoint.
    /// </summary>
    WaveFormat? WaveFormat { get; }

    /// <summary>
    /// Starts recording audio from the WASAPI endpoint.
    /// </summary>
    void StartRecording();

    /// <summary>
    /// Stops recording audio.
    /// </summary>
    void StopRecording();

    /// <summary>
    /// Event fired when a new buffer of raw audio data is available.
    /// </summary>
    event EventHandler<WaveInEventArgs>? DataAvailable;

    /// <summary>
    /// Event fired when recording has stopped (normally or due to an exception).
    /// </summary>
    event EventHandler<StoppedEventArgs>? RecordingStopped;
}
