using System;
using System.Collections.Generic;
using System.Globalization;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Opaque-box model validating DirectWrite text geometry layout, 3-layer stroke/glow rendering,
/// and composite fallback fonts eliminating tofu glyphs.
/// </summary>
public class DirectWriteTextModel
{
    public const string DefaultCompositeFontFamily = "Segoe UI Emoji, Microsoft JhengHei, Segoe UI, sans-serif";
    public const double MinFontSize = 12.0;
    public const double MaxFontSize = 72.0;
    public const double DefaultFontSize = 24.0;
    public const double DefaultStrokeThickness = 2.5;

    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = DefaultFontSize;
    public double StrokeThickness { get; set; } = DefaultStrokeThickness;
    public string FontFamily { get; set; } = DefaultCompositeFontFamily;
    public double ContainerWidth { get; set; } = 640.0;
    public double ContainerHeight { get; set; } = 140.0;
    public string GlowColor { get; set; } = "#80000000";
    public string StrokeColor { get; set; } = "#FF000000";
    public string FillColor { get; set; } = "#FFFFFFFF";
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Simulates closing/hiding overlay window from control bar command.
    /// </summary>
    public void Close(object? parameter = null)
    {
        IsVisible = false;
    }

    public DirectWriteTextModel(string text = "", double fontSize = DefaultFontSize)
    {
        Text = text;
        FontSize = Math.Clamp(fontSize, MinFontSize, MaxFontSize);
    }

    /// <summary>
    /// Increases font size by delta (default +2pt) clamping to MaxFontSize.
    /// </summary>
    public void IncreaseFontSize(double delta = 2.0)
    {
        FontSize = Math.Min(MaxFontSize, FontSize + delta);
    }

    /// <summary>
    /// Decreases font size by delta (default -2pt) clamping to MinFontSize.
    /// </summary>
    public void DecreaseFontSize(double delta = 2.0)
    {
        FontSize = Math.Max(MinFontSize, FontSize - delta);
    }

    /// <summary>
    /// Estimates rendered text geometry bounds based on glyph count and font metrics.
    /// </summary>
    public (double Width, double Height, int LineCount) MeasureTextGeometry()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return (0.0, 0.0, 0);
        }

        double avgCharWidth = FontSize * 0.75;
        double lineHeight = FontSize * 1.35;
        double maxLineWidth = Math.Max(ContainerWidth - (StrokeThickness * 4), 50.0);

        int currentLineChars = 0;
        int lineCount = 1;
        double currentLineWidth = 0;
        double maxSeenWidth = 0;

        var textElements = BreakIntoTextElements(Text);
        foreach (var elem in textElements)
        {
            // CJK characters or emojis occupy wider cell
            bool isWide = elem.Length > 1 || (elem[0] >= 0x2E80 && elem[0] <= 0x9FFF) || (elem[0] >= 0xAC00 && elem[0] <= 0xD7AF);
            double charWidth = isWide ? (FontSize * 1.05) : avgCharWidth;

            if (currentLineWidth + charWidth > maxLineWidth)
            {
                lineCount++;
                maxSeenWidth = Math.Max(maxSeenWidth, currentLineWidth);
                currentLineWidth = charWidth;
                currentLineChars = 1;
            }
            else
            {
                currentLineWidth += charWidth;
                currentLineChars++;
            }
        }

        maxSeenWidth = Math.Max(maxSeenWidth, currentLineWidth);
        double totalHeight = lineCount * lineHeight;

        return (maxSeenWidth, totalHeight, lineCount);
    }

    /// <summary>
    /// Breaks string into true Unicode grapheme clusters (correctly handling multi-code-point emojis).
    /// </summary>
    public static List<string> BreakIntoTextElements(string text)
    {
        var elements = new List<string>();
        if (string.IsNullOrEmpty(text)) return elements;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }
        return elements;
    }

    /// <summary>
    /// Validates that the font family contains required glyph fallbacks.
    /// </summary>
    public bool HasRequiredFontFallbacks()
    {
        return FontFamily.Contains("Segoe UI Emoji", StringComparison.OrdinalIgnoreCase) &&
               FontFamily.Contains("Microsoft JhengHei", StringComparison.OrdinalIgnoreCase) &&
               FontFamily.Contains("Segoe UI", StringComparison.OrdinalIgnoreCase);
    }
}
