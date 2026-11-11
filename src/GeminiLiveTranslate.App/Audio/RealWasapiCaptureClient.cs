using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Production wrapper for NAudio WasapiCapture and WasapiLoopbackCapture.
/// </summary>
public sealed class RealWasapiCaptureClient : IWasapiCaptureClient
{
    private readonly WasapiCapture _capture;
    private bool _isDisposed;

    public RealWasapiCaptureClient(WasapiCapture capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _capture.DataAvailable += HandleDataAvailable;
        _capture.RecordingStopped += HandleRecordingStopped;
    }

    public WaveFormat? WaveFormat => _capture.WaveFormat;

    public void StartRecording()
    {
        _capture.StartRecording();
    }

    public void StopRecording()
    {
        _capture.StopRecording();
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    private void HandleDataAvailable(object? sender, WaveInEventArgs e)
    {
        DataAvailable?.Invoke(this, e);
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStopped?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _capture.DataAvailable -= HandleDataAvailable;
            _capture.RecordingStopped -= HandleRecordingStopped;
            _capture.Dispose();
        }
    }
}
