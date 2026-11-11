using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Production native audio capture engine supporting Windows system audio loopback
/// (WasapiLoopbackCapture) and physical microphone (WasapiCapture), with real-time
/// resampling to 16kHz 16-bit mono Linear PCM, exact 100ms (3,200-byte) chunking,
/// 36-band FFT / RMS loudness extraction, atomic source switching, and silence packet injection.
/// </summary>
public class WasapiAudioCaptureEngine : IAudioCaptureEngine
{
    public const int TargetSampleRate = 16000;
    public const int TargetBitsPerSample = 16;
    public const int TargetChannels = 1;
    public const int SamplesPerChunk = 1600; // 100ms at 16,000 Hz
    public const int BytesPerChunk = 3200;    // 1600 samples * 2 bytes/sample

    private readonly IWasapiCaptureFactory _captureFactory;
    private readonly IAudioDspService _dspService;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly object _silenceLock = new();
    private readonly object _emitLock = new();
    private readonly Timer _silenceTimer;

    private IWasapiCaptureClient? _currentCapture;
    private byte[] _rawInputBuffer = new byte[65536];
    private int _rawInputCount;
    private int _chunksResampledCount;
    private int _totalRawFramesConsumed;
    private byte[] _accumulatorBuffer = new byte[65536];
    private int _accumulatorCount;
    private long _lastChunkEmittedTimestamp;
    private volatile bool _isCapturing;
    private volatile bool _isDisposed;
    private int _disposedFlag;

    private int _chunksCapturedCount;
    private int _spectrumCalculatedCount;
    private int _sourceSwitchCount;

    /// <inheritdoc/>
    public bool IsCapturing => _isCapturing;

    /// <inheritdoc/>
    public AudioSource CurrentSource { get; private set; } = AudioSource.SystemAudioLoopback;

