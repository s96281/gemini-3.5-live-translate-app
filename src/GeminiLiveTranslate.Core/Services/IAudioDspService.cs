using System;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Digital Signal Processing service for audio resampling, FFT spectrum analysis, and RMS loudness.
/// </summary>
public interface IAudioDspService
{
    /// <summary>
    /// Resamples an input audio buffer to 16,000 Hz, 16-bit signed integer little-endian, mono PCM.
    /// Supports IEEE 32-bit float and 16-bit integer PCM, stereo or mono.
    /// </summary>
    byte[] ResampleTo16k16BitMono(
        byte[] inputBuffer,
        int bytesRecorded,
        int sourceSampleRate,
        int sourceChannels,
        int sourceBitsPerSample,
        bool isFloat);

    /// <summary>
    /// Calculates logarithmic frequency spectrum magnitudes from 16kHz 16-bit mono PCM.
    /// Applies Hann window, computes FFT, and groups into bandCount bins normalized to [0.0, 1.0].
    /// </summary>
    float[] CalculateSpectrumFft(byte[] pcm16kBuffer, int bandCount = 36);

    /// <summary>
    /// Computes Root Mean Square (RMS) amplitude of 16-bit mono PCM, normalized to [0.0, 1.0].
    /// </summary>
    float CalculateRms(byte[] pcm16kBuffer);
}
