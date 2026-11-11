using System;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Production DSP implementation for resampling, FFT spectrum analysis, and RMS calculation.
/// </summary>
public class AudioDspService : IAudioDspService
{
    public const int TargetSampleRate = 16000;
    public const int TargetBitsPerSample = 16;
    public const int TargetChannels = 1;

    /// <inheritdoc/>
    public byte[] ResampleTo16k16BitMono(
        byte[] inputBuffer,
        int bytesRecorded,
        int sourceSampleRate,
        int sourceChannels,
        int sourceBitsPerSample,
        bool isFloat)
    {
        if (inputBuffer == null || bytesRecorded <= 0 || sourceSampleRate <= 0 || sourceChannels <= 0)
        {
            return Array.Empty<byte>();
        }

        bytesRecorded = Math.Min(bytesRecorded, inputBuffer.Length);

        int bytesPerSample = sourceBitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            bytesPerSample = isFloat ? 4 : 2;
        }

        int bytesPerFrame = sourceChannels * bytesPerSample;
        int frameCount = bytesRecorded / bytesPerFrame;
        if (frameCount <= 0)
        {
            return Array.Empty<byte>();
        }

        // Step 1: Decode multi-channel input frames into normalized mono float samples [-1.0, 1.0]
        float[] monoSamples = new float[frameCount];

