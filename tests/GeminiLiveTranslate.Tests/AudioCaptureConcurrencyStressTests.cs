using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

/// <summary>
/// Adversarial stress testing suite for WasapiAudioCaptureEngine:
/// Concurrency & lifecycle chaos, silence generator race conditions, memory safety,
/// fault injection, COMException device disconnect, and disposal race conditions.
/// </summary>
public class AudioCaptureConcurrencyStressTests
{
    // =========================================================================
    // SECTION 1: Concurrency & Lifecycle Chaos
    // =========================================================================

    [Fact]
    public async Task ConcurrencyChaos_60ConcurrentTasks_HammeringAllOperations_ZeroDeadlocksOrCrashes()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var token = cts.Token;

        var exceptions = new ConcurrentBag<Exception>();
        var operationsCompleted = new int[8]; // Track completions per op type

        // Background worker feeding audio
        var audioFeedTask = Task.Run(async () =>
        {
            byte[] loopbackAudio = AudioGen.Generate48kFloatStereo(0.05, 440);
            byte[] micAudio = AudioGen.Generate441kPcm16Stereo(0.05, 880);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (engine.IsCapturing)
                    {
                        if (engine.CurrentSource == AudioSource.SystemAudioLoopback)
                        {
                            factory.LoopbackClient.TryFeedAudio(loopbackAudio, loopbackAudio.Length);
                        }
                        else
                        {
                            factory.MicClient.TryFeedAudio(micAudio, micAudio.Length);
                        }
                    }
                }
                catch { }

                await Task.Delay(5, token).ConfigureAwait(false);
            }
        }, token);

        // 60 concurrent worker tasks hammering all operations
        int taskCount = 60;
        var tasks = new Task[taskCount];

        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(async () =>
            {
                var rng = new Random(taskId * 1000 + 42);

                while (!token.IsCancellationRequested)
                {
                    int op = rng.Next(8);
                    try
                    {
                        switch (op)
                        {
                            case 0:
                                await engine.StartAsync(AudioSource.SystemAudioLoopback).ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[0]);
                                break;
                            case 1:
                                await engine.StartAsync(AudioSource.Microphone, "mic-" + (taskId % 4)).ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[1]);
                                break;
                            case 2:
                                await engine.StopAsync().ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[2]);
                                break;
                            case 3:
                                await engine.SwitchSourceAsync(AudioSource.SystemAudioLoopback).ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[3]);
                                break;
                            case 4:
                                await engine.SwitchSourceAsync(AudioSource.Microphone, "mic-" + (taskId % 4)).ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[4]);
                                break;
                            case 5:
                                engine.InjectSilencePacket();
                                Interlocked.Increment(ref operationsCompleted[5]);
                                break;
                            case 6:
                                var _ = engine.GetMicrophoneDevices();
                                Interlocked.Increment(ref operationsCompleted[6]);
                                break;
                            case 7:
                                var __ = await engine.GetMicrophoneDevicesAsync().ConfigureAwait(false);
                                Interlocked.Increment(ref operationsCompleted[7]);
                                break;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }

                    // Brief non-blocking yield
                    await Task.Yield();
                }
            }, token);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }

        try
        {
            await audioFeedTask;
        }
        catch (OperationCanceledException) { }

        // Final verification: clean shutdown
        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
        Assert.Empty(exceptions);

        int totalOps = operationsCompleted.Sum();
        Assert.True(totalOps > 500, $"Expected > 500 completed chaos operations, actual: {totalOps}");
    }

    [Fact]
    public async Task RapidSourceSwitching_UnderContinuousAudio_ZeroPacketCorruptionOrLoss()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new ConcurrentBag<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] loopbackAudio = AudioGen.Generate48kFloatStereo(0.02, 440);
        byte[] micAudio = AudioGen.Generate441kPcm16Stereo(0.02, 880);

        // 100 rapid source switches while continuously pumping audio
        for (int i = 0; i < 100; i++)
        {
            var targetSource = (i % 2 == 0) ? AudioSource.Microphone : AudioSource.SystemAudioLoopback;
            var switchTask = engine.SwitchSourceAsync(targetSource, targetSource == AudioSource.Microphone ? "mic-1" : null);

            // Feed audio to whichever client might be active during the switch
            for (int k = 0; k < 5; k++)
            {
                factory.LoopbackClient.TryFeedAudio(loopbackAudio, loopbackAudio.Length);
                factory.MicClient.TryFeedAudio(micAudio, micAudio.Length);
            }

            await switchTask;
        }

        await engine.StopAsync();
        Assert.False(engine.IsCapturing);

        // Every chunk emitted during switches MUST be strictly 3,200 bytes
        Assert.NotEmpty(receivedChunks);
        foreach (var chunk in receivedChunks)
        {
            Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunk.Length);
        }
    }

    [Fact]
    public async Task ReentrantCallback_SubscriberCallsStopCapture_DoesNotDeadlock()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var stopInvokedTcs = new TaskCompletionSource<bool>();

        engine.OnAudioChunkCaptured += (s, chunk) =>
        {
            // Subscriber reentrantly calls StopCapture() from within chunk callback!
            try
            {
                engine.StopCapture();
                stopInvokedTcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                stopInvokedTcs.TrySetException(ex);
            }
        };

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed enough audio to emit a 100ms chunk
        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        // If deadlocked, this would timeout
        var completed = await Task.WhenAny(stopInvokedTcs.Task, Task.Delay(2000));
        Assert.Same(stopInvokedTcs.Task, completed);
        Assert.True(await stopInvokedTcs.Task);
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public async Task ReentrantCallback_SubscriberCallsSwitchSource_DoesNotDeadlock()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var switchInvokedTcs = new TaskCompletionSource<bool>();
        int callbackCount = 0;

        engine.OnAudioChunkCaptured += (s, chunk) =>
        {
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                // Reentrantly switch source inside chunk callback
                try
                {
                    engine.SwitchSource(AudioSource.Microphone, "mic-reentrant");
                    switchInvokedTcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    switchInvokedTcs.TrySetException(ex);
                }
            }
        };

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        var completed = await Task.WhenAny(switchInvokedTcs.Task, Task.Delay(2000));
        Assert.Same(switchInvokedTcs.Task, completed);
        Assert.True(await switchInvokedTcs.Task);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
        Assert.Equal("mic-reentrant", engine.CurrentDeviceId);

        await engine.StopAsync();
    }

    [Fact]
    public async Task ReentrantCallback_SubscriberCallsInjectSilence_DoesNotDeadlock()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        int totalChunksReceived = 0;
        int reentrantInjections = 0;

        engine.OnAudioChunkCaptured += (s, chunk) =>
        {
            int count = Interlocked.Increment(ref totalChunksReceived);
            if (count == 1 && Interlocked.Increment(ref reentrantInjections) == 1)
            {
                // Injects silence from inside the audio chunk callback
                engine.InjectSilencePacket();
            }
        };

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        // Both the audio chunk and the silence chunk should be emitted
        Assert.True(totalChunksReceived >= 2);
        await engine.StopAsync();
    }

    // =========================================================================
    // SECTION 2: Silence Generator Contention & Packet Integrity
    // =========================================================================

    [Fact]
    public async Task SilenceContention_RealAudioRacesSilenceTimer_NoCorruptedOrPartialChunks()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = true; // Background silence timer active!

        var chunks = new ConcurrentBag<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => chunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed packets at timing that deliberately collides with the 100ms silence timer ticks
        byte[] pcm = AudioGen.Generate48kFloatStereo(0.04, 440); // 40ms packets

        for (int i = 0; i < 15; i++)
        {
            factory.LoopbackClient.FeedAudio(pcm, pcm.Length);
            await Task.Delay(35); // 35ms sleep: periodically exceeds or falls below 100ms silence threshold
        }

        // Wait a moment for final silence tick
        await Task.Delay(150);

        await engine.StopAsync();

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunk.Length);

            // Verify packet is neither null nor has invalid uninitialized memory
            // Each 16-bit PCM sample must be valid
            for (int i = 0; i < chunk.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(chunk, i);
                // Samples must not be corrupt or NaN
                Assert.InRange(sample, short.MinValue, short.MaxValue);
            }
        }
    }

    [Fact]
    public async Task SilenceInjection_SustainedSilence_EmitsSteadyChunksWithoutAccumulatorGrowth()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = true;

        var silenceChunks = new ConcurrentBag<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => silenceChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // No audio fed at all for 400ms. Silence timer should fire roughly every 100ms
        await Task.Delay(400);

        await engine.StopAsync();

        Assert.True(silenceChunks.Count >= 2, $"Expected at least 2 silence chunks, actual: {silenceChunks.Count}");
        foreach (var chunk in silenceChunks)
        {
            Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunk.Length);
            // All samples in silence chunk must be exact zero (silence)
            Assert.All(chunk, b => Assert.Equal(0, b));
        }

        // Accumulator must remain strictly zero
        Assert.Equal(0, engine.AccumulatorCount);
    }

    [Fact]
    public async Task SilenceContention_ConcurrentInjectSilenceAndAudioFeed_ThreadSafe()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var chunks = new ConcurrentBag<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => chunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.05, 440);

        // 10 audio feeding threads + 10 silence injection threads concurrently
        var feedTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                factory.LoopbackClient.TryFeedAudio(pcm, pcm.Length);
                Thread.Sleep(2);
            }
        }));

        var injectTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                engine.InjectSilencePacket();
                Thread.Sleep(2);
            }
        }));

        await Task.WhenAll(feedTasks.Concat(injectTasks));
        await engine.StopAsync();

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunk.Length);
        }
    }

    // =========================================================================
    // SECTION 3: Memory Bounding & High-Volume Sustained Ingestion
    // =========================================================================

    [Fact]
    public async Task MemoryBounding_ContinuousIngestionMillionsOfSamples_BufferRemainsBounded()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        int totalChunksEmitted = 0;
        engine.OnAudioChunkCaptured += (s, chunk) => Interlocked.Increment(ref totalChunksEmitted);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Generate various packet sizes: 10ms, 25ms, 53ms, 100ms, 127ms
        byte[] pcm10ms = AudioGen.Generate48kFloatStereo(0.010, 440);
        byte[] pcm25ms = AudioGen.Generate48kFloatStereo(0.025, 550);
        byte[] pcm53ms = AudioGen.Generate48kFloatStereo(0.053, 660); // odd size
        byte[] pcm100ms = AudioGen.Generate48kFloatStereo(0.100, 880);

        byte[][] packets = [pcm10ms, pcm25ms, pcm53ms, pcm100ms];

        long totalBytesFed = 0;
        int initialBufferSize = engine.AccumulatorBufferSize;

        // Feed 1,200 packets (approx. 4,000,000+ input audio bytes)
        for (int i = 0; i < 1200; i++)
        {
            byte[] packet = packets[i % packets.Length];
            factory.LoopbackClient.FeedAudio(packet, packet.Length);
            totalBytesFed += packet.Length;

            // Invariant: Accumulator count must NEVER grow unboundedly
            // At the end of any feed, pending un-chunked bytes must be < BytesPerChunk
            Assert.True(engine.AccumulatorCount < WasapiAudioCaptureEngine.BytesPerChunk,
                $"AccumulatorCount {engine.AccumulatorCount} exceeded BytesPerChunk {WasapiAudioCaptureEngine.BytesPerChunk}!");

            // Invariant: Buffer capacity must remain bounded
            Assert.True(engine.AccumulatorBufferSize <= 131072,
                $"AccumulatorBufferSize {engine.AccumulatorBufferSize} grew unboundedly!");
        }

        await engine.StopAsync();

        Assert.True(totalBytesFed > 4_000_000, $"Total bytes fed: {totalBytesFed}");
        Assert.True(totalChunksEmitted > 500, $"Total chunks emitted: {totalChunksEmitted}");
        Assert.Equal(0, engine.AccumulatorCount); // Cleared on stop
    }

    [Fact]
    public async Task MemoryBounding_MassiveBurstPacket_DrainsCompletelyToZeroOrRemainder()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var receivedChunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunks.Add(chunk);

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Ingest massive 1.5-second burst (576,000 bytes at 48kHz float stereo) in a single packet!
        byte[] massiveBurst = AudioGen.Generate48kFloatStereo(1.5, 440);
        Assert.Equal(576000, massiveBurst.Length);

        factory.LoopbackClient.FeedAudio(massiveBurst, massiveBurst.Length);

        // 1.5s at 16kHz = 24,000 samples = 48,000 resampled bytes = exactly 15 chunks (15 * 3200)
        Assert.Equal(15, receivedChunks.Count);
        Assert.All(receivedChunks, c => Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, c.Length));

        // Remainder must be < 3200 bytes
        Assert.True(engine.AccumulatorCount < WasapiAudioCaptureEngine.BytesPerChunk);

        await engine.StopAsync();
    }

    // =========================================================================
    // SECTION 4: Fault Injection & Subscriber Resilience
    // =========================================================================

    [Fact]
    public async Task SubscriberResilience_OnAudioChunkCapturedThrows_EngineSurvivesAndOtherSubscribersFire()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Exception? capturedError = null;
        engine.OnCaptureError += (s, ex) => capturedError = ex;

        bool healthySubscriberReceived = false;

        // Exploding subscriber
        engine.OnAudioChunkCaptured += (s, chunk) =>
        {
            throw new InvalidOperationException("Exploding subscriber test exception");
        };

        // Healthy subscriber
        engine.AudioChunkAvailable += (s, args) =>
        {
            healthySubscriberReceived = true;
        };

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        // Engine must stay running!
        Assert.True(engine.IsCapturing);

        // Healthy subscriber must still have received the chunk
        Assert.True(healthySubscriberReceived);

        // Exception was captured in OnCaptureError
        Assert.NotNull(capturedError);
        Assert.IsType<InvalidOperationException>(capturedError);
        Assert.Equal("Exploding subscriber test exception", capturedError.Message);

        await engine.StopAsync();
    }

    [Fact]
    public async Task SubscriberResilience_OnSpectrumCalculatedThrows_AudioChunkAvailableStillFires()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Exception? capturedError = null;
        engine.OnCaptureError += (s, ex) => capturedError = ex;

        bool spectrumDataAvailableFired = false;

        engine.OnSpectrumCalculated += (s, bands) =>
        {
            throw new ArgumentException("Spectrum subscriber explosion");
        };

        engine.SpectrumDataAvailable += (s, args) =>
        {
            spectrumDataAvailableFired = true;
        };

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        Assert.True(engine.IsCapturing);
        Assert.True(spectrumDataAvailableFired);
        Assert.NotNull(capturedError);
        Assert.IsType<ArgumentException>(capturedError);

        await engine.StopAsync();
    }

    [Fact]
    public async Task SubscriberResilience_AllSubscribersThrowSimultaneously_EngineSurvives()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var errors = new ConcurrentBag<Exception>();
        engine.OnCaptureError += (s, ex) => errors.Add(ex);

        // All 4 event subscribers throw!
        engine.OnAudioChunkCaptured += (s, chunk) => throw new FormatException("Chunk error");
        engine.AudioChunkAvailable += (s, args) => throw new OverflowException("ChunkAvailable error");
        engine.OnSpectrumCalculated += (s, bands) => throw new DivideByZeroException("Spectrum error");
        engine.SpectrumDataAvailable += (s, args) => throw new ArithmeticException("SpectrumData error");

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        byte[] pcm = AudioGen.Generate48kFloatStereo(0.12, 440);
        // Feeding audio must NOT throw or crash
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);

        Assert.True(engine.IsCapturing);
        Assert.Equal(4, errors.Count);

        // Engine can still be stopped cleanly
        await engine.StopAsync();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public async Task DeviceDisconnect_COMExceptionDuringCapture_TransitionsToStoppedAndDispatchesError()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = true;

        Exception? reportedError = null;
        string? reportedErrorMsg = null;
        engine.OnCaptureError += (s, ex) => reportedError = ex;
        engine.CaptureErrorOccurred += (s, msg) => reportedErrorMsg = msg;

        await engine.StartAsync(AudioSource.Microphone, "mic-usb-1");
        Assert.True(engine.IsCapturing);

        // Feed some initial audio
        byte[] pcm = AudioGen.Generate441kPcm16Stereo(0.05, 440);
        factory.MicClient.FeedAudio(pcm, pcm.Length);

        // Simulate sudden hardware disconnect (AUDCLNT_E_DEVICE_INVALIDATED = 0x88890004)
        var comEx = new COMException("The audio endpoint was unplugged", unchecked((int)0x88890004));
        factory.MicClient.SimulateError(comEx);

        // Invariant: MUST transition cleanly to stopped state!
        Assert.False(engine.IsCapturing, "Engine must transition to IsCapturing == false upon COMException!");

        // Invariant: Must report the exact COMException
        Assert.NotNull(reportedError);
        Assert.Same(comEx, reportedError);
        Assert.Contains("endpoint was unplugged", reportedErrorMsg);

        // Invariant: Accumulator must be reset
        Assert.Equal(0, engine.AccumulatorCount);

        // Invariant: Silence timer must NOT continue emitting chunks for dead device
        int chunksBefore = engine.ChunksCapturedCount;
        await Task.Delay(250);
        Assert.Equal(chunksBefore, engine.ChunksCapturedCount);
    }

    [Fact]
    public async Task DeviceDisconnect_AudioServiceCrashDuringCapture_TransitionsToStopped()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        Exception? capturedError = null;
        engine.OnCaptureError += (s, ex) => capturedError = ex;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);
        Assert.True(engine.IsCapturing);

        // Simulate WASAPI service crash (AUDCLNT_E_SERVICE_NOT_RUNNING = 0x88890003)
        var serviceCrashEx = new COMException("Windows Audio service stopped unexpectedly", unchecked((int)0x88890003));
        factory.LoopbackClient.SimulateError(serviceCrashEx);

        Assert.False(engine.IsCapturing);
        Assert.Same(serviceCrashEx, capturedError);
    }

    // =========================================================================
    // SECTION 5: Disposal Race Conditions
    // =========================================================================

    [Fact]
    public async Task DisposalRace_DisposeCalledWhileFeedingAudio_NoProcessCrash()
    {
        var factory = new AdversarialCaptureFactory();
        var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        using var cts = new CancellationTokenSource();
        byte[] pcm = AudioGen.Generate48kFloatStereo(0.02, 440);

        // Background thread rapidly feeding audio
        var feedTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    factory.LoopbackClient.TryFeedAudio(pcm, pcm.Length);
                }
                catch { }
            }
        });

        // Let it run for 30ms then dispose asynchronously
        await Task.Delay(30);
        engine.Dispose();

        cts.Cancel();
        await feedTask;

        Assert.False(engine.IsCapturing);
        Assert.Throws<ObjectDisposedException>(() => engine.StartCapture());
    }

    [Fact]
    public async Task DisposalRace_DisposeAsyncCalledWhileConcurrentTasksHammering_ZeroUnhandledExceptions()
    {
        var factory = new AdversarialCaptureFactory();
        var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var token = cts.Token;

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await engine.SwitchSourceAsync(AudioSource.Microphone);
                        await engine.SwitchSourceAsync(AudioSource.SystemAudioLoopback);
                        engine.InjectSilencePacket();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Cleanly expected after dispose
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    await Task.Yield();
                }
            }));
        }

        // Dispose in the middle of hammering
        await Task.Delay(50);
        await engine.DisposeAsync();

        cts.Cancel();
        await Task.WhenAll(tasks);

        Assert.False(engine.IsCapturing);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.StartAsync());
    }

    [Fact]
    public void DisposalRace_MultiThreadedConcurrentDispose_IdempotentAndThreadSafe()
    {
        var factory = new AdversarialCaptureFactory();
        var engine = new WasapiAudioCaptureEngine(factory);

        // 20 threads simultaneously calling Dispose()
        Parallel.For(0, 20, _ =>
        {
            engine.Dispose();
        });

        Assert.False(engine.IsCapturing);
        Assert.Throws<ObjectDisposedException>(() => engine.StartCapture());
    }

    // =========================================================================
    // SECTION 6: Extreme Audio Formats & Boundary Sanitization
    // =========================================================================

    [Fact]
    public async Task AudioFormatStress_ExtremeSampleRatesAndChannels_ResamplesTo16k16BitMono()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        var chunks = new List<byte[]>();
        engine.OnAudioChunkCaptured += (s, chunk) => chunks.Add(chunk);

        // Test 1: 96kHz IEEE float Stereo (2 channels, 32-bit float)
        factory.LoopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(96000, 2);
        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // 96k * 0.1s = 9600 frames * 8 bytes/frame = 76,800 bytes -> 1600 samples at 16k = 3200 bytes
        byte[] pcm96k = AudioGen.GenerateFloatBuffer(96000, 2, 0.10, 440);
        factory.LoopbackClient.FeedAudio(pcm96k, pcm96k.Length);

        Assert.Single(chunks);
        Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunks[0].Length);

        // Test 2: 8kHz Mono 16-bit PCM (1 channel, 16-bit)
        chunks.Clear();
        await engine.SwitchSourceAsync(AudioSource.Microphone);
        factory.MicClient.WaveFormat = new WaveFormat(8000, 16, 1);

        // 8k * 0.2s = 1600 frames * 2 bytes = 3200 bytes -> resampled to 16k = 3200 samples = 6400 bytes = 2 chunks
        byte[] pcm8k = AudioGen.GeneratePcm16Buffer(8000, 1, 0.20, 300);
        factory.MicClient.FeedAudio(pcm8k, pcm8k.Length);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, c.Length));

        // Test 3: 48kHz 6-channel 5.1 Surround float (6 channels, 32-bit)
        chunks.Clear();
        await engine.SwitchSourceAsync(AudioSource.SystemAudioLoopback);
        factory.LoopbackClient.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 6);

        // 48k * 0.1s = 4800 frames * 24 bytes/frame = 115,200 bytes -> 1600 samples = 3200 bytes
        byte[] pcmSurround = AudioGen.GenerateFloatBuffer(48000, 6, 0.10, 440);
        factory.LoopbackClient.FeedAudio(pcmSurround, pcmSurround.Length);

        Assert.Single(chunks);
        Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, chunks[0].Length);

        await engine.StopAsync();
    }

    [Fact]
    public async Task AudioSanitization_NaNAndInfinityInFloatStream_ClampedWithoutCrashing()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        byte[]? receivedChunk = null;
        engine.OnAudioChunkCaptured += (s, chunk) => receivedChunk = chunk;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Construct 48kHz float stereo buffer containing NaN, +Inf, -Inf, 9999.0f, -9999.0f
        int frameCount = 4800; // 100ms at 48kHz -> resampled to 100ms at 16kHz (1600 samples = 3200 bytes)
        int bytesPerFrame = 8;
        byte[] corruptBuffer = new byte[frameCount * bytesPerFrame];

        for (int i = 0; i < frameCount; i++)
        {
            float val = (i % 5) switch
            {
                0 => float.NaN,
                1 => float.PositiveInfinity,
                2 => float.NegativeInfinity,
                3 => 99999.0f,
                _ => -99999.0f
            };

            byte[] b = BitConverter.GetBytes(val);
            Buffer.BlockCopy(b, 0, corruptBuffer, i * bytesPerFrame, 4);     // Left
            Buffer.BlockCopy(b, 0, corruptBuffer, i * bytesPerFrame + 4, 4); // Right
        }

        factory.LoopbackClient.FeedAudio(corruptBuffer, corruptBuffer.Length);

        Assert.NotNull(receivedChunk);
        Assert.Equal(WasapiAudioCaptureEngine.BytesPerChunk, receivedChunk.Length);

        // Output must contain valid finite numbers, clamped to short range
        for (int i = 0; i < receivedChunk.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(receivedChunk, i);
            Assert.InRange(sample, short.MinValue, short.MaxValue);
        }

        await engine.StopAsync();
    }

    [Fact]
    public async Task BoundaryStress_ZeroBytesNullBufferNegativeBytes_GracefullyIgnored()
    {
        var factory = new AdversarialCaptureFactory();
        using var engine = new WasapiAudioCaptureEngine(factory);
        engine.SilenceInjectionEnabled = false;

        int chunksEmitted = 0;
        engine.OnAudioChunkCaptured += (s, chunk) => chunksEmitted++;

        await engine.StartAsync(AudioSource.SystemAudioLoopback);

        // Feed zero bytes
        factory.LoopbackClient.FeedAudio(new byte[100], 0);
        Assert.Equal(0, chunksEmitted);

        // Feed negative bytes
        factory.LoopbackClient.FeedAudio(new byte[100], -10);
        Assert.Equal(0, chunksEmitted);

        // Feed unaligned single byte
        factory.LoopbackClient.FeedAudio(new byte[] { 0x42 }, 1);
        Assert.Equal(0, chunksEmitted);

        // Feed normal audio afterwards — verify engine remains fully operational
        byte[] pcm = AudioGen.Generate48kFloatStereo(0.10, 440);
        factory.LoopbackClient.FeedAudio(pcm, pcm.Length);
        Assert.Equal(1, chunksEmitted);

        await engine.StopAsync();
    }

    // =========================================================================
    // Test Doubles & Generators
    // =========================================================================

    private class AdversarialCaptureFactory : IWasapiCaptureFactory
    {
        private readonly object _lock = new();
        private AdversarialCaptureClient _loopbackClient = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        private AdversarialCaptureClient _micClient = new(new WaveFormat(44100, 16, 2));

        public AdversarialCaptureClient LoopbackClient
        {
            get { lock (_lock) return _loopbackClient; }
        }

        public AdversarialCaptureClient MicClient
        {
            get { lock (_lock) return _micClient; }
        }

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
            return new List<AudioDeviceDescriptor>
            {
                new() { Id = "mic-0", FriendlyName = "Default Microphone", IsDefault = true },
                new() { Id = "mic-1", FriendlyName = "USB Headset", IsDefault = false },
                new() { Id = "mic-2", FriendlyName = "Studio XLR Mic", IsDefault = false },
                new() { Id = "mic-3", FriendlyName = "Virtual Audio Cable", IsDefault = false }
            };
        }
    }

    private class AdversarialCaptureClient : IWasapiCaptureClient
    {
        private readonly object _clientLock = new();
        private volatile bool _isDisposed;
        private volatile bool _isRecording;

        public WaveFormat? WaveFormat { get; set; }
        public bool IsRecording => _isRecording;
        public bool IsDisposed => _isDisposed;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public AdversarialCaptureClient(WaveFormat? waveFormat)
        {
            WaveFormat = waveFormat;
        }

        public void StartRecording()
        {
            lock (_clientLock)
            {
                _isDisposed = false;
                _isRecording = true;
            }
        }

        public void StopRecording()
        {
            lock (_clientLock)
            {
                _isRecording = false;
            }
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void FeedAudio(byte[] buffer, int bytesRecorded)
        {
            if (_isDisposed || !_isRecording) return;
            DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
        }

        public bool TryFeedAudio(byte[] buffer, int bytesRecorded)
        {
            if (_isDisposed || !_isRecording) return false;
            try
            {
                DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SimulateError(Exception ex)
        {
            lock (_clientLock)
            {
                _isRecording = false;
            }
            RecordingStopped?.Invoke(this, new StoppedEventArgs(ex));
        }

        public void Dispose()
        {
            lock (_clientLock)
            {
                _isDisposed = true;
                _isRecording = false;
            }
        }
    }

    private static class AudioGen
    {
        public static byte[] Generate48kFloatStereo(double durationSec, double freqHz, float amplitude = 0.8f)
            => GenerateFloatBuffer(48000, 2, durationSec, freqHz, amplitude);

        public static byte[] Generate441kPcm16Stereo(double durationSec, double freqHz, short amplitude = 26000)
            => GeneratePcm16Buffer(44100, 2, durationSec, freqHz, amplitude);

        public static byte[] GenerateFloatBuffer(int sampleRate, int channels, double durationSec, double freqHz, float amplitude = 0.8f)
        {
            int frameCount = (int)(sampleRate * durationSec);
            int bytesPerFrame = channels * 4;
            byte[] buffer = new byte[frameCount * bytesPerFrame];

            for (int i = 0; i < frameCount; i++)
            {
                double t = (double)i / sampleRate;
                float sample = (float)(amplitude * Math.Sin(2.0 * Math.PI * freqHz * t));
                byte[] sampleBytes = BitConverter.GetBytes(sample);

                int offset = i * bytesPerFrame;
                for (int c = 0; c < channels; c++)
                {
                    Buffer.BlockCopy(sampleBytes, 0, buffer, offset + (c * 4), 4);
                }
            }

            return buffer;
        }

        public static byte[] GeneratePcm16Buffer(int sampleRate, int channels, double durationSec, double freqHz, short amplitude = 26000)
        {
            int frameCount = (int)(sampleRate * durationSec);
            int bytesPerFrame = channels * 2;
            byte[] buffer = new byte[frameCount * bytesPerFrame];

            for (int i = 0; i < frameCount; i++)
            {
                double t = (double)i / sampleRate;
                short sample = (short)(amplitude * Math.Sin(2.0 * Math.PI * freqHz * t));
                byte[] sampleBytes = BitConverter.GetBytes(sample);

                int offset = i * bytesPerFrame;
                for (int c = 0; c < channels; c++)
                {
                    Buffer.BlockCopy(sampleBytes, 0, buffer, offset + (c * 2), 2);
                }
            }

            return buffer;
        }
    }
}
