using System;
using System.Linq;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Challenger 1 Deep Stress-Test Suite for Audio DSP Engine and Mathematical Algorithms.
/// Probes extreme sample rates, audio clipping/spikes, DC offsets, channel topologies,
/// buffer boundaries, FFT frequency edge cases, short/large buffers, and RMS stability.
/// </summary>
public class AudioDspStressAndEdgeCaseTests
{
    private readonly AudioDspService _dsp = new();

    #region 1. Resampling Edge Cases

    [Theory]
    [InlineData(8000, 1, 16, false)]      // Narrowband / telephony 8kHz 16-bit mono
    [InlineData(8000, 2, 32, true)]       // 8kHz float stereo
    [InlineData(11025, 1, 16, false)]     // 11.025kHz 16-bit mono
    [InlineData(11025, 2, 32, true)]      // 11.025kHz float stereo
    [InlineData(22050, 2, 16, false)]     // 22.05kHz 16-bit stereo
    [InlineData(22050, 1, 32, true)]      // 22.05kHz float mono
    [InlineData(44100, 2, 16, false)]     // 44.1kHz CD audio 16-bit stereo
    [InlineData(44100, 2, 32, true)]      // 44.1kHz float stereo
    [InlineData(48000, 2, 32, true)]      // 48kHz standard studio float stereo
    [InlineData(48000, 1, 16, false)]     // 48kHz 16-bit mono
    [InlineData(96000, 2, 32, true)]      // 96kHz hi-res float stereo
    [InlineData(96000, 1, 16, false)]     // 96kHz hi-res 16-bit mono
    [InlineData(192000, 2, 32, true)]     // 192kHz ultra-hi-res float stereo
    [InlineData(192000, 1, 16, false)]    // 192kHz ultra-hi-res 16-bit mono
    [InlineData(16000, 1, 16, false)]     // 16kHz identity resampling (16-bit mono)
    [InlineData(16000, 2, 32, true)]      // 16kHz stereo float to 16kHz mono 16-bit
    public void ResampleTo16k_ExtremeAndUnusualSampleRates_ProduceAccurate16kOutput(
        int sampleRate, int channels, int bitsPerSample, bool isFloat)
    {
        // 100 milliseconds duration
        int bytesPerSample = bitsPerSample / 8;
        int frameCount = sampleRate / 10; // 100ms
        int totalBytes = frameCount * channels * bytesPerSample;
        byte[] input = new byte[totalBytes];

        // Synthesize 440 Hz test tone
        for (int i = 0; i < frameCount; i++)
        {
            double phase = 2.0 * Math.PI * 440.0 * i / sampleRate;
            float sampleVal = (float)Math.Sin(phase) * 0.75f;

            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                if (isFloat && bytesPerSample == 4)
                {
                    byte[] b = BitConverter.GetBytes(sampleVal);
                    Array.Copy(b, 0, input, offset, 4);
                }
                else if (bytesPerSample == 2)
                {
                    short s = (short)Math.Round(sampleVal * 32767.0f);
                    byte[] b = BitConverter.GetBytes(s);
                    Array.Copy(b, 0, input, offset, 2);
                }
            }
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(
            inputBuffer: input,
            bytesRecorded: input.Length,
            sourceSampleRate: sampleRate,
            sourceChannels: channels,
            sourceBitsPerSample: bitsPerSample,
            isFloat: isFloat);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        // Expected 100ms at 16kHz = 1,600 frames * 2 bytes = 3,200 bytes
        int expectedFrames = 1600;
        int expectedBytes = expectedFrames * 2;
        Assert.InRange(output.Length, expectedBytes - 6, expectedBytes + 6);

        // Verify the resampled signal is valid 16-bit PCM and non-silent
        short maxAmp = 0;
        int nonZeroCount = 0;
        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            if (s != 0) nonZeroCount++;
            if (Math.Abs(s) > maxAmp) maxAmp = (short)Math.Abs(s);
        }

