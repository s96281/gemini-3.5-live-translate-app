using System;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Generates synthetic audio buffers for testing resampling, DSP, FFT, and capture contracts.
/// </summary>
public static class SyntheticAudioGenerator
{
    /// <summary>
    /// Generates IEEE 32-bit floating point PCM audio buffer.
    /// </summary>
    public static byte[] GenerateFloatWave(
        double frequencyHz,
        int sampleRate,
        int channels,
        int frameCount,
        float amplitude = 0.8f)
    {
        int bytesPerSample = 4;
        int bytesRecorded = frameCount * channels * bytesPerSample;
        byte[] buffer = new byte[bytesRecorded];

        for (int i = 0; i < frameCount; i++)
        {
            float sample = (float)(Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate) * amplitude);
            byte[] sampleBytes = BitConverter.GetBytes(sample);

            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                Array.Copy(sampleBytes, 0, buffer, offset, bytesPerSample);
            }
        }

        return buffer;
    }

    /// <summary>
    /// Generates 16-bit signed integer linear PCM audio buffer.
    /// </summary>
    public static byte[] GeneratePcm16Wave(
        double frequencyHz,
        int sampleRate,
        int channels,
        int frameCount,
        double amplitude = 0.8)
    {
        int bytesPerSample = 2;
        int bytesRecorded = frameCount * channels * bytesPerSample;
        byte[] buffer = new byte[bytesRecorded];
        short maxVal = (short)(32767 * Math.Clamp(amplitude, 0.0, 1.0));

        for (int i = 0; i < frameCount; i++)
        {
            short sample = (short)(Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate) * maxVal);
            byte[] sampleBytes = BitConverter.GetBytes(sample);

            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                Array.Copy(sampleBytes, 0, buffer, offset, bytesPerSample);
            }
        }

        return buffer;
    }

    /// <summary>
    /// Generates pure silence buffer of 16-bit PCM.
    /// </summary>
    public static byte[] GenerateSilencePcm16(int sampleRate, int channels, int frameCount)
    {
        return new byte[frameCount * channels * 2];
    }

    /// <summary>
    /// Generates pure silence buffer of 32-bit float PCM.
    /// </summary>
    public static byte[] GenerateSilenceFloat(int sampleRate, int channels, int frameCount)
    {
        return new byte[frameCount * channels * 4];
    }

    /// <summary>
    /// Generates an extreme clipping signal with values outside [-1.0, 1.0].
    /// </summary>
    public static byte[] GenerateExtremeClippingFloat(int channels, int frameCount, float extremeValue = 100.0f)
    {
        byte[] buffer = new byte[frameCount * channels * 4];
        for (int i = 0; i < frameCount; i++)
        {
            float val = (i % 2 == 0) ? extremeValue : -extremeValue;
            byte[] sampleBytes = BitConverter.GetBytes(val);
            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * 4;
                Array.Copy(sampleBytes, 0, buffer, offset, 4);
            }
        }
        return buffer;
    }

    /// <summary>
    /// Extracts maximum absolute 16-bit sample from a PCM buffer.
    /// </summary>
    public static short GetMaxAbsoluteSample(byte[] pcm16Buffer)
    {
        if (pcm16Buffer == null || pcm16Buffer.Length < 2) return 0;
        short max = 0;
        for (int i = 0; i < pcm16Buffer.Length / 2; i++)
        {
            short s = Math.Abs(BitConverter.ToInt16(pcm16Buffer, i * 2));
            if (s > max) max = s;
        }
        return max;
    }
}