        if (isFloat && bytesPerSample == 4)
        {
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                int frameOffset = i * bytesPerFrame;
                for (int c = 0; c < sourceChannels; c++)
                {
                    int sampleOffset = frameOffset + (c * 4);
                    if (sampleOffset + 4 <= inputBuffer.Length && sampleOffset + 4 <= bytesRecorded)
                    {
                        float val = BitConverter.ToSingle(inputBuffer, sampleOffset);
                        if (float.IsNaN(val) || float.IsSubnormal(val)) val = 0f;
                        else if (float.IsPositiveInfinity(val) || val > 1.0f) val = 1.0f;
                        else if (float.IsNegativeInfinity(val) || val < -1.0f) val = -1.0f;
                        sum += val;
                    }
                }
                float avg = sum / sourceChannels;
                monoSamples[i] = Math.Clamp(avg, -1.0f, 1.0f);
            }
        }
        else if (!isFloat && bytesPerSample == 4) // 32-bit signed integer PCM
        {
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                int frameOffset = i * bytesPerFrame;
                for (int c = 0; c < sourceChannels; c++)
                {
                    int sampleOffset = frameOffset + (c * 4);
                    if (sampleOffset + 4 <= inputBuffer.Length && sampleOffset + 4 <= bytesRecorded)
                    {
                        int s = BitConverter.ToInt32(inputBuffer, sampleOffset);
                        sum += s / 2147483648.0f;
                    }
                }
                float avg = sum / sourceChannels;
                monoSamples[i] = Math.Clamp(avg, -1.0f, 1.0f);
            }
        }
        else if (bytesPerSample == 2) // 16-bit signed integer PCM
        {
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                int frameOffset = i * bytesPerFrame;
                for (int c = 0; c < sourceChannels; c++)
                {
                    int sampleOffset = frameOffset + (c * 2);
                    if (sampleOffset + 2 <= inputBuffer.Length && sampleOffset + 2 <= bytesRecorded)
                    {
                        short s = BitConverter.ToInt16(inputBuffer, sampleOffset);
                        sum += s / 32768.0f;
                    }
                }
                monoSamples[i] = Math.Clamp(sum / sourceChannels, -1.0f, 1.0f);
            }
        }
        else if (bytesPerSample == 3) // 24-bit signed integer PCM
        {
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                int frameOffset = i * bytesPerFrame;
                for (int c = 0; c < sourceChannels; c++)
                {
                    int sampleOffset = frameOffset + (c * 3);
                    if (sampleOffset + 3 <= inputBuffer.Length && sampleOffset + 3 <= bytesRecorded)
                    {
                        int s = (inputBuffer[sampleOffset] & 0xFF) |
                                ((inputBuffer[sampleOffset + 1] & 0xFF) << 8) |
                                ((sbyte)inputBuffer[sampleOffset + 2] << 16);
                        sum += s / 8388608.0f;
                    }
                }
                monoSamples[i] = Math.Clamp(sum / sourceChannels, -1.0f, 1.0f);
            }
        }
        else if (bytesPerSample == 1) // 8-bit unsigned PCM
        {
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                int frameOffset = i * bytesPerFrame;
                for (int c = 0; c < sourceChannels; c++)
                {
                    int sampleOffset = frameOffset + c;
                    if (sampleOffset < inputBuffer.Length && sampleOffset < bytesRecorded)
                    {
                        int s = inputBuffer[sampleOffset] - 128;
                        sum += s / 128.0f;
                    }
                }
                monoSamples[i] = Math.Clamp(sum / sourceChannels, -1.0f, 1.0f);
            }
        }
        else
        {
            return Array.Empty<byte>();
        }

        // Step 2: Resample from sourceSampleRate to TargetSampleRate (16,000 Hz) using linear interpolation
        int outputFrameCount;
        if (sourceSampleRate == TargetSampleRate)
        {
            outputFrameCount = frameCount;
        }
        else
        {
            outputFrameCount = (int)Math.Round((double)frameCount * TargetSampleRate / sourceSampleRate);
        }

        if (outputFrameCount <= 0)
        {
            return Array.Empty<byte>();
        }

        byte[] outputBuffer = new byte[outputFrameCount * 2];

        if (sourceSampleRate == TargetSampleRate)
        {
            for (int i = 0; i < outputFrameCount; i++)
            {
                float sample = monoSamples[i];
                if (float.IsNaN(sample) || float.IsSubnormal(sample)) sample = 0f;
                else sample = Math.Clamp(sample, -1.0f, 1.0f);

                short pcm = (short)Math.Clamp((int)Math.Round(sample * 32767.0f), -32768, 32767);
                outputBuffer[i * 2] = (byte)(pcm & 0xFF);
                outputBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }
        }
        else
        {
            double ratio = (double)sourceSampleRate / TargetSampleRate;
            for (int j = 0; j < outputFrameCount; j++)
            {
                double srcPos = j * ratio;
                int idx0 = (int)Math.Floor(srcPos);
                int idx1 = idx0 + 1;
                double frac = srcPos - idx0;

                float sample0 = idx0 < frameCount ? monoSamples[idx0] : monoSamples[frameCount - 1];
                float sample1 = idx1 < frameCount ? monoSamples[idx1] : sample0;

                float interpolated = (float)((1.0 - frac) * sample0 + frac * sample1);
                if (float.IsNaN(interpolated) || float.IsSubnormal(interpolated))
                {
                    interpolated = 0f;
                }
                else
                {
                    interpolated = Math.Clamp(interpolated, -1.0f, 1.0f);
                }

                short pcm = (short)Math.Clamp((int)Math.Round(interpolated * 32767.0f), -32768, 32767);
                outputBuffer[j * 2] = (byte)(pcm & 0xFF);
                outputBuffer[j * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }
        }

        return outputBuffer;
    }

    /// <inheritdoc/>
    public float[] CalculateSpectrumFft(byte[] pcm16kBuffer, int bandCount = 36)
    {
        if (bandCount <= 0) bandCount = 36;
        float[] result = new float[bandCount];

        if (pcm16kBuffer == null || pcm16kBuffer.Length < 4)
        {
            return result;
        }

        int sampleCount = pcm16kBuffer.Length / 2;
        if (sampleCount < 16)
        {
            return result;
        }

        // Determine FFT size N as a power of 2 (min 64, max 1024, <= sampleCount)
        int n = 512;
        while (n > sampleCount && n > 64)
        {
            n /= 2;
        }
        if (n < 64) n = 64;

        double[] real = new double[n];
        double[] imag = new double[n];

        // Apply Hann window: w(k) = 0.5 * (1 - cos(2*pi*k / (N - 1)))
        for (int i = 0; i < n; i++)
        {
            if (i < sampleCount)
            {
                short sample = BitConverter.ToInt16(pcm16kBuffer, i * 2);
                double normalized = sample / 32768.0;
                double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
                real[i] = normalized * hann;
            }
            else
            {
                real[i] = 0.0;
            }
            imag[i] = 0.0;
        }

        // Perform Radix-2 Cooley-Tukey FFT
        ComputeFft(real, imag, n);

        // Calculate magnitude spectrum for Nyquist range (bins 0 to n/2)
        int halfN = n / 2;
        double[] magnitudes = new double[halfN];
        for (int i = 0; i < halfN; i++)
        {
            double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / (n / 2.0);
            magnitudes[i] = mag;
        }

        // Group into logarithmic frequency bands across Nyquist (e.g., 20 Hz to 8000 Hz)
        double minFreq = 20.0;
        double maxFreq = 8000.0;
        double freqResolution = (double)TargetSampleRate / n;

        for (int b = 0; b < bandCount; b++)
        {
            double fLow = minFreq * Math.Pow(maxFreq / minFreq, (double)b / bandCount);
            double fHigh = minFreq * Math.Pow(maxFreq / minFreq, (double)(b + 1) / bandCount);

            int binStart = Math.Clamp((int)Math.Floor(fLow / freqResolution), 0, halfN - 1);
            int binEnd = Math.Clamp((int)Math.Ceiling(fHigh / freqResolution), binStart, halfN - 1);

            double sum = 0.0;
            int count = 0;
            for (int k = binStart; k <= binEnd; k++)
            {
                sum += magnitudes[k];
                count++;
            }

            double avgMag = count > 0 ? (sum / count) : 0.0;
            // Scale and normalize with soft compression for visualizer aesthetics
            float normalized;
            if (double.IsNaN(avgMag) || double.IsInfinity(avgMag))
            {
                normalized = 0.0f;
            }
            else
            {
                normalized = (float)Math.Clamp(avgMag * 3.5, 0.0, 1.0);
            }
            result[b] = normalized;
        }

        return result;
    }

    /// <inheritdoc/>
    public float CalculateRms(byte[] pcm16kBuffer)
    {
        if (pcm16kBuffer == null || pcm16kBuffer.Length < 2)
        {
            return 0.0f;
        }

        int sampleCount = pcm16kBuffer.Length / 2;
        double sumSquares = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(pcm16kBuffer, i * 2);
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        float rms = (float)Math.Sqrt(sumSquares / sampleCount);
        if (float.IsNaN(rms) || float.IsInfinity(rms))
        {
            return 0.0f;
        }
        return Math.Clamp(rms, 0.0f, 1.0f);
    }

    /// <summary>
    /// In-place Radix-2 Cooley-Tukey FFT algorithm.
    /// </summary>
    private static void ComputeFft(double[] real, double[] imag, int n)
    {
        // Bit-reversal permutation
        int j = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
            int k = n / 2;
            while (k <= j)
            {
                j -= k;
                k /= 2;
            }
            j += k;
        }

        // Cooley-Tukey decimation-in-time
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double wlenReal = Math.Cos(angle);
            double wlenImag = Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                double wReal = 1.0;
                double wImag = 0.0;
                int halfLen = len / 2;

                for (int m = 0; m < halfLen; m++)
                {
                    int uIdx = i + m;
                    int vIdx = i + m + halfLen;

                    double uReal = real[uIdx];
                    double uImag = imag[uIdx];
                    double vReal = real[vIdx] * wReal - imag[vIdx] * wImag;
                    double vImag = real[vIdx] * wImag + imag[vIdx] * wReal;

                    real[uIdx] = uReal + vReal;
                    imag[uIdx] = uImag + vImag;
                    real[vIdx] = uReal - vReal;
                    imag[vIdx] = uImag - vImag;

                    double nextWReal = wReal * wlenReal - wImag * wlenImag;
                    double nextWImag = wReal * wlenImag + wImag * wlenReal;
                    wReal = nextWReal;
                    wImag = nextWImag;
                }
            }
        }
    }
}
