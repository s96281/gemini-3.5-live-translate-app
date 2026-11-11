using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.App.Audio;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using NAudio.Wave;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class AudioCaptureTests
{
    // =========================================================================
    // SECTION 1: Lifecycle & State Transitions
    // =========================================================================

    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);

        Assert.False(engine.IsCapturing);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);
        Assert.Null(engine.CurrentDeviceId);
        Assert.True(engine.SilenceInjectionEnabled);
        Assert.Equal(0, engine.ChunksCapturedCount);
        Assert.Equal(0, engine.SpectrumCalculatedCount);
        Assert.Equal(0, engine.SourceSwitchCount);
    }

    [Fact]
    public async Task StartAndStop_TransitionsIsCapturingStateCorrectly()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Assert.False(engine.IsCapturing);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);
        Assert.True(engine.IsCapturing);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);
        Assert.Equal(1, factory.CreateLoopbackCount);

        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void StartCaptureAndStopCapture_SynchronousMethods_WorkCorrectly()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        engine.StartCapture(AudioSource.Microphone, "mic-device-1");
        Assert.True(engine.IsCapturing);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("mic-device-1", engine.CurrentDeviceId);
        Assert.Equal(1, factory.CreateMicCount);

        engine.StopCapture();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public async Task DoubleStartAndDoubleStop_AreSafeAndIdempotent()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        // Double start with same source
        await engine.StartAsync(AudioSource.SystemAudioLoopback);
        Assert.True(engine.IsCapturing);
        int loopbackCountBefore = factory.CreateLoopbackCount;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);
        Assert.True(engine.IsCapturing);
        Assert.Equal(loopbackCountBefore, factory.CreateLoopbackCount);

        // Double stop
        await engine.StopAsync();
        Assert.False(engine.IsCapturing);

        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void AudioSourceType_Overloads_BehaveIdentically()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        engine.StartCapture(AudioSourceType.Microphone, "mic-xyz");
        Assert.True(engine.IsCapturing);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("mic-xyz", engine.CurrentDeviceId);

        engine.SwitchSource(AudioSourceType.SystemAudioLoopback);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);

        engine.StopCapture();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void Dispose_DisablesCapturing_AndThrowsOnSubsequentCalls()
    {
        var factory = new TestCaptureFactory();
        var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        engine.StartCapture();
        Assert.True(engine.IsCapturing);

        engine.Dispose();
        Assert.False(engine.IsCapturing);

        Assert.Throws<ObjectDisposedException>(() => engine.StartCapture());
        Assert.Throws<ObjectDisposedException>(() => engine.StopCapture());
        Assert.Throws<ObjectDisposedException>(() => engine.SwitchSource(AudioSource.Microphone));
        Assert.Throws<ObjectDisposedException>(() => engine.InjectSilencePacket());

        // Idempotent dispose
        engine.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpAsynchronously()
    {
        var factory = new TestCaptureFactory();
        var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        await engine.StartAsync();
        Assert.True(engine.IsCapturing);

        await engine.DisposeAsync();
        Assert.False(engine.IsCapturing);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.StartAsync());
    }

    // =========================================================================
    // SECTION 2: Resampling & Exact 100ms (3,200 Bytes) Chunking
    // =========================================================================

    [Fact]
    public async Task Resampling_48kHzFloatStereo_EmitsExact3200ByteChunks()
    {
        var factory = new TestCaptureFactory();
        var loopbackClient = factory.LoopbackClient;
        loopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var rawChunksReceived = new List<byte[]>();
        var chunkEventsReceived = new List<AudioChunkEventArgs>();

        engine.OnAudioChunkCaptured += (s, chunk) => rawChunksReceived.Add(chunk);
        engine.AudioChunkAvailable += (s, e) => chunkEventsReceived.Add(e);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed exactly 100ms of 48kHz stereo float audio:
        // 48000 frames/sec * 0.1 sec = 4800 frames.
        // Each frame = 2 channels * 4 bytes = 8 bytes. Total = 38,400 bytes.
        byte[] buffer100ms = Generate48kFloatStereo(durationSec: 0.100, freqHz: 440.0);
        Assert.Equal(38400, buffer100ms.Length);

        loopbackClient.FeedAudio(buffer100ms, buffer100ms.Length);

        Assert.Single(rawChunksReceived);
        Assert.Single(chunkEventsReceived);
        Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, rawChunksReceived[0].Length);
        Assert.Equal(3200, rawChunksReceived[0].Length);
        Assert.Equal(3200, chunkEventsReceived[0].PcmChunk.Length);
        Assert.Equal(16000, chunkEventsReceived[0].SampleRate);
        Assert.Equal(1, chunkEventsReceived[0].Channels);

        // Feed 300ms more audio (3 chunks expected)
        byte[] buffer300ms = Generate48kFloatStereo(durationSec: 0.300, freqHz: 440.0);
        loopbackClient.FeedAudio(buffer300ms, buffer300ms.Length);

        Assert.Equal(4, rawChunksReceived.Count);
        Assert.Equal(4, engine.ChunksCapturedCount);
        foreach (var chunk in rawChunksReceived)
        {
            Assert.Equal(3200, chunk.Length);
        }
    }

    [Fact]
    public async Task Resampling_441kHzPcm16Stereo_EmitsExact3200ByteChunks()
    {
        var factory = new TestCaptureFactory();
        var micClient = factory.MicClient;
        micClient.WaveFormat = new WaveFormat(44100, 16, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.Microphone);

        // Feed 1.0 second of 44.1kHz 16-bit PCM stereo audio:
        // 44100 frames * 4 bytes/frame = 176,400 bytes.
        // 1.0s at 16kHz mono 16-bit = 16,000 samples = 32,000 bytes = exactly 10 chunks of 3200 bytes.
        byte[] buffer1s = Generate441kPcm16Stereo(durationSec: 1.0, freqHz: 1000.0);
        Assert.Equal(176400, buffer1s.Length);

        micClient.FeedAudio(buffer1s, buffer1s.Length);

        Assert.Equal(10, receivedChunks.Count);
        Assert.Equal(10, engine.ChunksCapturedCount);
        foreach (var chunk in receivedChunks)
        {
            Assert.Equal(3200, chunk.Length);
        }
    }

    [Fact]
    public async Task Chunking_VariableFragmentBuffers_AccumulatesAndSplitsCorrectly()
    {
        var factory = new TestCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var chunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, c) => chunks.Add(c);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 48kHz float stereo has 8 bytes per frame.
        // 50ms = 2400 frames = 19200 bytes.
        // Resamples to 50ms at 16kHz = 800 samples = 1600 bytes (< 3200 bytes chunk threshold).
        byte[] halfChunk = Generate48kFloatStereo(durationSec: 0.050, freqHz: 440.0);
        client.FeedAudio(halfChunk, halfChunk.Length);

        // Accumulator has 1,600 bytes; no chunk emitted yet!
        Assert.Empty(chunks);

        // Feed second 50ms fragment
        client.FeedAudio(halfChunk, halfChunk.Length);

        // Accumulator reaches 3,200 bytes; exactly 1 chunk emitted!
        Assert.Single(chunks);
        Assert.Equal(3200, chunks[0].Length);

        // Feed 75ms (1,200 resampled samples = 2,400 bytes)
        byte[] fragment75ms = Generate48kFloatStereo(durationSec: 0.075, freqHz: 440.0);
        client.FeedAudio(fragment75ms, fragment75ms.Length);
        Assert.Single(chunks); // 2400 bytes accumulated, still < 3200

        // Feed another 75ms (2400 + 2400 = 4800 bytes -> 1 chunk emitted, 1600 bytes remain)
        client.FeedAudio(fragment75ms, fragment75ms.Length);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(3200, chunks[1].Length);
    }

    // =========================================================================
    // SECTION 3: 36-Band FFT Spectrum & RMS Loudness Taps
    // =========================================================================

    [Fact]
    public async Task SpectrumAndRms_SineWave_ProducesValidBandsAndRmsLevel()
    {
        var factory = new TestCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        float[]? lastSpectrum = null;
        AudioSpectrumEventArgs? lastSpectrumArgs = null;

        engine.OnSpectrumCalculated += (s, bands) => lastSpectrum = bands;
        engine.SpectrumDataAvailable += (s, e) => lastSpectrumArgs = e;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] audio100ms = Generate48kFloatStereo(durationSec: 0.100, freqHz: 1000.0, amplitude: 0.8f);
        client.FeedAudio(audio100ms, audio100ms.Length);

        Assert.NotNull(lastSpectrum);
        Assert.NotNull(lastSpectrumArgs);
        Assert.Equal(36, lastSpectrum.Length);
        Assert.Equal(36, lastSpectrumArgs.Bands.Length);

        // Check normalization: all bands must be in [0.0, 1.0]
        foreach (var band in lastSpectrum)
        {
            Assert.True(band >= 0.0f && band <= 1.0f, $"Band value {band} out of range [0.0, 1.0]");
        }

        // Verify that 1000Hz has detectable energy
        float maxEnergy = lastSpectrum.Max();
        Assert.True(maxEnergy > 0.05f, $"Expected max FFT energy > 0.05 for 1kHz sine wave, got {maxEnergy}");
        Assert.Equal(1, engine.SpectrumCalculatedCount);
    }

    [Fact]
    public async Task SpectrumAndRms_PureSilence_ProducesAllZeroBands()
    {
        var factory = new TestCaptureFactory();
        var client = factory.LoopbackClient;
        client.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        float[]? spectrum = null;
        engine.OnSpectrumCalculated += (s, bands) => spectrum = bands;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] silence = new byte[38400]; // 100ms of zeros
        client.FeedAudio(silence, silence.Length);

        Assert.NotNull(spectrum);
        Assert.Equal(36, spectrum.Length);
        for (int i = 0; i < spectrum.Length; i++)
        {
            Assert.Equal(0.0f, spectrum[i]);
        }
    }

    // =========================================================================
    // SECTION 4: Atomic Audio Source Switching
    // =========================================================================

    [Fact]
    public async Task SwitchSourceAsync_BetweenLoopbackAndMic_MaintainsCaptureFlow()
    {
        var factory = new TestCaptureFactory();
        factory.LoopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        factory.MicClient.WaveFormat = new WaveFormat(44100, 16, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, c) => receivedChunks.Add(c);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);

        // Feed 100ms to Loopback
        byte[] loopbackAudio = Generate48kFloatStereo(0.100, 440.0);
        factory.LoopbackClient.FeedAudio(loopbackAudio, loopbackAudio.Length);
        Assert.Single(receivedChunks);

        // Switch atomically to Microphone
        await engine.SwitchSourceAsync(AudioSource.Microphone, "mic-headset");
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("mic-headset", engine.CurrentDeviceId);
        Assert.Equal(1, engine.SourceSwitchCount);
        Assert.True(engine.IsCapturing);

        // Feed 100ms of 44.1kHz audio to Mic
        byte[] micAudio = Generate441kPcm16Stereo(0.100, 880.0);
        factory.MicClient.FeedAudio(micAudio, micAudio.Length);

        Assert.Equal(2, receivedChunks.Count);
        Assert.Equal(3200, receivedChunks[1].Length);
    }

    [Fact]
    public async Task SwitchSource_CleansAccumulator_PreventsSourceBleed()
    {
        var factory = new TestCaptureFactory();
        factory.LoopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        factory.MicClient.WaveFormat = new WaveFormat(44100, 16, 2);

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var chunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, c) => chunks.Add(c);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed partial 50ms buffer to Loopback
        byte[] half1 = Generate48kFloatStereo(0.050, 440.0);
        factory.LoopbackClient.FeedAudio(half1, half1.Length);
        Assert.Empty(chunks); // Nothing emitted yet

        // Switch to Microphone - must flush pending partial loopback bytes
        await engine.SwitchSourceAsync(AudioSource.Microphone);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);

        // Feed partial 50ms buffer to Mic
        byte[] half2 = Generate441kPcm16Stereo(0.050, 880.0);
        factory.MicClient.FeedAudio(half2, half2.Length);

        // If accumulator was NOT reset, half1 + half2 would total 100ms and emit a corrupted chunk!
        // Because it was properly flushed, accumulator only has 50ms from Mic, so chunks must be empty!
        Assert.Empty(chunks);

        // Feed the remaining 50ms to Mic
        factory.MicClient.FeedAudio(half2, half2.Length);

        // Now exactly 1 clean Mic chunk emitted
        Assert.Single(chunks);
        Assert.Equal(3200, chunks[0].Length);
    }

    [Fact]
    public void SwitchSource_WhileStopped_UpdatesSourceStateImmediately()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);

        Assert.False(engine.IsCapturing);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);

        engine.SwitchSource(AudioSource.Microphone, "device-mic-42");

        Assert.False(engine.IsCapturing);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("device-mic-42", engine.CurrentDeviceId);
        Assert.Equal(1, engine.SourceSwitchCount);
    }

    // =========================================================================
    // SECTION 5: Silence Packet Injection
    // =========================================================================

    [Fact]
    public void InjectSilencePacket_EmitsExact3200ByteZeroedPcmChunk()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? capturedChunk = null;
        float[]? capturedSpectrum = null;

        engine.OnAudioChunkCaptured += (s, c) => capturedChunk = c;
        engine.OnSpectrumCalculated += (s, sp) => capturedSpectrum = sp;

        engine.StartCapture();

        engine.InjectSilencePacket();

        Assert.NotNull(capturedChunk);
        Assert.Equal(3200, capturedChunk.Length);
        Assert.All(capturedChunk, b => Assert.Equal(0, b));

        Assert.NotNull(capturedSpectrum);
        Assert.Equal(36, capturedSpectrum.Length);
        Assert.All(capturedSpectrum, val => Assert.Equal(0.0f, val));

        Assert.Equal(1, engine.ChunksCapturedCount);
        Assert.Equal(1, engine.SpectrumCalculatedCount);
    }

    [Fact]
    public async Task AutomaticSilenceInjection_WhenCaptureQuiet_InjectsZeroedPacket()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = true;

        var silenceChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, c) => silenceChunks.Add(c);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Sleep 180ms without feeding any audio. Silence timer should fire at least once.
        await Task.Delay(180);

        Assert.NotEmpty(silenceChunks);
        var chunk = silenceChunks[0];
        Assert.Equal(3200, chunk.Length);
        Assert.All(chunk, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task AutomaticSilenceInjection_Disabled_DoesNotInjectPackets()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var chunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, c) => chunks.Add(c);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Wait 150ms with silence injection disabled
        await Task.Delay(150);

        Assert.Empty(chunks);
    }

    // =========================================================================
    // SECTION 6: Error Handling & Device Disconnection
    // =========================================================================

    [Fact]
    public async Task HardwareError_OnRecordingStoppedWithException_FiresErrorEvents()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Exception? capturedError = null;
        string? capturedErrorMessage = null;

        engine.OnCaptureError += (s, ex) => capturedError = ex;
        engine.CaptureErrorOccurred += (s, msg) => capturedErrorMessage = msg;

        await engine.StartAsync(AudioSource.Microphone);

        // Simulate USB mic disconnected
        var comException = new COMException("Audio endpoint removed", unchecked((int)0x88890004));
        factory.MicClient.SimulateError(comException);

        Assert.NotNull(capturedError);
        Assert.Same(comException, capturedError);
        Assert.NotNull(capturedErrorMessage);
        Assert.Contains("Audio endpoint removed", capturedErrorMessage);
    }

    [Fact]
    public async Task StartRecordingFailure_CatchesAndFiresErrorEvents()
    {
        var factory = new TestCaptureFactory();
        factory.LoopbackClient.SimulateStartError = new InvalidOperationException("Audio service stopped");

        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Exception? errorFired = null;
        string? msgFired = null;

        engine.OnCaptureError += (s, ex) => errorFired = ex;
        engine.CaptureErrorOccurred += (s, msg) => msgFired = msg;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());
        Assert.Equal("Audio service stopped", ex.Message);

        Assert.NotNull(errorFired);
        Assert.Same(ex, errorFired);
        Assert.NotNull(msgFired);
        Assert.Equal("Audio service stopped", msgFired);
        Assert.False(engine.IsCapturing);
    }

    // =========================================================================
    // SECTION 7: Device Enumeration & Default Factory
    // =========================================================================

    [Fact]
    public async Task GetMicrophoneDevicesAsync_ReturnsFactoryDevices()
    {
        var factory = new TestCaptureFactory();
        factory.Devices.Add(new AudioDeviceDescriptor { Id = "mic-1", FriendlyName = "Blue Yeti", IsDefault = true });
        factory.Devices.Add(new AudioDeviceDescriptor { Id = "mic-2", FriendlyName = "Realtek HD", IsDefault = false });

        using var engine = new WasapiAudioCaptureEngine(factory);

        var syncDevices = engine.GetMicrophoneDevices();
        var asyncDevices = await engine.GetMicrophoneDevicesAsync();

        Assert.Equal(2, syncDevices.Count);
        Assert.Equal(2, asyncDevices.Count);
        Assert.Equal("Blue Yeti", syncDevices[0].FriendlyName);
        Assert.True(syncDevices[0].IsDefault);
        Assert.Equal("Realtek HD", syncDevices[1].FriendlyName);
        Assert.False(syncDevices[1].IsDefault);
        Assert.Equal("Blue Yeti", syncDevices[0].ToString());
    }

    [Fact]
    public void DefaultWasapiCaptureFactory_OnLinux_ThrowsPlatformNotSupported()
    {
        var factory = new DefaultWasapiCaptureFactory();

        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => factory.CreateLoopbackCapture());
            Assert.Throws<PlatformNotSupportedException>(() => factory.CreateMicrophoneCapture());
            Assert.Throws<PlatformNotSupportedException>(() => factory.CreateMicrophoneCapture("dev-id"));
            Assert.Empty(factory.GetMicrophoneDevices());
        }
    }

    // =========================================================================
    // SECTION 8: Thread-Safety & Concurrency
    // =========================================================================

    [Fact]
    public async Task HighConcurrency_RapidSourceSwitching_ThreadSafe()
    {
        var factory = new TestCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        await engine.StartAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                var src = (index % 2 == 0) ? AudioSource.SystemAudioLoopback : AudioSource.Microphone;
                await engine.SwitchSourceAsync(src, $"device-{index}");
            }));
        }

        await Task.WhenAll(tasks);
        Assert.True(engine.IsCapturing);
        Assert.Equal(20, engine.SourceSwitchCount);

        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
    }

    // =========================================================================
    // Synthetic Audio Generation Helpers
    // =========================================================================

    private static byte[] Generate48kFloatStereo(double durationSec, double freqHz, float amplitude = 0.8f)
    {
        int sampleRate = 48000;
        int channels = 2;
        int frameCount = (int)(sampleRate * durationSec);
        int bytesPerFrame = channels * 4; // 32-bit float = 4 bytes
        byte[] buffer = new byte[frameCount * bytesPerFrame];

        for (int i = 0; i < frameCount; i++)
        {
            double t = (double)i / sampleRate;
            float sample = (float)(amplitude * Math.Sin(2.0 * Math.PI * freqHz * t));

            // Left and Right channels
            byte[] leftBytes = BitConverter.GetBytes(sample);
            byte[] rightBytes = BitConverter.GetBytes(sample);

            int offset = i * bytesPerFrame;
            Buffer.BlockCopy(leftBytes, 0, buffer, offset, 4);
            Buffer.BlockCopy(rightBytes, 0, buffer, offset + 4, 4);
        }

        return buffer;
    }

    private static byte[] Generate441kPcm16Stereo(double durationSec, double freqHz, short amplitude = 26000)
    {
        int sampleRate = 44100;
        int channels = 2;
        int frameCount = (int)(sampleRate * durationSec);
        int bytesPerFrame = channels * 2; // 16-bit PCM = 2 bytes
        byte[] buffer = new byte[frameCount * bytesPerFrame];

        for (int i = 0; i < frameCount; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(amplitude * Math.Sin(2.0 * Math.PI * freqHz * t));

            byte[] sampleBytes = BitConverter.GetBytes(sample);
            int offset = i * bytesPerFrame;
            Buffer.BlockCopy(sampleBytes, 0, buffer, offset, 2);
            Buffer.BlockCopy(sampleBytes, 0, buffer, offset + 2, 2);
        }

        return buffer;
    }

    // =========================================================================
    // Simulation Test Doubles
    // =========================================================================

    private class TestCaptureFactory : IWasapiCaptureFactory
    {
        private readonly object _lock = new();
        private TestCaptureClient _loopbackClient = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        private TestCaptureClient _micClient = new(new WaveFormat(44100, 16, 2));

        public TestCaptureClient LoopbackClient
        {
            get { lock (_lock) return _loopbackClient; }
            set { lock (_lock) _loopbackClient = value; }
        }

        public TestCaptureClient MicClient
        {
            get { lock (_lock) return _micClient; }
            set { lock (_lock) _micClient = value; }
        }

        public List<AudioDeviceDescriptor> Devices { get; set; } = [];

        public int CreateLoopbackCount { get; private set; }
        public int CreateMicCount { get; private set; }
        public string? LastMicDeviceId { get; private set; }

        public IWasapiCaptureClient CreateLoopbackCapture()
        {
            lock (_lock)
            {
                CreateLoopbackCount++;
                if (_loopbackClient.IsDisposed)
                {
                    _loopbackClient = new TestCaptureClient(_loopbackClient.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                }
                return _loopbackClient;
            }
        }

        public IWasapiCaptureClient CreateMicrophoneCapture(string? deviceId = null)
        {
            lock (_lock)
            {
                CreateMicCount++;
                LastMicDeviceId = deviceId;
                if (_micClient.IsDisposed)
                {
                    _micClient = new TestCaptureClient(_micClient.WaveFormat ?? new WaveFormat(44100, 16, 2));
                }
                return _micClient;
            }
        }

        public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices()
        {
            lock (_lock) return Devices;
        }
    }

    private class TestCaptureClient : IWasapiCaptureClient
    {
        private bool _isDisposed;

        public WaveFormat? WaveFormat { get; set; }
        public bool IsRecording { get; private set; }
        public bool IsDisposed => _isDisposed;
        public Exception? SimulateStartError { get; set; }

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public TestCaptureClient(WaveFormat? waveFormat = null)
        {
            WaveFormat = waveFormat;
        }

        public void StartRecording()
        {
            if (_isDisposed)
            {
                _isDisposed = false;
            }
            if (SimulateStartError != null) throw SimulateStartError;
            IsRecording = true;
        }

        public void StopRecording()
        {
            IsRecording = false;
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void FeedAudio(byte[] buffer, int bytesRecorded)
        {
            if (_isDisposed) throw new InvalidOperationException("FeedAudio called on DISPOSED TestCaptureClient!");
            if (!IsRecording) throw new InvalidOperationException("FeedAudio called on NOT RECORDING TestCaptureClient!");
            if (DataAvailable == null) throw new InvalidOperationException("FeedAudio: DataAvailable has NO handlers!");
            DataAvailable.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
        }

        public void SimulateError(Exception ex)
        {
            if (_isDisposed) throw new InvalidOperationException("SimulateError called on DISPOSED TestCaptureClient!");
            if (RecordingStopped == null) throw new InvalidOperationException("SimulateError: RecordingStopped has NO handlers!");
            RecordingStopped.Invoke(this, new StoppedEventArgs(ex));
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                IsRecording = false;
            }
        }
    }
}
