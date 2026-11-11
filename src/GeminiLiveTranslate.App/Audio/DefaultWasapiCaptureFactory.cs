using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Production WASAPI capture factory utilizing real NAudio WasapiLoopbackCapture,
/// WasapiCapture, and MMDeviceEnumerator on Windows.
/// </summary>
public class DefaultWasapiCaptureFactory : IWasapiCaptureFactory
{
    /// <inheritdoc/>
    public IWasapiCaptureClient CreateLoopbackCapture()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI system audio loopback capture requires Windows Core Audio.");
        }

        return new RealWasapiCaptureClient(new WasapiLoopbackCapture());
    }

    /// <inheritdoc/>
    public IWasapiCaptureClient CreateMicrophoneCapture(string? deviceId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI microphone capture requires Windows Core Audio.");
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new RealWasapiCaptureClient(new WasapiCapture());
        }

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        return new RealWasapiCaptureClient(new WasapiCapture(device));
    }

    /// <inheritdoc/>
    public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<AudioDeviceDescriptor>();
        }

        var result = new List<AudioDeviceDescriptor>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            MMDevice? defaultDevice = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                // Default device query may fail if no active audio endpoints exist
            }

            foreach (var dev in devices)
            {
                result.Add(new AudioDeviceDescriptor
                {
                    Id = dev.ID,
                    FriendlyName = dev.FriendlyName,
                    IsDefault = defaultDevice != null && dev.ID == defaultDevice.ID
                });
            }
        }
        catch
        {
            // Fallback gracefully on enumeration failures
        }

        return result;
    }
}
