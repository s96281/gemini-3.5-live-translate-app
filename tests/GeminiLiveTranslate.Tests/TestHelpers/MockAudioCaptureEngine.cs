using System;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Audio capture contract interface matching PROJECT.md § Interface Contracts.
/// </summary>
public interface IAudioCaptureEngine : IDisposable
{
    bool IsCapturing { get; }
    AudioSource CurrentSource { get; }
    void StartCapture(AudioSource source);
    void StopCapture();
    void SwitchSource(AudioSource newSource);

    event EventHandler<byte[]>? OnAudioChunkCaptured;
    event EventHandler<float[]>? OnSpectrumCalculated;
    event EventHandler<Exception>? OnCaptureError;
}

/// <summary>
/// Test and mock implementation of IAudioCaptureEngine adhering strictly to contract specifications.
/// </summary>
public class MockAudioCaptureEngine : IAudioCaptureEngine
{
    private readonly IAudioDspService _dspService;
    private bool _isDisposed;

    public bool IsCapturing { get; private set; }
    public AudioSource CurrentSource { get; private set; } = AudioSource.SystemAudioLoopback;
    public int ChunksCapturedCount { get; private set; }
    public int SpectrumCalculatedCount { get; private set; }
    public int SourceSwitchCount { get; private set; }
    public bool ShouldMutePlayback => CurrentSource == AudioSource.SystemAudioLoopback;
    public static bool ShouldMutePlaybackForSource(AudioSource source) => source == AudioSource.SystemAudioLoopback;

    public event EventHandler<byte[]>? OnAudioChunkCaptured;
    public event EventHandler<float[]>? OnSpectrumCalculated;
    public event EventHandler<Exception>? OnCaptureError;

    public MockAudioCaptureEngine(IAudioDspService? dspService = null)
    {
        _dspService = dspService ?? new AudioDspService();
    }

    public void StartCapture(AudioSource source)
    {
        ThrowIfDisposed();
        CurrentSource = source;
        IsCapturing = true;
    }

    public void StopCapture()
    {
        ThrowIfDisposed();
        IsCapturing = false;
    }

    public void SwitchSource(AudioSource newSource)
    {
        ThrowIfDisposed();
        if (CurrentSource != newSource)
        {
            CurrentSource = newSource;
            SourceSwitchCount++;
        }
    }

    /// <summary>
    /// Simulates raw audio arrival (e.g. 48kHz stereo float or 16-bit PCM), resamples to 16kHz mono, and emits events.
    /// </summary>
    public void SimulateRawInput(
        byte[] inputBuffer,
        int bytesRecorded,
        int sourceRate = 48000,
        int sourceChannels = 2,
        int sourceBits = 32,
        bool isFloat = true)
    {
        ThrowIfDisposed();
        if (!IsCapturing) return;

        try
        {
            byte[] pcm16k = _dspService.ResampleTo16k16BitMono(
                inputBuffer,
                bytesRecorded,
                sourceRate,
                sourceChannels,
                sourceBits,
                isFloat);

            if (pcm16k.Length > 0)
            {
                ChunksCapturedCount++;
                OnAudioChunkCaptured?.Invoke(this, pcm16k);

                float[] spectrum = _dspService.CalculateSpectrumFft(pcm16k, 36);
                SpectrumCalculatedCount++;
                OnSpectrumCalculated?.Invoke(this, spectrum);
            }
        }
        catch (Exception ex)
        {
            OnCaptureError?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Injects a silence packet (e.g. during loopback pause).
    /// </summary>
    public void InjectSilence(int sampleCount = 1600)
    {
        ThrowIfDisposed();
        if (!IsCapturing) return;

        byte[] silence = new byte[sampleCount * 2]; // 16kHz 16-bit mono
        ChunksCapturedCount++;
        OnAudioChunkCaptured?.Invoke(this, silence);

        float[] spectrum = new float[36];
        SpectrumCalculatedCount++;
        OnSpectrumCalculated?.Invoke(this, spectrum);
    }

    /// <summary>
    /// Simulates device unplugged or WASAPI error.
    /// </summary>
    public void SimulateError(Exception error)
    {
        ThrowIfDisposed();
        OnCaptureError?.Invoke(this, error);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MockAudioCaptureEngine));
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            IsCapturing = false;
        }
    }
}
