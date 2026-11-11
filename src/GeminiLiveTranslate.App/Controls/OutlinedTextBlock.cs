using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GeminiLiveTranslate.App.Interop;

namespace GeminiLiveTranslate.App.Controls;

/// <summary>
/// High-contrast typography control implementing 3-layer rendering (ambient outer glow,
/// crisp black outline stroke, and vibrant text fill) with composite font fallback preventing tofu boxes.
/// </summary>
public class OutlinedTextBlock : FrameworkElement
{
    public const string DefaultCompositeFontFamily = Win32Constants.DefaultCompositeFontFamily;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(4.5, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(new FontFamily(DefaultCompositeFontFamily),
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(TextAlignment.Center, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxTextWidthProperty =
        DependencyProperty.Register(nameof(MaxTextWidth), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double MaxTextWidth
    {
        get => (double)GetValue(MaxTextWidthProperty);
        set => SetValue(MaxTextWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return new Size(0, 0);
        }

        double constraintW = double.IsNaN(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        var ft = CreateFormattedText(constraintW);
        double safeStroke = (double.IsNaN(StrokeThickness) || StrokeThickness < 0 || double.IsInfinity(StrokeThickness))
            ? 0.0
            : StrokeThickness;

        double padding = safeStroke * 2;
        double width = ft.WidthIncludingTrailingWhitespace + padding;
        if (!double.IsInfinity(constraintW) && constraintW > 0)
        {
            width = Math.Min(constraintW, width);
        }
        double height = ft.Height + padding;
        double finalW = double.IsNaN(width) ? 0 : Math.Max(0, width);
        double finalH = double.IsNaN(height) ? 0 : Math.Max(0, height);
        return new Size(finalW, finalH);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        double actualW = double.IsNaN(ActualWidth) || double.IsInfinity(ActualWidth) ? 0 : Math.Max(0, ActualWidth);
        double actualH = double.IsNaN(ActualHeight) || double.IsInfinity(ActualHeight) ? 0 : Math.Max(0, ActualHeight);

        var ft = CreateFormattedText(actualW);

        double safeStroke = (double.IsNaN(StrokeThickness) || StrokeThickness < 0 || double.IsInfinity(StrokeThickness))
            ? 0.0
            : StrokeThickness;

        // Position coordinates
        double x = 0;
        if (TextAlignment == TextAlignment.Center)
        {
            x = (actualW - ft.Width) / 2.0;
        }
        else if (TextAlignment == TextAlignment.Right)
        {
            x = actualW - ft.Width - safeStroke;
        }
        else
        {
            x = safeStroke;
        }
        double y = Math.Max(0, (actualH - ft.Height) / 2.0);

        if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
        if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;

        Geometry? textGeometry = ft.BuildGeometry(new Point(x, y));
        if (textGeometry == null)
        {
            return;
        }

        // Layer 1: Ambient Outer Glow / Shadow (Thick pen, black alpha #A0000000, round caps & joins)
        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), safeStroke + 4.0)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        glowPen.Freeze();
        dc.DrawGeometry(null, glowPen, textGeometry);

        // Layer 2: Crisp Black Outline Stroke (StrokeBrush, StrokeThickness, round caps & joins)
        var strokePen = new Pen(StrokeBrush ?? Brushes.Black, safeStroke)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        strokePen.Freeze();
        dc.DrawGeometry(null, strokePen, textGeometry);

        // Layer 3: High-contrast Text Fill (FillBrush)
        dc.DrawGeometry(FillBrush ?? Brushes.White, null, textGeometry);
    }

    private FormattedText CreateFormattedText(double maxConstraintWidth)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);

        double safeFontSize = (double.IsNaN(FontSize) || FontSize <= 0 || double.IsInfinity(FontSize))
            ? 24.0
            : Math.Clamp(FontSize, 1.0, 500.0);

        var ft = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            safeFontSize,
            Brushes.White,
            pixelsPerDip)
        {
            TextAlignment = TextAlignment,
            Trimming = TextTrimming.WordEllipsis
        };

        double safeStroke = (double.IsNaN(StrokeThickness) || StrokeThickness < 0 || double.IsInfinity(StrokeThickness))
            ? 0.0
            : StrokeThickness;

        double padding = safeStroke * 2 + 16;
        double constraint = !double.IsInfinity(maxConstraintWidth) && !double.IsNaN(maxConstraintWidth) && maxConstraintWidth > padding
            ? maxConstraintWidth - padding
            : (!double.IsInfinity(MaxTextWidth) && !double.IsNaN(MaxTextWidth) && MaxTextWidth > 0 ? MaxTextWidth : double.PositiveInfinity);

        if (!double.IsInfinity(constraint) && constraint > 0)
        {
            ft.MaxTextWidth = Math.Max(50, constraint);
        }

        return ft;
    }
}