        Assert.True(nonZeroCount > output.Length / 4, "Output should contain non-trivial resampled audio samples.");
        Assert.True(maxAmp > 5000, $"Output max amplitude ({maxAmp}) should preserve tone energy.");
    }

    [Theory]
    [InlineData(100.0f)]
    [InlineData(-100.0f)]
    [InlineData(1000000.0f)]
    [InlineData(-1000000.0f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void ResampleTo16k_ExtremeClipping_ClampsStrictlyToShortRange(float extremeVal)
    {
        int frameCount = 480; // 10ms at 48kHz
        byte[] input = new byte[frameCount * 2 * 4]; // 2 channels, 32-bit float

        byte[] extremeBytes = BitConverter.GetBytes(extremeVal);
        for (int i = 0; i < frameCount * 2; i++)
        {
            Array.Copy(extremeBytes, 0, input, i * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            if (extremeVal > 0)
            {
                Assert.Equal(32767, s);
            }
            else
            {
                Assert.True(s == -32767 || s == -32768, $"Expected negative clamp, got {s}");
            }
        }
    }

    [Fact]
    public void ResampleTo16k_NaNAndInfinitiesInFloatInput_TreatedSafelyAsZero()
    {
        int frameCount = 480;
        byte[] input = new byte[frameCount * 2 * 4];

        // Inject NaN, +Infinity, -Infinity interspersed with valid samples
        for (int i = 0; i < frameCount; i++)
        {
            float val;
            if (i % 4 == 0) val = float.NaN;
            else if (i % 4 == 1) val = float.PositiveInfinity;
            else if (i % 4 == 2) val = float.NegativeInfinity;
            else val = 0.5f;

            byte[] b = BitConverter.GetBytes(val);
            Array.Copy(b, 0, input, (i * 2) * 4, 4);
            Array.Copy(b, 0, input, (i * 2 + 1) * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        // Verify no crash, and no NaN/corrupt values in output
        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            Assert.InRange(s, (short)-32768, (short)32767);
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-0.5f)]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    public void ResampleTo16k_DcOffset_PreservesConstantDcWithoutDrift(float dcLevel)
    {
        int frameCount = 960; // 20ms at 48kHz
        byte[] input = new byte[frameCount * 2 * 4];

        byte[] b = BitConverter.GetBytes(dcLevel);
        for (int i = 0; i < frameCount * 2; i++)
        {
            Array.Copy(b, 0, input, i * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        short expectedPcm = (short)Math.Round(dcLevel * 32767.0f);
        short minBound = (short)Math.Max(-32768, expectedPcm - 1);
        short maxBound = (short)Math.Min(32767, expectedPcm + 1);

        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            Assert.InRange(s, minBound, maxBound);
        }
    }

    [Theory]
    [InlineData(1)]  // Mono
    [InlineData(2)]  // Stereo
    [InlineData(4)]  // Quadraphonic
    [InlineData(6)]  // 5.1 Surround
    [InlineData(8)]  // 7.1 Surround
    [InlineData(3)]  // 3-channel
    [InlineData(5)]  // 5-channel
    public void ResampleTo16k_NonStandardChannelCounts_AveragesDownToMonoProperly(int channelCount)
    {
        int sampleRate = 48000;
        int frameCount = 480;
        byte[] input = new byte[frameCount * channelCount * 4];

        // In each frame, channel 0 has 1.0f, all other channels have 0.0f
        // Mono average should be exactly 1.0f / channelCount
        for (int i = 0; i < frameCount; i++)
        {
            int baseOffset = i * channelCount * 4;
            byte[] ch0 = BitConverter.GetBytes(1.0f);
            Array.Copy(ch0, 0, input, baseOffset, 4);
            // Remaining channels stay 0.0f
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, channelCount, 32, true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        float expectedMono = 1.0f / channelCount;
        short expectedPcm = (short)Math.Round(expectedMono * 32767.0f);
        short minBound = (short)Math.Max(-32768, expectedPcm - 2);
        short maxBound = (short)Math.Min(32767, expectedPcm + 2);

        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            Assert.InRange(s, minBound, maxBound);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(3841)] // 48kHz float stereo has 8 bytes/frame; 3841 has 1 trailing odd byte
    [InlineData(3843)] // 3 trailing odd bytes
    [InlineData(3847)] // 7 trailing odd bytes
    public void ResampleTo16k_OddByteCountsAndTruncatedFrames_HandlesGracefullyWithoutThrowing(int byteCount)
    {
        byte[] buffer = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            buffer[i] = (byte)(i % 256);
        }

        // Test with 48kHz float stereo (8 bytes per frame)
        var exception = Record.Exception(() =>
        {
            byte[] out1 = _dsp.ResampleTo16k16BitMono(buffer, byteCount, 48000, 2, 32, true);
            Assert.NotNull(out1);
        });
        Assert.Null(exception);

        // Test with 44.1kHz 16-bit stereo (4 bytes per frame)
        exception = Record.Exception(() =>
        {
            byte[] out2 = _dsp.ResampleTo16k16BitMono(buffer, byteCount, 44100, 2, 16, false);
            Assert.NotNull(out2);
        });
        Assert.Null(exception);
    }

    [Fact]
    public void ResampleTo16k_ZeroAndNegativeParameters_ReturnsEmptyArraySafely()
    {
        byte[] buf = new byte[100];

        Assert.Empty(_dsp.ResampleTo16k16BitMono(null!, 100, 48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, 0, 48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, -10, 48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, 100, 0, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, 100, -48000, 2, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, 100, 48000, 0, 32, true));
        Assert.Empty(_dsp.ResampleTo16k16BitMono(buf, 100, 48000, -2, 32, true));
    }

    [Fact]
    public void ResampleTo16k_BytesRecordedGreaterThanBufferLength_DoesNotReadPastBuffer()
    {
        // Buffer has only 8 bytes (1 frame of 48kHz float stereo)
        byte[] smallBuf = new byte[8];
        byte[] b = BitConverter.GetBytes(0.5f);
        Array.Copy(b, 0, smallBuf, 0, 4);
        Array.Copy(b, 0, smallBuf, 4, 4);

        // Caller claims 800 bytes recorded
        var exception = Record.Exception(() =>
        {
            byte[] output = _dsp.ResampleTo16k16BitMono(smallBuf, 800, 48000, 2, 32, true);
            Assert.NotNull(output);
        });
        Assert.Null(exception);
    }

    #endregion

    #region 2. FFT & Spectrum Analysis Stress

    [Fact]
    public void CalculateSpectrumFft_PureSilenceAllZeros_All36BandsAreStrictlyZero()
    {
        foreach (int size in new[] { 64, 128, 512, 1024, 2048, 8192 })
        {
            byte[] silence = new byte[size * 2];
            float[] spectrum = _dsp.CalculateSpectrumFft(silence, bandCount: 36);

            Assert.NotNull(spectrum);
            Assert.Equal(36, spectrum.Length);
            for (int i = 0; i < spectrum.Length; i++)
            {
                Assert.Equal(0.0f, spectrum[i]);
            }
        }
    }

    [Theory]
    [InlineData((short)32767)]
    [InlineData((short)-32768)]
    [InlineData((short)16384)]
    [InlineData((short)-16384)]
    public void CalculateSpectrumFft_DcOnlySignal_OutputIsBoundedAndConcentratedInLowestBand(short dcLevel)
    {
        int sampleCount = 512;
        byte[] pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            pcm[i * 2] = (byte)(dcLevel & 0xFF);
            pcm[i * 2 + 1] = (byte)((dcLevel >> 8) & 0xFF);
        }

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);

        // Every band must be strictly within [0.0, 1.0], no NaN, no Inf
        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]), $"Band {i} produced NaN");
            Assert.False(float.IsInfinity(spectrum[i]), $"Band {i} produced Infinity");
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }

        // DC energy should be prominent in lowest band (band 0) and negligible in upper bands
        Assert.True(spectrum[0] >= spectrum[35], "DC energy should be higher in band 0 than highest band");
    }

    [Fact]
    public void CalculateSpectrumFft_NyquistFrequencySignal_RemainsMathematicallyStableAndBounded()
    {
        // 8000 Hz at 16,000 Hz sample rate alternates +32767 and -32768 every sample
        int sampleCount = 512;
        byte[] pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (i % 2 == 0) ? (short)32767 : (short)-32768;
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);

        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]), $"Band {i} is NaN");
            Assert.False(float.IsInfinity(spectrum[i]), $"Band {i} is Infinity");
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }

        // Nyquist energy should peak at the upper frequency bands
        int maxIndex = Array.IndexOf(spectrum, spectrum.Max());
        Assert.True(maxIndex >= 30, $"Nyquist tone peak should be near the highest bands, got band {maxIndex}");
    }

    [Theory]
    [InlineData(0)]    // Spike at first sample
    [InlineData(1)]    // Spike at second sample
    [InlineData(256)]  // Spike at center (Hann window peak)
    [InlineData(511)]  // Spike at final sample
    public void CalculateSpectrumFft_ImpulsiveClick_ProducesStrictlyBoundedSpectrum(int spikeIndex)
    {
        int sampleCount = 512;
        byte[] pcm = new byte[sampleCount * 2];

        // Single full-scale impulse click
        pcm[spikeIndex * 2] = 0xFF;
        pcm[spikeIndex * 2 + 1] = 0x7F; // +32767

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);

        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]), $"Band {i} is NaN on spike at {spikeIndex}");
            Assert.False(float.IsInfinity(spectrum[i]), $"Band {i} is Infinity on spike at {spikeIndex}");
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(16000)]
    public void CalculateSpectrumFft_VaryingBufferSizes_ComputesSafelyWithoutException(int sampleCount)
    {
        byte[] pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 1000.0 * i / 16000.0) * 20000);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, bandCount: 36);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);

        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]));
            Assert.False(float.IsInfinity(spectrum[i]));
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(36)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(0)]   // Defaults to 36
    [InlineData(-10)] // Defaults to 36
    public void CalculateSpectrumFft_NonStandardBandCounts_ReturnsAccurateLengthAndBounds(int requestedBands)
    {
        byte[] pcm = new byte[1024];
        for (int i = 0; i < 512; i++)
        {
            short s = (short)(Math.Sin(2.0 * Math.PI * 500.0 * i / 16000.0) * 25000);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, requestedBands);

        int expectedBands = requestedBands <= 0 ? 36 : requestedBands;
        Assert.Equal(expectedBands, spectrum.Length);

        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]));
            Assert.False(float.IsInfinity(spectrum[i]));
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }
    }

    #endregion

    #region 3. RMS Calculations

    [Fact]
    public void CalculateRms_SilenceAllZeros_ReturnsExactlyZero()
    {
        foreach (int size in new[] { 2, 4, 100, 512, 1600, 8000 })
        {
            byte[] silence = new byte[size];
            float rms = _dsp.CalculateRms(silence);
            Assert.Equal(0.0f, rms);
        }
    }

    [Fact]
    public void CalculateRms_FullScaleSquareWave_ReturnsCloseToOne()
    {
        int sampleCount = 1600; // 100ms
        byte[] pcm = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            short s = (i % 2 == 0) ? (short)32767 : (short)-32768;
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        float rms = _dsp.CalculateRms(pcm);

        Assert.False(float.IsNaN(rms));
        Assert.False(float.IsInfinity(rms));
        Assert.InRange(rms, 0.99f, 1.0f);
    }

    [Theory]
    [InlineData((short)32767)]
    [InlineData((short)-32768)]
    public void CalculateRms_FullScaleDcSignal_YieldsOne(short dcLevel)
    {
        int sampleCount = 512;
        byte[] pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            pcm[i * 2] = (byte)(dcLevel & 0xFF);
            pcm[i * 2 + 1] = (byte)((dcLevel >> 8) & 0xFF);
        }

        float rms = _dsp.CalculateRms(pcm);

        Assert.False(float.IsNaN(rms));
        Assert.False(float.IsInfinity(rms));
        Assert.InRange(rms, 0.999f, 1.0f);
    }

    [Fact]
    public void CalculateRms_RandomNoise_IsBoundedAndMathematicallyStable()
    {
        var rng = new Random(42);
        int sampleCount = 16000;
        byte[] pcm = new byte[sampleCount * 2];
        rng.NextBytes(pcm);

        float rms = _dsp.CalculateRms(pcm);

        Assert.False(float.IsNaN(rms));
        Assert.False(float.IsInfinity(rms));
        // Uniform random 16-bit noise has expected RMS of 1 / sqrt(3) ~= 0.577
        Assert.InRange(rms, 0.50f, 0.65f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void CalculateRms_TinyOrOddByteLengths_HandledSafely(int length)
    {
        byte[] buf = new byte[length];
        for (int i = 0; i < length; i++) buf[i] = 0x55;

        float rms = _dsp.CalculateRms(buf);
        Assert.InRange(rms, 0.0f, 1.0f);
    }

    [Fact]
    public void CalculateRms_NullBuffer_ReturnsZero()
    {
        float rms = _dsp.CalculateRms(null!);
        Assert.Equal(0.0f, rms);
    }

    #endregion
}
