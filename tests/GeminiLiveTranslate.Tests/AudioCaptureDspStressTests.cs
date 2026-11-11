using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.App.Audio;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using NAudio.Wave;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Adversarial stress testing and edge-case verification for WasapiAudioCaptureEngine
/// and the native audio capture DSP pipeline.
/// Covers:
/// - Extreme sample rates (8k to 192k)
/// - Multi-channel topologies (mono, stereo, 5.1, 7.1)
/// - Non-standard bit depths (8-bit unsigned, 16-bit PCM, 24-bit PCM, 32-bit float, 32-bit int)
/// - Extreme float amplitudes (+100.0, -100.0, NaNs, infinities, denormals)
/// - Micro-buffer fragmentation (1-byte, 7-byte, odd-sized buffers, rapid bursts of 1000 micro-buffers)
/// - Exact 3200-byte chunk assembly without off-by-one errors
/// - FFT and RMS robustness under malformed audio
/// - Atomic source switching under heavy streaming
/// </summary>
public class AudioCaptureDspStressTests
{
    private readonly AudioDspService _dsp = new();

    #region SECTION 1: Extreme Sample Rates & Resampling Accuracy

    [Theory]
    [InlineData(8000)]
    [InlineData(11025)]
    [InlineData(22050)]
    [InlineData(32000)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(88200)]
    [InlineData(96000)]
    [InlineData(192000)]
    public async Task ExtremeSampleRates_EngineResamplesToExact3200ByteChunks(int sampleRate)
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed exactly 200ms of audio (expected exactly 2 chunks of 3200 bytes)
        byte[] audio200ms = GenerateSyntheticAudio(sampleRate, channels: 2, bitsPerSample: 32, isFloat: true, durationSec: 0.200, freqHz: 440.0);
        client.FeedAudio(audio200ms, audio200ms.Length);

        Assert.Equal(2, receivedChunks.Count);
        Assert.Equal(2, engine.ChunksCapturedCount);
        foreach (var chunk in receivedChunks)
        {
            Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunk.Length);
            Assert.Equal(3200, chunk.Length);