    /// <inheritdoc/>
    public string? CurrentDeviceId { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic silence packet injection is enabled.
    /// Injects 3,200-byte zeroed PCM chunks every 100ms when the source is quiet or paused.
    /// </summary>
    public bool SilenceInjectionEnabled { get; set; } = true;

    /// <summary>
    /// Total count of 3,200-byte chunks emitted.
    /// </summary>
    public int ChunksCapturedCount => _chunksCapturedCount;

    /// <summary>
    /// Total count of 36-band spectrum calculations performed.
    /// </summary>
    public int SpectrumCalculatedCount => _spectrumCalculatedCount;

    /// <summary>
    /// Total number of atomic source switches executed.
    /// </summary>
    public int SourceSwitchCount => _sourceSwitchCount;

    /// <summary>
    /// Gets current allocated capacity of the internal sample accumulator buffer.
    /// </summary>
    public int AccumulatorBufferSize
    {
        get
        {
            lock (_bufferLock)
            {
                return _accumulatorBuffer.Length;
            }
        }
    }

    /// <summary>
    /// Gets the count of pending un-chunked resampled bytes in the accumulator.
    /// </summary>
    public int AccumulatorCount
    {
        get
        {
            lock (_bufferLock)
            {
                return _accumulatorCount;
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<byte[]>? OnAudioChunkCaptured;

    /// <inheritdoc/>
    public event EventHandler<float[]>? OnSpectrumCalculated;

    /// <inheritdoc/>
    public event EventHandler<Exception>? OnCaptureError;

    /// <inheritdoc/>
    public event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;

    /// <inheritdoc/>
    public event EventHandler<AudioSpectrumEventArgs>? SpectrumDataAvailable;

    /// <inheritdoc/>
    public event EventHandler<string>? CaptureErrorOccurred;

    /// <summary>
    /// Initializes a new instance of <see cref="WasapiAudioCaptureEngine"/> using default factory and DSP service.
    /// </summary>
    public WasapiAudioCaptureEngine()
        : this(new DefaultWasapiCaptureFactory(), new AudioDspService())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WasapiAudioCaptureEngine"/> with a custom DSP service.
    /// </summary>
    public WasapiAudioCaptureEngine(IAudioDspService dspService)
        : this(new DefaultWasapiCaptureFactory(), dspService)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WasapiAudioCaptureEngine"/> with injected capture factory and DSP service.
    /// </summary>
    public WasapiAudioCaptureEngine(IWasapiCaptureFactory captureFactory, IAudioDspService? dspService = null)
    {
        _captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
        _dspService = dspService ?? new AudioDspService();
        _silenceTimer = new Timer(OnSilenceTimerTick, null, 25, 25);
    }

    /// <inheritdoc/>
    public void StartCapture(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null)
    {
        StartAsync(source, deviceId).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task StartAsync(AudioSource source = AudioSource.SystemAudioLoopback, string? deviceId = null)
    {
        ThrowIfDisposed();
        await _switchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isCapturing)
            {
                if (CurrentSource == source && CurrentDeviceId == deviceId)
                {
                    return; // Idempotent double-start
                }
            }

            CurrentSource = source;
            CurrentDeviceId = deviceId;

            lock (_bufferLock)
            {
                _accumulatorCount = 0;
                _rawInputCount = 0;
                _chunksResampledCount = 0;
                _totalRawFramesConsumed = 0;
            }

            TeardownCaptureClient();

            _currentCapture = CreateCaptureClient(source, deviceId);
            _currentCapture.DataAvailable += OnDataAvailable;
            _currentCapture.RecordingStopped += OnRecordingStopped;
            _currentCapture.StartRecording();

            _isCapturing = true;
            Interlocked.Exchange(ref _lastChunkEmittedTimestamp, Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            HandleCaptureError(ex);
            throw;
        }
        finally
        {
            try
            {
                _switchLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <inheritdoc/>
    public void StartCapture(AudioSourceType sourceType, string? deviceId = null)
        => StartCapture((AudioSource)sourceType, deviceId);

    /// <inheritdoc/>
    public Task StartAsync(AudioSourceType sourceType, string? deviceId = null)
        => StartAsync((AudioSource)sourceType, deviceId);

    /// <inheritdoc/>
    public void StopCapture()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        ThrowIfDisposed();
        await _switchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_isCapturing) return; // Idempotent double-stop

            _isCapturing = false;
            TeardownCaptureClient();

            lock (_bufferLock)
            {
                _accumulatorCount = 0;
                _rawInputCount = 0;
                _chunksResampledCount = 0;
                _totalRawFramesConsumed = 0;
            }
        }
        finally
        {
            try
            {
                _switchLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <inheritdoc/>
    public void SwitchSource(AudioSource newSource, string? deviceId = null)
    {
        SwitchSourceAsync(newSource, deviceId).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task SwitchSourceAsync(AudioSource newSource, string? deviceId = null)
    {
        ThrowIfDisposed();
        await _switchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (CurrentSource == newSource && CurrentDeviceId == deviceId)
            {
                return;
            }

            bool wasCapturing = _isCapturing;
            if (wasCapturing)
            {
                TeardownCaptureClient();
            }

            lock (_bufferLock)
            {
                _accumulatorCount = 0;
                _rawInputCount = 0;
                _chunksResampledCount = 0;
                _totalRawFramesConsumed = 0;
            }

            CurrentSource = newSource;
            CurrentDeviceId = deviceId;
            Interlocked.Increment(ref _sourceSwitchCount);

            if (wasCapturing)
            {
                _currentCapture = CreateCaptureClient(newSource, deviceId);
                _currentCapture.DataAvailable += OnDataAvailable;
                _currentCapture.RecordingStopped += OnRecordingStopped;
                _currentCapture.StartRecording();
                Interlocked.Exchange(ref _lastChunkEmittedTimestamp, Stopwatch.GetTimestamp());
            }
        }
        catch (Exception ex)
        {
            HandleCaptureError(ex);
            throw;
        }
        finally
        {
            try
            {
                _switchLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <inheritdoc/>
    public void SwitchSource(AudioSourceType newSource, string? deviceId = null)
        => SwitchSource((AudioSource)newSource, deviceId);

    /// <inheritdoc/>
    public Task SwitchSourceAsync(AudioSourceType newSource, string? deviceId = null)
        => SwitchSourceAsync((AudioSource)newSource, deviceId);

    /// <inheritdoc/>
    public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices()
    {
        ThrowIfDisposed();
        return _captureFactory.GetMicrophoneDevices();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AudioDeviceDescriptor>> GetMicrophoneDevicesAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult(_captureFactory.GetMicrophoneDevices());
    }

    /// <summary>
    /// Explicitly injects a 3,200-byte zeroed PCM chunk (silence) to keep downstream sessions alive.
    /// </summary>
    public void InjectSilencePacket(int sampleCount = SamplesPerChunk)
    {
        ThrowIfDisposed();
        if (!_isCapturing) return;

        if (sampleCount <= 0) sampleCount = SamplesPerChunk;
        int byteCount = sampleCount * 2;
        byte[] silence = new byte[byteCount];
        EmitChunk(silence);
    }

    private IWasapiCaptureClient CreateCaptureClient(AudioSource source, string? deviceId)
    {
        return source switch
        {
            AudioSource.SystemAudioLoopback => _captureFactory.CreateLoopbackCapture(),
            AudioSource.Microphone => _captureFactory.CreateMicrophoneCapture(deviceId),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported audio capture source.")
        };
    }

    private void TeardownCaptureClient()
    {
        if (_currentCapture != null)
        {
            try
            {
                _currentCapture.DataAvailable -= OnDataAvailable;
                _currentCapture.RecordingStopped -= OnRecordingStopped;
                _currentCapture.StopRecording();
            }
            catch (Exception ex)
            {
                HandleCaptureError(ex);
            }
            finally
            {
                try
                {
                    _currentCapture.Dispose();
                }
                catch (Exception ex)
                {
                    HandleCaptureError(ex);
                }
                _currentCapture = null;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || _isDisposed) return;
        if (e.Buffer == null || e.BytesRecorded <= 0) return;

        try
        {
            var waveFormat = (sender as IWasapiCaptureClient)?.WaveFormat ?? _currentCapture?.WaveFormat;
            int sampleRate = waveFormat?.SampleRate ?? 48000;
            int channels = waveFormat?.Channels ?? 2;
            int bitsPerSample = waveFormat?.BitsPerSample ?? 32;
            bool isFloat = false;

            if (waveFormat != null)
            {
                if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    isFloat = true;
                }
                else if (waveFormat.Encoding == WaveFormatEncoding.Pcm)
                {
                    isFloat = false;
                }
                else if (waveFormat is WaveFormatExtensible extensible)
                {
                    if (extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71")) // IEEE Float
                    {
                        isFloat = true;
                    }
                    else if (extensible.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71")) // PCM
                    {
                        isFloat = false;
                    }
                    else if (bitsPerSample == 32)
                    {
                        isFloat = true;
                    }
                }
                else if (bitsPerSample == 32)
                {
                    isFloat = true;
                }
            }

            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample <= 0)
            {
                bytesPerSample = isFloat ? 4 : 2;
            }
            int bytesPerFrame = channels * bytesPerSample;
            if (bytesPerFrame <= 0) bytesPerFrame = 4;

            List<byte[]> chunksToEmit = new();
            lock (_bufferLock)
            {
                int bytesToCopy = Math.Min(e.BytesRecorded, e.Buffer.Length);
                if (_rawInputCount + bytesToCopy > _rawInputBuffer.Length)
                {
                    Array.Resize(ref _rawInputBuffer, Math.Max(_rawInputBuffer.Length * 2, _rawInputCount + bytesToCopy));
                }
                Buffer.BlockCopy(e.Buffer, 0, _rawInputBuffer, _rawInputCount, bytesToCopy);
                _rawInputCount += bytesToCopy;

                while (true)
                {
                    if (_chunksResampledCount >= 10000)
                    {
                        _chunksResampledCount = 0;
                        _totalRawFramesConsumed = 0;
                    }

                    int targetTotalFrames = sampleRate > 0
                        ? (int)Math.Round((double)(_chunksResampledCount + 1) * sampleRate * SamplesPerChunk / TargetSampleRate, MidpointRounding.AwayFromZero)
                        : (_chunksResampledCount + 1) * SamplesPerChunk;
                    int framesNeededThisChunk = Math.Max(1, targetTotalFrames - _totalRawFramesConsumed);
                    int bytesNeededThisChunk = framesNeededThisChunk * bytesPerFrame;

                    if (_rawInputCount < bytesNeededThisChunk)
                    {
                        break;
                    }

                    byte[] rawChunk = new byte[bytesNeededThisChunk];
                    Buffer.BlockCopy(_rawInputBuffer, 0, rawChunk, 0, bytesNeededThisChunk);

                    _rawInputCount -= bytesNeededThisChunk;
                    if (_rawInputCount > 0)
                    {
                        Buffer.BlockCopy(_rawInputBuffer, bytesNeededThisChunk, _rawInputBuffer, 0, _rawInputCount);
                    }

                    _totalRawFramesConsumed += framesNeededThisChunk;
                    _chunksResampledCount++;

                    byte[] resampled = _dspService.ResampleTo16k16BitMono(
                        rawChunk,
                        bytesNeededThisChunk,
                        sampleRate,
                        channels,
                        bitsPerSample,
                        isFloat);

                    if (resampled.Length > 0)
                    {
                        if (_accumulatorCount + resampled.Length > _accumulatorBuffer.Length)
                        {
                            Array.Resize(ref _accumulatorBuffer, Math.Max(_accumulatorBuffer.Length * 2, _accumulatorCount + resampled.Length));
                        }
                        Buffer.BlockCopy(resampled, 0, _accumulatorBuffer, _accumulatorCount, resampled.Length);
                        _accumulatorCount += resampled.Length;
                    }
                }

                while (_accumulatorCount >= BytesPerChunk)
                {
                    byte[] chunk = new byte[BytesPerChunk];
                    Buffer.BlockCopy(_accumulatorBuffer, 0, chunk, 0, BytesPerChunk);

                    _accumulatorCount -= BytesPerChunk;
                    if (_accumulatorCount > 0)
                    {
                        Buffer.BlockCopy(_accumulatorBuffer, BytesPerChunk, _accumulatorBuffer, 0, _accumulatorCount);
                    }

                    chunksToEmit.Add(chunk);
                }
            }

            // Emit chunks outside the buffer lock to avoid deadlocks with callbacks
            for (int i = 0; i < chunksToEmit.Count; i++)
            {
                EmitChunk(chunksToEmit[i]);
            }
        }
        catch (Exception ex)
        {
            HandleCaptureError(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _isCapturing = false;
            lock (_bufferLock)
            {
                _accumulatorCount = 0;
            }
            HandleCaptureError(e.Exception);
        }
    }

    private void EmitChunk(byte[] chunk)
    {
        if (chunk == null || chunk.Length == 0) return;

        lock (_emitLock)
        {
            if (!_isCapturing || _isDisposed) return;

            Interlocked.Exchange(ref _lastChunkEmittedTimestamp, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _chunksCapturedCount);

            try
            {
                OnAudioChunkCaptured?.Invoke(this, chunk);
            }
            catch (Exception ex)
            {
                HandleCaptureError(ex);
            }

            try
            {
                AudioChunkAvailable?.Invoke(this, new AudioChunkEventArgs(chunk, TargetSampleRate, TargetChannels));
            }
            catch (Exception ex)
            {
                HandleCaptureError(ex);
            }

            float[]? bands = null;
            try
            {
                bands = _dspService.CalculateSpectrumFft(chunk, 36);
                Interlocked.Increment(ref _spectrumCalculatedCount);
            }
            catch (Exception ex)
            {
                HandleCaptureError(ex);
            }

            if (bands != null)
            {
                try
                {
                    OnSpectrumCalculated?.Invoke(this, bands);
                }
                catch (Exception ex)
                {
                    HandleCaptureError(ex);
                }

                try
                {
                    SpectrumDataAvailable?.Invoke(this, new AudioSpectrumEventArgs(bands));
                }
                catch (Exception ex)
                {
                    HandleCaptureError(ex);
                }
            }
        }
    }

    private void OnSilenceTimerTick(object? state)
    {
        if (_isDisposed || !_isCapturing || !SilenceInjectionEnabled)
        {
            return;
        }

        long last = Interlocked.Read(ref _lastChunkEmittedTimestamp);
        if (last == 0) return;

        if (Stopwatch.GetElapsedTime(last).TotalMilliseconds >= 100)
        {
            if (Monitor.TryEnter(_silenceLock))
            {
                try
                {
                    if (_isDisposed || !_isCapturing || !SilenceInjectionEnabled) return;
                    last = Interlocked.Read(ref _lastChunkEmittedTimestamp);
                    if (last != 0 && Stopwatch.GetElapsedTime(last).TotalMilliseconds >= 100)
                    {
                        byte[] silence = new byte[BytesPerChunk];
                        EmitChunk(silence);
                    }
                }
                finally
                {
                    Monitor.Exit(_silenceLock);
                }
            }
        }
    }

    private void HandleCaptureError(Exception ex)
    {
        try
        {
            OnCaptureError?.Invoke(this, ex);
        }
        catch { }

        try
        {
            CaptureErrorOccurred?.Invoke(this, ex.Message);
        }
        catch { }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;

        _isDisposed = true;
        _isCapturing = false;

        try { _silenceTimer.Dispose(); } catch { }

        try
        {
            _switchLock.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }

        try
        {
            TeardownCaptureClient();
        }
        finally
        {
            try { _switchLock.Release(); } catch { }
            try { _switchLock.Dispose(); } catch { }
        }

        lock (_bufferLock)
        {
            _accumulatorCount = 0;
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;

        _isDisposed = true;
        _isCapturing = false;

        try { await _silenceTimer.DisposeAsync().ConfigureAwait(false); } catch { }

        try
        {
            await _switchLock.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch { }

        try
        {
            TeardownCaptureClient();
        }
        finally
        {
            try { _switchLock.Release(); } catch { }
            try { _switchLock.Dispose(); } catch { }
        }

        lock (_bufferLock)
        {
            _accumulatorCount = 0;
        }
    }
}
