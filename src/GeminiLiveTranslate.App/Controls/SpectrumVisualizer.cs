using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using GeminiLiveTranslate.App.ViewModels;

namespace GeminiLiveTranslate.App.Controls;

/// <summary>
/// Hardware-accelerated 36-band dynamic audio spectrum waveform visualizer control
/// with Fluent Design 2 neon cyan-to-purple gradient, smooth decay physics, and peak-hold indicators.
/// </summary>
public class SpectrumVisualizer : FrameworkElement
{
    public const int BandCount = SpectrumEngine.DefaultBandCount;

    private readonly SpectrumEngine _engine = new(BandCount);
    private readonly LinearGradientBrush _barBrush;
    private readonly SolidColorBrush _peakBrush;
    private readonly Pen _baselinePen;
    private bool _isRenderingHooked;

    public static readonly DependencyProperty DecayRateProperty =
        DependencyProperty.Register(
            nameof(DecayRate),
            typeof(double),
            typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(
                (double)SpectrumEngine.DefaultDecayRate,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnDecayRateChanged));

    public static readonly DependencyProperty RmsLevelProperty =
        DependencyProperty.Register(
            nameof(RmsLevel),
            typeof(double),
            typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpectrumDataProperty =
        DependencyProperty.Register(
            nameof(SpectrumData),
            typeof(float[]),
            typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSpectrumDataChanged));

    public double DecayRate
    {
        get => (double)GetValue(DecayRateProperty);
        set => SetValue(DecayRateProperty, value);
    }

    public double RmsLevel
    {
        get => (double)GetValue(RmsLevelProperty);
        set => SetValue(RmsLevelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public float[]? SpectrumData
    {
        get => (float[]?)GetValue(SpectrumDataProperty);
        set => SetValue(SpectrumDataProperty, value);
    }

    public IReadOnlyList<float> CurrentLevels => _engine.CurrentLevels;
    public IReadOnlyList<float> PeakLevels => _engine.PeakLevels;

    public SpectrumVisualizer()
    {
        SnapsToDevicePixels = true;

        // Fluent 2 Neon Gradient: Electric Cyan (#00E5FF) -> Vibrant Purple (#A855F7)
        _barBrush = new LinearGradientBrush(
            Color.FromRgb(0x00, 0xE5, 0xFF),
            Color.FromRgb(0xA8, 0x55, 0xF7),
            new Point(0.0, 1.0),
            new Point(0.0, 0.0));
        _barBrush.Freeze();

        _peakBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x38, 0xBD, 0xF8));
        _peakBrush.Freeze();

        var baselineBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x64, 0x74, 0x8B));
        baselineBrush.Freeze();
        _baselinePen = new Pen(baselineBrush, 1.0);
        _baselinePen.Freeze();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void UpdateSpectrum(float[]? newBands)
    {
        _engine.UpdateSpectrum(newBands);
        SafeInvalidateVisual();
    }

    public void UpdateFftBins(float[]? fftMagnitudes)
    {
        UpdateSpectrum(fftMagnitudes);
    }

    public void DecayAll()
    {
        _engine.DecayAll();
        SafeInvalidateVisual();
    }

    public void Reset()
    {
        _engine.Reset();
        SafeInvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isRenderingHooked)
        {
            CompositionTarget.Rendering += OnCompositionRendering;
            _isRenderingHooked = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            _isRenderingHooked = false;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        var levels = _engine.CurrentLevels;
        bool hasEnergy = false;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] > 0.001f)
            {
                hasEnergy = true;
                break;
            }
        }

        if (hasEnergy)
        {
            _engine.DecayAll();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 360.0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 64.0 : availableSize.Height;
        return new Size(Math.Max(120.0, w), Math.Max(32.0, h));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0.0 || height <= 0.0 || double.IsNaN(width) || double.IsNaN(height))
        {
            return;
        }

        // Draw subtle baseline
        dc.DrawLine(_baselinePen, new Point(0, height - 1.0), new Point(width, height - 1.0));

        var barRects = _engine.CalculateBarGeometries(width, height, minBarHeight: 4.0);
        var peaks = _engine.PeakLevels;

        for (int i = 0; i < barRects.Count; i++)
        {
            var (x, y, barW, barH) = barRects[i];
            double radius = Math.Min(3.0, barW / 2.0);
            dc.DrawRoundedRectangle(_barBrush, null, new Rect(x, y, barW, barH), radius, radius);

            if (i < peaks.Count && peaks[i] > 0.05f)
            {
                double peakY = Math.Clamp(height - height * peaks[i] - 3.0, 0.0, Math.Max(0.0, y - 2.0));
                if (peakY < y - 1.0)
                {
                    dc.DrawRoundedRectangle(_peakBrush, null, new Rect(x, peakY, barW, 2.0), 1.0, 1.0);
                }
            }
        }
    }

    private void SafeInvalidateVisual()
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(InvalidateVisual);
        }
        else
        {
            InvalidateVisual();
        }
    }

    private static void OnDecayRateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumVisualizer visualizer && e.NewValue is double newRate)
        {
            visualizer._engine.DecayRate = (float)newRate;
        }
    }

    private static void OnSpectrumDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumVisualizer visualizer)
        {
            visualizer.UpdateSpectrum(e.NewValue as float[]);
        }
    }
}
