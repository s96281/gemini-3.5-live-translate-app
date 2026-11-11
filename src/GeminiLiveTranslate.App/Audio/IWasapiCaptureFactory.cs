using System.Collections.Generic;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Factory interface for creating WASAPI capture clients for system loopback and microphones.
/// Enables mock injection and cross-platform unit testing.
/// </summary>
public interface IWasapiCaptureFactory
{
    /// <summary>
    /// Creates an audio capture client for Windows system audio loopback.
    /// </summary>
    IWasapiCaptureClient CreateLoopbackCapture();

    /// <summary>
    /// Creates an audio capture client for a microphone endpoint.
    /// </summary>
    /// <param name="deviceId">Optional specific device ID, or null for default communication/capture endpoint.</param>
    IWasapiCaptureClient CreateMicrophoneCapture(string? deviceId = null);

    /// <summary>
    /// Enumerates all active microphone/input audio endpoints.
    /// </summary>
    IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices();
}