            // Verify non-zero resampled signal energy
            float rms = _dsp.CalculateRms(chunk);
            Assert.True(rms > 0.05f, $"Resampled chunk should retain signal energy, got RMS={rms} at rate {sampleRate}");
        }

        await engine.StopAsync();
    }

    [Theory]
    [InlineData(8000, 440.0)]
    [InlineData(11025, 1000.0)]
    [InlineData(22050, 1500.0)]
    [InlineData(32000, 2000.0)]
    [InlineData(44100, 440.0)]
    [InlineData(48000, 1000.0)]
    [InlineData(88200, 3000.0)]
    [InlineData(96000, 4000.0)]
    [InlineData(192000, 5000.0)]
    public void ExtremeSampleRates_DspDirectResampling_ProducesAccurateFrameCounts(int sampleRate, double freqHz)
    {
        // 100ms at source sample rate
        int frameCount = sampleRate / 10;
        byte[] input = GenerateSyntheticAudio(sampleRate, channels: 2, bitsPerSample: 32, isFloat: true, durationSec: 0.100, freqHz: freqHz);

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, 2, 32, isFloat: true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        // Output frames must be 1600 (100ms at 16kHz) +/- 2 frames rounding allowance
        int outputFrames = output.Length / 2;
        Assert.InRange(outputFrames, 1598, 1602);

        float rms = _dsp.CalculateRms(output);
        Assert.True(rms > 0.05f, $"Direct resampled output must preserve energy for rate {sampleRate}, got RMS={rms}");
    }

    #endregion

    #region SECTION 2: Multi-Channel Formats (Mono, Stereo, 5.1, 7.1)

    [Theory]
    [InlineData(1)]  // Mono
    [InlineData(2)]  // Stereo
    [InlineData(6)]  // 5.1 Surround (6 channels)
    [InlineData(8)]  // 7.1 Surround (8 channels)
    public async Task MultiChannelFormats_EngineDownmixesToSingleChannelMono(int channels)
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedEvents = new List<AudioChunkEventArgs>();
        engine.AudioChunkAvailable += (s, e) => receivedEvents.Add(e);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed 100ms
        byte[] buffer = GenerateSyntheticAudio(48000, channels, bitsPerSample: 32, isFloat: true, durationSec: 0.100, freqHz: 880.0, amplitude: 0.7f);
        client.FeedAudio(buffer, buffer.Length);

        Assert.Single(receivedEvents);
        var chunk = receivedEvents[0];
        Assert.Equal(3200, chunk.PcmChunk.Length);
        Assert.Equal(16000, chunk.SampleRate);
        Assert.Equal(1, chunk.Channels);

        // Check loudness
        float rms = _dsp.CalculateRms(chunk.PcmChunk);
        Assert.True(rms > 0.05f, $"RMS should be positive for {channels}-channel audio, got {rms}");

        await engine.StopAsync();
    }

    [Fact]
    public void MultiChannel_PhaseCancellation_StereoOppositePhaseYieldsSilence()
    {
        int sampleRate = 48000;
        int frameCount = 4800; // 100ms
        byte[] input = new byte[frameCount * 2 * 4]; // Stereo float

        for (int i = 0; i < frameCount; i++)
        {
            float left = 0.8f;
            float right = -0.8f; // Exactly out of phase

            int offset = i * 8;
            Buffer.BlockCopy(BitConverter.GetBytes(left), 0, input, offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(right), 0, input, offset + 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, 2, 32, isFloat: true);

        Assert.NotNull(output);
        float rms = _dsp.CalculateRms(output);
        Assert.Equal(0.0f, rms, 3); // Downmix (left + right)/2 = 0
    }

    [Fact]
    public void MultiChannel_Surround71_SingleActiveSurroundChannel_AveragesCleanly()
    {
        int sampleRate = 48000;
        int channels = 8; // 7.1 surround
        int frameCount = 4800;
        byte[] input = new byte[frameCount * channels * 4]; // 32-bit float

        // Channel 7 (Surround Back Right) has 0.8f; all other 7 channels are silent (0.0f)
        float activeVal = 0.8f;
        byte[] valBytes = BitConverter.GetBytes(activeVal);
        for (int i = 0; i < frameCount; i++)
        {
            int ch7Offset = (i * channels + 7) * 4;
            Buffer.BlockCopy(valBytes, 0, input, ch7Offset, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, sampleRate, channels, 32, isFloat: true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        // Expected mono sample = 0.8f / 8 = 0.1f -> 3277 in 16-bit PCM
        short expectedPcm = (short)Math.Round(0.1f * 32767.0f);
        short firstSample = BitConverter.ToInt16(output, 0);
        Assert.InRange(firstSample, expectedPcm - 2, expectedPcm + 2);
    }

    #endregion

    #region SECTION 3: Non-Standard Bit Depths (8-bit, 16-bit, 24-bit, 32-bit float, 32-bit int)

    [Fact]
    public async Task BitDepth_8BitUnsigned_EngineAndDspProcessAccurately()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.MicClient;
        client.WaveFormat = new WaveFormat(16000, 8, 1); // 8-bit unsigned PCM mono

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;

        await engine.StartAsync(AudioSource.Microphone);

        // 100ms at 16kHz 8-bit mono = 1600 bytes
        // Generate sine wave biased around 128 (center)
        byte[] input8Bit = new byte[1600];
        for (int i = 0; i < 1600; i++)
        {
            double phase = 2.0 * Math.PI * 440.0 * i / 16000.0;
            int val = 128 + (int)(100 * Math.Sin(phase));
            input8Bit[i] = (byte)Math.Clamp(val, 0, 255);
        }

        client.FeedAudio(input8Bit, input8Bit.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        float rms = _dsp.CalculateRms(chunkCaptured);
        Assert.True(rms > 0.1f, $"8-bit sine wave should yield positive RMS, got {rms}");

        await engine.StopAsync();
    }

    [Fact]
    public async Task BitDepth_24BitSignedPCM_EngineAndDspProcessAccurately()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.MicClient;
        // 24-bit signed PCM stereo at 48kHz
        client.WaveFormat = new WaveFormat(48000, 24, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;

        await engine.StartAsync(AudioSource.Microphone);

        // 100ms at 48kHz stereo 24-bit = 4800 frames * 2 ch * 3 bytes = 28,800 bytes
        byte[] input24Bit = GenerateSyntheticAudio(48000, channels: 2, bitsPerSample: 24, isFloat: false, durationSec: 0.100, freqHz: 1000.0);
        Assert.Equal(28800, input24Bit.Length);

        client.FeedAudio(input24Bit, input24Bit.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        float rms = _dsp.CalculateRms(chunkCaptured);
        Assert.True(rms > 0.05f, $"24-bit sine wave should yield positive RMS, got {rms}");

        await engine.StopAsync();
    }

    [Fact]
    public async Task BitDepth_32BitSignedIntegerPCM_EngineAndDspProcessAccurately()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        // 32-bit signed integer PCM stereo at 48kHz (Encoding = WaveFormatEncoding.Pcm)
        client.WaveFormat = new WaveFormat(48000, 32, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 100ms at 48kHz stereo 32-bit int = 4800 frames * 2 ch * 4 bytes = 38,400 bytes
        byte[] input32BitInt = GenerateSyntheticAudio(48000, channels: 2, bitsPerSample: 32, isFloat: false, durationSec: 0.100, freqHz: 1000.0);
        Assert.Equal(38400, input32BitInt.Length);

        client.FeedAudio(input32BitInt, input32BitInt.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        float rms = _dsp.CalculateRms(chunkCaptured);
        Assert.True(rms > 0.05f, $"32-bit integer PCM sine wave should yield positive RMS, got {rms}");

        await engine.StopAsync();
    }

    [Fact]
    public async Task BitDepth_32BitFloat_WaveFormatExtensible_EngineProcessesAccurately()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        // WaveFormatExtensible with KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] inputFloat = GenerateSyntheticAudio(48000, channels: 2, bitsPerSample: 32, isFloat: true, durationSec: 0.100, freqHz: 1000.0);
        client.FeedAudio(inputFloat, inputFloat.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        float rms = _dsp.CalculateRms(chunkCaptured);
        Assert.True(rms > 0.05f, $"32-bit float should yield positive RMS, got {rms}");

        await engine.StopAsync();
    }

    #endregion

    #region SECTION 4: Extreme Float Spikes, NaNs, Infinities & Denormals

    [Theory]
    [InlineData(100.0f)]
    [InlineData(-100.0f)]
    [InlineData(999999.0f)]
    [InlineData(-999999.0f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public async Task ExtremeFloatSpikes_EngineClampsStrictlyWithoutCrashing(float spikeAmplitude)
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 100ms buffer filled with extreme values
        int frameCount = 4800;
        byte[] input = new byte[frameCount * 8];
        byte[] spikeBytes = BitConverter.GetBytes(spikeAmplitude);
        for (int i = 0; i < frameCount * 2; i++)
        {
            Buffer.BlockCopy(spikeBytes, 0, input, i * 4, 4);
        }

        client.FeedAudio(input, input.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        // All samples must be clamped to -32768..32767
        for (int i = 0; i < chunkCaptured.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(chunkCaptured, i * 2);
            if (spikeAmplitude > 0)
            {
                Assert.Equal(32767, s);
            }
            else
            {
                Assert.True(s == -32767 || s == -32768, $"Expected negative limit clamp, got {s}");
            }
        }

        await engine.StopAsync();
    }

    [Fact]
    public async Task ExtremeFloat_NaNsAndInfinities_FlushedSafelyToZeroWithoutCrash()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? chunkCaptured = null;
        float[]? spectrumCalculated = null;

        engine.OnAudioChunkCaptured += (s, chunk) => chunkCaptured = chunk;
        engine.OnSpectrumCalculated += (s, spec) => spectrumCalculated = spec;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Inject alternating NaN, +Inf, -Inf
        int frameCount = 4800;
        byte[] input = new byte[frameCount * 8];
        for (int i = 0; i < frameCount; i++)
        {
            float val = (i % 3 == 0) ? float.NaN : (i % 3 == 1) ? float.PositiveInfinity : float.NegativeInfinity;
            byte[] valBytes = BitConverter.GetBytes(val);
            Buffer.BlockCopy(valBytes, 0, input, i * 8, 4);
            Buffer.BlockCopy(valBytes, 0, input, i * 8 + 4, 4);
        }

        client.FeedAudio(input, input.Length);

        Assert.NotNull(chunkCaptured);
        Assert.Equal(3200, chunkCaptured.Length);

        // Spectrum must be calculated without NaN or Inf
        Assert.NotNull(spectrumCalculated);
        Assert.Equal(36, spectrumCalculated.Length);
        foreach (var band in spectrumCalculated)
        {
            Assert.False(float.IsNaN(band), "Spectrum band should not be NaN");
            Assert.False(float.IsInfinity(band), "Spectrum band should not be Infinity");
            Assert.InRange(band, 0.0f, 1.0f);
        }

        await engine.StopAsync();
    }

    [Fact]
    public void ExtremeFloat_DenormalSubnormals_FlushedToZeroWithoutStall()
    {
        int frameCount = 4800; // 100ms
        byte[] input = new byte[frameCount * 8];

        // Fill with subnormal float numbers (e.g. float.Epsilon = 1.4e-45f)
        byte[] denormalBytes = BitConverter.GetBytes(float.Epsilon);
        for (int i = 0; i < frameCount * 2; i++)
        {
            Buffer.BlockCopy(denormalBytes, 0, input, i * 4, 4);
        }

        byte[] output = _dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, isFloat: true);

        Assert.NotNull(output);
        Assert.NotEmpty(output);

        float rms = _dsp.CalculateRms(output);
        Assert.Equal(0.0f, rms); // Denormals flushed to zero
    }

    #endregion

    #region SECTION 5: Micro-Buffer Fragmentation & 1000-Burst Stress

    [Fact]
    public async Task MicroBuffer_1ByteBuffers_AccumulatesAndEmitsExact3200ByteChunk()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 100ms of 48kHz stereo float = 38,400 bytes.
        // Feed in 38,400 single-byte calls!
        byte[] fullAudio = GenerateSyntheticAudio(48000, 2, 32, isFloat: true, durationSec: 0.100, freqHz: 1000.0);
        Assert.Equal(38400, fullAudio.Length);

        byte[] singleByte = new byte[1];
        for (int i = 0; i < fullAudio.Length; i++)
        {
            singleByte[0] = fullAudio[i];
            client.FeedAudio(singleByte, 1);
        }

        // Must emit EXACTLY 1 chunk of EXACTLY 3200 bytes without dropping bytes or off-by-one errors!
        Assert.Single(receivedChunks);
        Assert.Equal(3200, receivedChunks[0].Length);
        Assert.Equal(1, engine.ChunksCapturedCount);

        float rms = _dsp.CalculateRms(receivedChunks[0]);
        Assert.True(rms > 0.05f, $"1-byte assembled chunk should retain signal energy, got RMS={rms}");

        await engine.StopAsync();
    }

    [Fact]
    public async Task MicroBuffer_7ByteBuffers_AccumulatesOddSizesCleanly()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed 38,400 bytes in 7-byte slices
        byte[] fullAudio = GenerateSyntheticAudio(48000, 2, 32, isFloat: true, durationSec: 0.100, freqHz: 1000.0);
        int offset = 0;
        while (offset < fullAudio.Length)
        {
            int sliceSize = Math.Min(7, fullAudio.Length - offset);
            byte[] slice = new byte[sliceSize];
            Buffer.BlockCopy(fullAudio, offset, slice, 0, sliceSize);
            client.FeedAudio(slice, sliceSize);
            offset += sliceSize;
        }

        Assert.Single(receivedChunks);
        Assert.Equal(3200, receivedChunks[0].Length);
        Assert.Equal(1, engine.ChunksCapturedCount);

        await engine.StopAsync();
    }

    [Fact]
    public async Task MicroBuffer_Burst1000MicroBuffers_RapidArrival_EmitsExactChunks()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 200ms of 48kHz float stereo = 76,800 bytes (expected exactly 2 chunks of 3200 bytes)
        byte[] audio200ms = GenerateSyntheticAudio(48000, 2, 32, isFloat: true, durationSec: 0.200, freqHz: 440.0);
        Assert.Equal(76800, audio200ms.Length);

        // Divide 76,800 bytes into ~1000 micro-buffers of varying sizes (between 13 and 141 bytes)
        var rnd = new Random(1337);
        int currentOffset = 0;
        int microBufferCount = 0;

        while (currentOffset < audio200ms.Length)
        {
            int remaining = audio200ms.Length - currentOffset;
            int size = Math.Min(remaining, rnd.Next(13, 142));
            byte[] microBuf = new byte[size];
            Buffer.BlockCopy(audio200ms, currentOffset, microBuf, 0, size);
            client.FeedAudio(microBuf, size);
            currentOffset += size;
            microBufferCount++;
        }

        Assert.True(microBufferCount >= 500, $"Expected at least 500 micro-buffers in burst, got {microBufferCount}");
        Assert.Equal(2, receivedChunks.Count);
        Assert.Equal(2, engine.ChunksCapturedCount);
        Assert.Equal(3200, receivedChunks[0].Length);
        Assert.Equal(3200, receivedChunks[1].Length);

        await engine.StopAsync();
    }

    [Fact]
    public async Task MicroBuffer_OddByteCountWithPartialFrames_PreservesLeftoverAcrossFeedCalls()
    {
        var factory = new AdversarialCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); // 8 bytes per frame

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 100ms = 38,400 bytes.
        // Feed 38,403 bytes (100ms + 3 odd bytes, not a full frame)
        byte[] part1 = new byte[38403];
        Array.Fill<byte>(part1, 0x11);
        client.FeedAudio(part1, part1.Length);

        // 1 chunk should be emitted, and 3 odd bytes kept in staging buffer
        Assert.Single(receivedChunks);
        Assert.Equal(3200, receivedChunks[0].Length);

        // Now feed 38,397 bytes (38397 + 3 odd leftover = 38,400 bytes = exactly another chunk!)
        byte[] part2 = new byte[38397];
        Array.Fill<byte>(part2, 0x22);
        client.FeedAudio(part2, part2.Length);

        Assert.Equal(2, receivedChunks.Count);
        Assert.Equal(3200, receivedChunks[1].Length);
        Assert.Equal(2, engine.ChunksCapturedCount);

        await engine.StopAsync();
    }

    #endregion

    #region SECTION 6: FFT & RMS Robustness Under Extreme Audio

    [Fact]
    public void FftRobustness_PureSilence_All36BandsAreStrictlyZero()
    {
        foreach (int length in new[] { 64, 128, 512, 1600, 3200, 6400 })
        {
            byte[] silence = new byte[length];
            float[] spectrum = _dsp.CalculateSpectrumFft(silence, 36);

            Assert.Equal(36, spectrum.Length);
            for (int i = 0; i < 36; i++)
            {
                Assert.Equal(0.0f, spectrum[i]);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(1599)]
    public void FftRobustness_ImpulsesAndClicks_BandsRemainBounded(int impulseIndex)
    {
        // 100ms buffer of 1600 samples
        byte[] pcm = new byte[3200];
        // Inject single full-scale impulse
        pcm[impulseIndex * 2] = 0xFF;
        pcm[impulseIndex * 2 + 1] = 0x7F; // +32767

        float[] spectrum = _dsp.CalculateSpectrumFft(pcm, 36);

        Assert.Equal(36, spectrum.Length);
        for (int i = 0; i < 36; i++)
        {
            Assert.False(float.IsNaN(spectrum[i]));
            Assert.False(float.IsInfinity(spectrum[i]));
            Assert.InRange(spectrum[i], 0.0f, 1.0f);
        }
    }

    [Fact]
    public void FftRobustness_HighFrequencySweeps_NyquistStability()
    {
        // 100ms sine waves at 1kHz, 3kHz, 7kHz
        foreach (double freq in new[] { 1000.0, 3000.0, 7000.0 })
        {
            byte[] pcm = new byte[3200];
            for (int i = 0; i < 1600; i++)
            {
                double phase = 2.0 * Math.PI * freq * i / 16000.0;
                short sample = (short)(Math.Sin(phase) * 30000);
                pcm[i * 2] = (byte)(sample & 0xFF);
                pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            float[] spectrum = _dsp.CalculateSpectrumFft(pcm, 36);

            Assert.Equal(36, spectrum.Length);
            float maxBand = spectrum.Max();
            int maxIndex = Array.IndexOf(spectrum, maxBand);

            Assert.True(maxBand > 0.05f, $"Peak band should be detectable for {freq}Hz tone");
            // Higher frequencies should peak at higher band indices
            if (freq == 1000.0) Assert.InRange(maxIndex, 8, 24);
            else if (freq == 7000.0) Assert.InRange(maxIndex, 25, 35);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void RmsRobustness_MalformedOrOddLengthBuffers_ReturnsSafeFloat(int length)
    {
        byte[] buffer = new byte[length];
        for (int i = 0; i < length; i++) buffer[i] = 0xFF;

        float rms = _dsp.CalculateRms(buffer);
        Assert.False(float.IsNaN(rms));
        Assert.False(float.IsInfinity(rms));
        Assert.InRange(rms, 0.0f, 1.0f);
    }

    [Fact]
    public void RmsRobustness_NullBuffer_ReturnsZero()
    {
        Assert.Equal(0.0f, _dsp.CalculateRms(null!));
    }

    #endregion

    #region SECTION 7: Atomic Audio Source Switching Under Heavy Streaming

    [Fact]
    public async Task SwitchSource_DuringActiveMicroBufferBurst_FlushesCleanlyAndRealigns()
    {
        var factory = new AdversarialCaptureFactory();
        factory.LoopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        factory.MicClient.WaveFormat = new WaveFormat(44100, 16, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed 50ms of 48kHz float audio to Loopback (half chunk)
        byte[] halfLoopback = GenerateSyntheticAudio(48000, 2, 32, isFloat: true, durationSec: 0.050, freqHz: 440.0);
        factory.LoopbackClient.FeedAudio(halfLoopback, halfLoopback.Length);

        // Accumulator has 50ms; 0 chunks emitted yet
        Assert.Empty(receivedChunks);

        // Rapid atomic switch to Microphone
        await engine.SwitchSourceAsync(AudioSource.Microphone, "mic-noise-cancelling");
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("mic-noise-cancelling", engine.CurrentDeviceId);
        Assert.Equal(1, engine.SourceSwitchCount);

        // Feed 100ms of 44.1kHz audio to Mic
        byte[] fullMic = GenerateSyntheticAudio(44100, 2, 16, isFloat: false, durationSec: 0.100, freqHz: 1000.0);
        factory.MicClient.FeedAudio(fullMic, fullMic.Length);

        // Exactly 1 clean chunk from Mic emitted (no loopback bleed)
        Assert.Single(receivedChunks);
        Assert.Equal(3200, receivedChunks[0].Length);
        Assert.Equal(1, engine.ChunksCapturedCount);

        await engine.StopAsync();
    }

    [Fact]
    public async Task RapidAtomicSourceSwitching_50Times_ZeroDeadlockOrExceptions()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        for (int i = 0; i < 50; i++)
        {
            var targetSource = (i % 2 == 0) ? AudioSource.Microphone : AudioSource.SystemAudioLoopback;
            string deviceId = $"dev-{i}";
            await engine.SwitchSourceAsync(targetSource, deviceId);
            Assert.Equal(targetSource, engine.CurrentSource);
            Assert.Equal(deviceId, engine.CurrentDeviceId);
        }

        Assert.Equal(50, engine.SourceSwitchCount);
        Assert.True(engine.IsCapturing);

        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
    }

    #endregion

    #region Helper Methods & Adversarial Test Doubles

    private static byte[] GenerateSyntheticAudio(
        int sampleRate,
        int channels,
        int bitsPerSample,
        bool isFloat,
        double durationSec,
        double freqHz,
        float amplitude = 0.8f)
    {
        int bytesPerSample = bitsPerSample / 8;
        int frameCount = (int)(sampleRate * durationSec);
        int totalBytes = frameCount * channels * bytesPerSample;
        byte[] buffer = new byte[totalBytes];

        for (int i = 0; i < frameCount; i++)
        {
            double t = (double)i / sampleRate;
            float sampleVal = (float)(amplitude * Math.Sin(2.0 * Math.PI * freqHz * t));

            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                if (isFloat && bytesPerSample == 4)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(sampleVal), 0, buffer, offset, 4);
                }
                else if (bytesPerSample == 4 && !isFloat) // 32-bit int
                {
                    int s = (int)Math.Clamp((long)Math.Round(sampleVal * 2147483647.0), int.MinValue, int.MaxValue);
                    Buffer.BlockCopy(BitConverter.GetBytes(s), 0, buffer, offset, 4);
                }
                else if (bytesPerSample == 3) // 24-bit int
                {
                    int s = (int)Math.Clamp((long)Math.Round(sampleVal * 8388607.0), -8388608, 8388607);
                    buffer[offset] = (byte)(s & 0xFF);
                    buffer[offset + 1] = (byte)((s >> 8) & 0xFF);
                    buffer[offset + 2] = (byte)((s >> 16) & 0xFF);
                }
                else if (bytesPerSample == 2) // 16-bit PCM
                {
                    short s = (short)Math.Clamp((int)Math.Round(sampleVal * 32767.0f), -32768, 32767);
                    Buffer.BlockCopy(BitConverter.GetBytes(s), 0, buffer, offset, 2);
                }
                else if (bytesPerSample == 1) // 8-bit unsigned
                {
                    int s = (int)Math.Clamp(Math.Round(128.0 + (sampleVal * 127.0)), 0, 255);
                    buffer[offset] = (byte)s;
                }
            }
        }

        return buffer;
    }

    private class AdversarialCaptureFactory : IWasapiCaptureFactory
    {
        private readonly object _lock = new();
        private AdversarialCaptureClient _loopbackClient = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        private AdversarialCaptureClient _micClient = new(new WaveFormat(44100, 16, 2));

        public AdversarialCaptureClient LoopbackClient
        {
            get { lock (_lock) return _loopbackClient; }
            set { lock (_lock) _loopbackClient = value; }
        }

        public AdversarialCaptureClient MicClient
        {
            get { lock (_lock) return _micClient; }
            set { lock (_lock) _micClient = value; }
        }

        public List<AudioDeviceDescriptor> Devices { get; set; } = [];

        public IWasapiCaptureClient CreateLoopbackCapture()
        {
            lock (_lock)
            {
                if (_loopbackClient.IsDisposed)
                {
                    _loopbackClient = new AdversarialCaptureClient(_loopbackClient.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                }
                return _loopbackClient;
            }
        }

        public IWasapiCaptureClient CreateMicrophoneCapture(string? deviceId = null)
        {
            lock (_lock)
            {
                if (_micClient.IsDisposed)
                {
                    _micClient = new AdversarialCaptureClient(_micClient.WaveFormat ?? new WaveFormat(44100, 16, 2));
                }
                return _micClient;
            }
        }

        public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices()
        {
            lock (_lock) return Devices;
        }
    }

    private class AdversarialCaptureClient : IWasapiCaptureClient
    {
        private bool _isDisposed;

        public WaveFormat? WaveFormat { get; set; }
        public bool IsRecording { get; private set; }
        public bool IsDisposed => _isDisposed;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public AdversarialCaptureClient(WaveFormat? waveFormat = null)
        {
            WaveFormat = waveFormat;
        }

        public void StartRecording()
        {
            if (_isDisposed) _isDisposed = false;
            IsRecording = true;
        }

        public void StopRecording()
        {
            IsRecording = false;
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void FeedAudio(byte[] buffer, int bytesRecorded)
        {
            if (_isDisposed || !IsRecording) return;
            DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
        }

        public void Dispose()
        {
            _isDisposed = true;
            IsRecording = false;
        }
    }

    #endregion
}
