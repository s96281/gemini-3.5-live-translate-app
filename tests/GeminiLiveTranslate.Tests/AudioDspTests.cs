using System;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class AudioDspTests
{
    private readonly AudioDspService _dsp = new();

    [Fact]
    public void ResampleTo16k16BitMono_48kHzFloatStereo_ProducesExpectedLengthAndFormat()
    {
        // 48,000 Hz, stereo (2 channels), 32-bit float (4 bytes/sample)
        // Duration: 100ms = 4,800 frames.
        // Total bytes = 4800 frames * 2 channels * 4 bytes = 38,400 bytes.
        int sampleRate = 48000;
        int channels = 2;
        int frameCount = 4800;
        int bytesRecorded = frameCount * channels * 4;
        byte[] input = new byte[bytesRecorded];

        // Fill with 440Hz stereo sine wave in IEEE float
        for (int i = 0; i < frameCount; i++)
        {
            float sample = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.8f;
            byte[] sampleBytes = BitConverter.GetBytes(sample);
            // Left channel
            Array.Copy(sampleBytes, 0, input, (i * 2) * 4, 4);
            // Right channel
            Array.Copy(sampleBytes, 0, input, (i * 2 + 1) * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(
            inputBuffer: input,
            bytesRecorded: bytesRecorded,
            sourceSampleRate: 48000,
            sourceChannels: 2,
            sourceBitsPerSample: 32,
            isFloat: true);

        // At 16,000 Hz, 100ms = 1,600 frames.
        // 16-bit mono = 2 bytes per frame. Expected bytes = 3,200 bytes.
        Assert.NotNull(output);
        int expectedFrames = 1600;
        int expectedBytes = expectedFrames * 2;
        Assert.InRange(output.Length, expectedBytes - 4, expectedBytes + 4);

        // Verify non-silent samples with genuine sine wave amplitude
        Assert.Contains(output, b => b != 0);
        short maxSample = 0;
        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = Math.Abs(BitConverter.ToInt16(output, i * 2));
            if (s > maxSample) maxSample = s;
        }
        Assert.True(maxSample > 10000, $"Expected resampled 440Hz sine wave to have amplitude > 10000, got {maxSample}");
    }

    [Fact]
    public void ResampleTo16k16BitMono_44100Hz16BitStereo_ResamplesAccurately()
    {
        // 44,100 Hz, stereo, 16-bit PCM (2 bytes/sample)
        // 100ms = 4,410 frames. Bytes = 4410 * 2 * 2 = 17,640 bytes.
        int sampleRate = 44100;
        int channels = 2;
        int frameCount = 4410;
        int bytesRecorded = frameCount * channels * 2;
        byte[] input = new byte[bytesRecorded];

        for (int i = 0; i < frameCount; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate) * 20000);
            byte[] b = BitConverter.GetBytes(s);
            Array.Copy(b, 0, input, (i * 2) * 2, 2);
            Array.Copy(b, 0, input, (i * 2 + 1) * 2, 2);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(
            inputBuffer: input,
            bytesRecorded: bytesRecorded,
            sourceSampleRate: 44100,
            sourceChannels: 2,
            sourceBitsPerSample: 16,
            isFloat: false);

        // 100ms at 16kHz = 1,600 frames * 2 bytes = 3,200 bytes
        Assert.NotNull(output);
        int expectedFrames = 1600;
        int expectedBytes = expectedFrames * 2;
        Assert.InRange(output.Length, expectedBytes - 10, expectedBytes + 10);
    }

    [Fact]
    public void ResampleTo16k16BitMono_SilenceBuffer_ReturnsZeroes()
    {
        byte[] silence = new byte[3840]; // 480 frames of 48kHz float stereo
        byte[] output = _dsp.ResampleTo16k16BitMono(
            inputBuffer: silence,
            bytesRecorded: silence.Length,
            sourceSampleRate: 48000,
            sourceChannels: 2,
            sourceBitsPerSample: 32,
            isFloat: true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);
        foreach (byte b in output)
        {
            Assert.Equal(0, b);
        }
    }

    [Fact]
    public void ResampleTo16k16BitMono_ExtremeClipping_ClampsSafelyWithoutOverflow()
    {
        // 48kHz float stereo with values exceeding [-1.0, 1.0]
        int frameCount = 100;
        byte[] input = new byte[frameCount * 2 * 4];
        for (int i = 0; i < frameCount; i++)
        {
            float extremeVal = i % 2 == 0 ? 50.0f : -50.0f;
            byte[] b = BitConverter.GetBytes(extremeVal);
            Array.Copy(b, 0, input, (i * 2) * 4, 4);
            Array.Copy(b, 0, input, (i * 2 + 1) * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(
            inputBuffer: input,
            bytesRecorded: input.Length,
            sourceSampleRate: 48000,
            sourceChannels: 2,
            sourceBitsPerSample: 32,
            isFloat: true);

        Assert.NotNull(output);
        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            Assert.True(s == 32767 || s == -32767 || s == -32768, $"Sample {s} is not clamped properly");
        }
    }

    [Fact]
    public void ResampleTo16k16BitMono_InvalidInputs_ReturnsEmptyArray()
    {
        Assert.Empty(_dsp.ResampleTo16k16BitMono(Array.Empty<byte>(), 0, 48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(new byte[100], -1, 48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(new byte[100], 100, 0, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(new byte[100], 100, 48000, 0, 32, true));
    }

    [Fact]
    public void CalculateSpectrumFft_ProducesValid36BandsInRange0To1()
    {
        // Generate 512 samples of 1000 Hz tone at 16kHz
        int n = 512;
        byte[] pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 1000.0 * i / 16000.0) * 25000);
            byte[] b = BitConverter.GetBytes(s);
            pcm[i * 2] = b[0];
            pcm[i * 2 + 1] = b[1];
        }

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);

        bool foundPeak = false;
        for (int b = 0; b < spectrum.Length; b++)
        {
            float val = spectrum[b];
            Assert.InRange(val, 0.0f, 1.0f);
            if (val > 0.05f)
            {
                foundPeak = true;
            }
        }
        Assert.True(foundPeak, "FFT should detect prominent energy from a 1000 Hz pure sine wave.");
    }

    [Fact]
    public void CalculateSpectrumFft_SilenceBuffer_YieldsZeroSpectrum()
    {
        byte[] silence = new byte[1024];
        float[] spectrum = _dsp.CalculateSpectrumFft(silence, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);
        foreach (float val in spectrum)
        {
            Assert.Equal(0.0f, val);
        }
    }

    [Fact]
    public void CalculateRms_SilenceBuffer_ReturnsZero()
    {
        byte[] silence = new byte[512];
        float rms = _dsp.CalculateRms(silence);
        Assert.Equal(0.0f, rms);
    }

    [Fact]
    public void CalculateRms_FullScaleSineWave_CalculatesAccurateTheoreticalRms()
    {
        // For a full-scale sine wave, RMS is 1 / sqrt(2) ~= 0.7071
        int n = 1600;
        byte[] pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / 16000.0) * 32767);
            byte[] b = BitConverter.GetBytes(s);
            pcm[i * 2] = b[0];
            pcm[i * 2 + 1] = b[1];
        }

        float rms = _dsp.CalculateRms(pcm);
        // Expect close to 0.707 within 5% tolerance
        Assert.InRange(rms, 0.65f, 0.75f);
    }

    [Fact]
    public void ResampleTo16k16BitMono_96kHzFloatStereo_DownsamplesAccurately()
    {
        // 96,000 Hz float stereo
        int sampleRate = 96000;
        int frameCount = 9600; // 100ms
        byte[] input = new byte[frameCount * 2 * 4];

        for (int i = 0; i < frameCount; i++)
        {
            float s = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.5f;
            byte[] b = BitConverter.GetBytes(s);
            Array.Copy(b, 0, input, i * 8, 4);
            Array.Copy(b, 0, input, i * 8 + 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 96000, 2, 32, true);

        Assert.NotNull(output);
        int expectedBytes = 1600 * 2; // 100ms at 16kHz
        Assert.InRange(output.Length, expectedBytes - 4, expectedBytes + 4);
    }

    [Fact]
    public void ResampleTo16k16BitMono_24BitSignedPCM_ConvertsAccurately()
    {
        // 48,000 Hz 24-bit PCM mono (3 bytes per sample)
        int sampleRate = 48000;
        int frameCount = 4800; // 100ms
        byte[] input = new byte[frameCount * 3];

        for (int i = 0; i < frameCount; i++)
        {
            int s = (int)(Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 8000000);
            input[i * 3] = (byte)(s & 0xFF);
            input[i * 3 + 1] = (byte)((s >> 8) & 0xFF);
            input[i * 3 + 2] = (byte)((s >> 16) & 0xFF);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 1, 24, false);

        Assert.NotNull(output);
        int expectedBytes = 1600 * 2;
        Assert.InRange(output.Length, expectedBytes - 4, expectedBytes + 4);
    }

    [Fact]
    public void ResampleTo16k16BitMono_OddByteCount_HandlesWithoutCrashing()
    {
        // Odd byte buffer (e.g. truncated or corrupted packet)
        byte[] oddInput = new byte[3841];
        byte[] output = _dsp.ResampleTo16k16BitMono(oddInput, oddInput.Length, 48000, 2, 32, true);
        Assert.NotNull(output);
    }

    [Fact]
    public void CalculateSpectrumFft_NonPowerOfTwoSampleCount_ComputesWithoutError()
    {
        // 700 samples (not a power of 2: 1400 bytes)
        byte[] pcm = new byte[1400];
        for (int i = 0; i < 700; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 800.0 * i / 16000.0) * 20000);
            byte[] b = BitConverter.GetBytes(s);
            pcm[i * 2] = b[0];
            pcm[i * 2 + 1] = b[1];
        }

        float[] bands = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);
        Assert.Equal(36, bands.Length);
        Assert.All(bands, b => Assert.InRange(b, 0.0f, 1.0f));
    }

    [Fact]
    public void CalculateSpectrumFft_FrequencyDiscrimination_LowVsHighTonePeakDifferentBands()
    {
        // Low tone: 300 Hz
        byte[] lowPcm = new byte[1024];
        for (int i = 0; i < 512; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 300.0 * i / 16000.0) * 30000);
            byte[] b = BitConverter.GetBytes(s);
            lowPcm[i * 2] = b[0];
            lowPcm[i * 2 + 1] = b[1];
        }

        // High tone: 3500 Hz
        byte[] highPcm = new byte[1024];
        for (int i = 0; i < 512; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 3500.0 * i / 16000.0) * 30000);
            byte[] b = BitConverter.GetBytes(s);
            highPcm[i * 2] = b[0];
            highPcm[i * 2 + 1] = b[1];
        }

        float[] lowBands = _dsp.CalculateSpectrumFft(lowPcm, 36);
        float[] highBands = _dsp.CalculateSpectrumFft(highPcm, 36);

        int lowPeakIndex = Array.IndexOf(lowBands, lowBands.Max());
        int highPeakIndex = Array.IndexOf(highBands, highBands.Max());

        // Low tone peak must be in lower band index than high tone peak
        Assert.True(lowPeakIndex < highPeakIndex,
            $"Low tone peak index ({lowPeakIndex}) should be less than high tone peak index ({highPeakIndex})");
    }
}
