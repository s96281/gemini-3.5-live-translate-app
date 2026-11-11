using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Authoritative audio capture engine interface for native Windows system audio loopback
/// and physical microphone capture with real-time resampling, chunking, and FFT analysis.
/// </summary>
public interface IAudioCaptureEngine : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether audio capture is actively running.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Gets the currently active audio source (SystemAudioLoopback or Microphone).
    /// </summary>
    AudioSource CurrentSource { get; }

    /// <summary>
    /// Gets the currently active capture device identifier, or null if using default.
    /// </summary>
    string? CurrentDeviceId { get; }

    /// <summary>
    /// Starts capturing audio synchronously from the specified source.
    /// </summary>
    void StartCapture(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null);

    /// <summary>
    /// Starts capturing audio asynchronously from the specified source.
    /// </summary>
    Task StartAsync(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null);

    /// <summary>
    /// Starts capturing audio synchronously from the specified source type.
    /// </summary>
    void StartCapture(AudioSourceType sourceType, string? deviceId = null);

    /// <summary>
    /// Starts capturing audio asynchronously from the specified source type.
    /// </summary>
    Task StartAsync(AudioSourceType sourceType, string? deviceId = null);

    /// <summary>
    /// Stops capturing audio synchronously.
    /// </summary>
    void StopCapture();

    /// <summary>
    /// Stops capturing audio asynchronously.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Atomically switches capture to the specified source synchronously without breaking downstream sessions.
    /// </summary>
    void SwitchSource(AudioSource newSource, string? deviceId = null);

    /// <summary>
    /// Atomically switches capture to the specified source asynchronously without breaking downstream sessions.
    /// </summary>
    Task SwitchSourceAsync(AudioSource newSource, string? deviceId = null);

    /// <summary>
    /// Atomically switches capture to the specified source type synchronously.
    /// </summary>
    void SwitchSource(AudioSourceType newSource, string? deviceId = null);

    /// <summary>
    /// Atomically switches capture to the specified source type asynchronously.
    /// </summary>
    Task SwitchSourceAsync(AudioSourceType newSource, string? deviceId = null);

    /// <summary>
    /// Enumerates available microphone capture endpoints synchronously.
    /// </summary>
    IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices();

    /// <summary>
    /// Enumerates available microphone capture endpoints asynchronously.
    /// </summary>
    Task<IReadOnlyList<AudioDeviceDescriptor>> GetMicrophoneDevicesAsync();

    /// <summary>
    /// Dispatched when an exact 100ms (3,200-byte) 16kHz 16-bit Mono Linear PCM chunk is captured and resampled.
    /// </summary>
    event EventHandler<byte[]>? OnAudioChunkCaptured;

    /// <summary>
    /// Dispatched when 36-band logarithmic FFT spectrum magnitudes [0.0, 1.0] are calculated.
    /// </summary>
    event EventHandler<float[]>? OnSpectrumCalculated;

    /// <summary>
    /// Dispatched when an audio capture error or hardware exception occurs.
    /// </summary>
    event EventHandler<Exception>? OnCaptureError;

    /// <summary>
    /// High-level event carrying audio chunk data and format metadata.
    /// </summary>
    event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;

    /// <summary>
    /// High-level event carrying normalized frequency spectrum band data.
    /// </summary>
    event EventHandler<AudioSpectrumEventArgs>? SpectrumDataAvailable;

    /// <summary>
    /// High-level event carrying capture error messages for UI notifications.
    /// </summary>
    event EventHandler<string>? CaptureErrorOccurred;
}
