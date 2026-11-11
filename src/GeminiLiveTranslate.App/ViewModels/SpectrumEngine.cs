using System;
using System.Collections.Generic;

namespace GeminiLiveTranslate.App.ViewModels;

/// <summary>
/// Pure C# 36-band dynamic audio spectrum physics engine implementing
/// immediate attack, smooth frame decay, peak-hold decay, input sanitization,
/// arbitrary FFT bin resampling, and pill bar geometry layout calculation.
/// </summary>
public sealed class SpectrumEngine
{
    public const int DefaultBandCount = 36;
    public const float DefaultDecayRate = 0.08f;
    public const float DefaultPeakDecayRate = 0.03f;

    private readonly object _syncLock = new();
    private readonly float[] _currentLevels;
    private readonly float[] _peakLevels;
    private float _decayRate = DefaultDecayRate;
    private float _peakDecayRate = DefaultPeakDecayRate;

    public int BandCount { get; }

    public float DecayRate
    {
        get
        {
            lock (_syncLock)
            {
                return _decayRate;
            }
        }
        set
        {
            lock (_syncLock)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.0f)
                {
                    _decayRate = DefaultDecayRate;
                }
                else
                {
                    _decayRate = Math.Clamp(value, 0.0f, 1.0f);
                }
            }
        }
    }

    public float PeakDecayRate
    {
        get
        {
            lock (_syncLock)
            {
                return _peakDecayRate;
            }
        }
        set
        {
            lock (_syncLock)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.0f)
                {
                    _peakDecayRate = DefaultPeakDecayRate;
                }
                else
                {
                    _peakDecayRate = Math.Clamp(value, 0.0f, 1.0f);
                }
            }
        }
    }

    public IReadOnlyList<float> CurrentLevels
    {
        get
        {
            lock (_syncLock)
            {
                return (float[])_currentLevels.Clone();
            }
        }
    }

    public IReadOnlyList<float> PeakLevels
    {
        get
        {
            lock (_syncLock)
            {
                return (float[])_peakLevels.Clone();
            }
        }
    }

    public SpectrumEngine(int bandCount = DefaultBandCount)
    {
        BandCount = bandCount > 0 ? bandCount : DefaultBandCount;
        _currentLevels = new float[BandCount];
        _peakLevels = new float[BandCount];
    }

    /// <summary>
    /// Updates the 36-band spectrum levels from incoming FFT magnitudes.
    /// Applies NaN/Infinity sanitization, [0, 1] clamping, resampling to BandCount,
    /// immediate attack when rising, and smooth decay when falling.
    /// </summary>
    public void UpdateSpectrum(float[]? newBands)
    {
        if (newBands == null || newBands.Length == 0)
        {
            DecayAll();
            return;
        }

        lock (_syncLock)
        {
            for (int i = 0; i < BandCount; i++)
            {
                float target = MapBandValue(newBands, i, BandCount);

                if (target >= _currentLevels[i])
                {
                    // Immediate attack
                    _currentLevels[i] = target;
                }
                else
                {
                    // Smooth decay toward target
                    float decayed = _currentLevels[i] - _decayRate;
                    _currentLevels[i] = Math.Clamp(Math.Max(target, decayed), 0.0f, 1.0f);
                }

                if (_currentLevels[i] >= _peakLevels[i])
                {
                    _peakLevels[i] = _currentLevels[i];
                }
                else
                {
                    float peakDecayed = _peakLevels[i] - _peakDecayRate;
                    _peakLevels[i] = Math.Clamp(Math.Max(_currentLevels[i], peakDecayed), 0.0f, 1.0f);
                }
            }
        }
    }

    /// <summary>
    /// Decays all current and peak spectrum levels by one frame step, clamped to [0.0, 1.0].
    /// </summary>
    public void DecayAll()
    {
        lock (_syncLock)
        {
            for (int i = 0; i < BandCount; i++)
            {
                _currentLevels[i] = Math.Clamp(_currentLevels[i] - _decayRate, 0.0f, 1.0f);
                _peakLevels[i] = Math.Clamp(Math.Max(_currentLevels[i], _peakLevels[i] - _peakDecayRate), 0.0f, 1.0f);
            }
        }
    }

    /// <summary>
    /// Resets all current and peak levels immediately to zero.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            Array.Clear(_currentLevels, 0, BandCount);
            Array.Clear(_peakLevels, 0, BandCount);
        }
    }

    /// <summary>
    /// Computes the 36 rounded pill bar bounding rectangles for the given canvas dimensions.
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Width, double Height)> CalculateBarGeometries(
        double canvasWidth,
        double canvasHeight,
        double minBarHeight = 4.0)
    {
        if (double.IsNaN(canvasWidth) || double.IsNaN(canvasHeight) ||
            double.IsInfinity(canvasWidth) || double.IsInfinity(canvasHeight) ||
            canvasWidth <= 0.0 || canvasHeight <= 0.0)
        {
            return Array.Empty<(double X, double Y, double Width, double Height)>();
        }

        double effectiveMinHeight = double.IsNaN(minBarHeight) || double.IsInfinity(minBarHeight) || minBarHeight < 0.0
            ? 4.0
            : Math.Min(minBarHeight, canvasHeight);

        var geometries = new (double X, double Y, double Width, double Height)[BandCount];
        double slotWidth = canvasWidth / BandCount;
        double barWidth = slotWidth * 0.65;
        double spacing = slotWidth * 0.35;

        lock (_syncLock)
        {
            for (int i = 0; i < BandCount; i++)
            {
                float level = SanitizeLevel(_currentLevels[i]);
                double barHeight = Math.Clamp(Math.Max(effectiveMinHeight, canvasHeight * level), effectiveMinHeight, canvasHeight);
                double x = i * (barWidth + spacing) + spacing / 2.0;
                double y = Math.Max(0.0, canvasHeight - barHeight);

                geometries[i] = (x, y, barWidth, barHeight);
            }
        }

        return geometries;
    }

    public static float SanitizeLevel(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0.0f;
        }
        return Math.Clamp(value, 0.0f, 1.0f);
    }

    private static float MapBandValue(float[] input, int bandIndex, int targetBandCount)
    {
        if (input.Length == targetBandCount)
        {
            return SanitizeLevel(input[bandIndex]);
        }

        if (input.Length == 1)
        {
            return SanitizeLevel(input[0]);
        }

        if (input.Length > targetBandCount)
        {
            int start = (bandIndex * input.Length) / targetBandCount;
            int end = Math.Max(start + 1, ((bandIndex + 1) * input.Length) / targetBandCount);
            end = Math.Min(end, input.Length);

            float sum = 0.0f;
            int count = 0;
            for (int j = start; j < end; j++)
            {
                sum += SanitizeLevel(input[j]);
                count++;
            }
            return count > 0 ? Math.Clamp(sum / count, 0.0f, 1.0f) : 0.0f;
        }
        else
        {
            float pos = (float)bandIndex * (input.Length - 1) / Math.Max(1, targetBandCount - 1);
            int idx0 = Math.Clamp((int)MathF.Floor(pos), 0, input.Length - 1);
            int idx1 = Math.Clamp(idx0 + 1, 0, input.Length - 1);
            float frac = pos - idx0;

            float v0 = SanitizeLevel(input[idx0]);
            float v1 = SanitizeLevel(input[idx1]);
            return Math.Clamp(v0 + (v1 - v0) * frac, 0.0f, 1.0f);
        }
    }
}
