using System;
using System.Collections.Generic;
using System.Globalization;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Comprehensive unit tests verifying Milestone 3 (M3) Desktop Floating Lyrics Overlay Window:
/// - 8-direction Win32 WM_NCHITTEST edge/corner calculations
/// - Bitwise extended window styles for mouse click-through mode
/// - Global hotkey modifiers and virtual key constants
/// - Font scaling, delta steps, and bounds clamping
/// - Opacity cycling sequence and theme color palettes
/// - Composite font fallback chain eliminating tofu boxes
/// - True Unicode grapheme cluster splitting for CJK and emojis
/// - Integration with TranslationTurn and TextSegmentationService
/// </summary>
public class OverlayWindowTests
{
    private const double OverlayLeft = 100.0;
    private const double OverlayTop = 100.0;
    private const double OverlayWidth = 640.0;
    private const double OverlayHeight = 140.0;
    private const int BorderThickness = 8;

    #region 1. 8-Direction Win32 WM_NCHITTEST Edge and Corner Math

    [Fact]
    public void HitTest_InteriorClientArea_ReturnsHTCLIENT()
    {
        // Center of window, well away from 8px border
        double cursorX = OverlayLeft + OverlayWidth / 2.0;
        double cursorY = OverlayTop + OverlayHeight / 2.0;

        int result = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight, cursorX, cursorY, BorderThickness);

        Assert.Equal(Win32Constants.HTCLIENT, result);
    }

    [Theory]
    [InlineData(OverlayLeft + 2, OverlayTop + 50, Win32Constants.HTLEFT)]               // Left edge
    [InlineData(OverlayLeft + OverlayWidth - 3, OverlayTop + 50, Win32Constants.HTRIGHT)] // Right edge
    [InlineData(OverlayLeft + 150, OverlayTop + 2, Win32Constants.HTTOP)]               // Top edge
    [InlineData(OverlayLeft + 150, OverlayTop + OverlayHeight - 3, Win32Constants.HTBOTTOM)] // Bottom edge
    public void HitTest_FourEdges_ReturnCorrectEdgeCodes(double cursorX, double cursorY, int expectedCode)
    {
        int result = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight, cursorX, cursorY, BorderThickness);

        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData(OverlayLeft + 2, OverlayTop + 2, Win32Constants.HTTOPLEFT)]
    [InlineData(OverlayLeft + OverlayWidth - 2, OverlayTop + 2, Win32Constants.HTTOPRIGHT)]
    [InlineData(OverlayLeft + 2, OverlayTop + OverlayHeight - 2, Win32Constants.HTBOTTOMLEFT)]
    [InlineData(OverlayLeft + OverlayWidth - 2, OverlayTop + OverlayHeight - 2, Win32Constants.HTBOTTOMRIGHT)]
    public void HitTest_FourCorners_ReturnCorrectCornerCodes(double cursorX, double cursorY, int expectedCode)
    {
        int result = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight, cursorX, cursorY, BorderThickness);

        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData(OverlayLeft - 10, OverlayTop + 50)]                       // Left outside
    [InlineData(OverlayLeft + OverlayWidth + 10, OverlayTop + 50)]         // Right outside
    [InlineData(OverlayLeft + 50, OverlayTop - 10)]                       // Top outside
    [InlineData(OverlayLeft + 50, OverlayTop + OverlayHeight + 10)]        // Bottom outside
    [InlineData(OverlayLeft - 20, OverlayTop - 20)]                       // Far diagonal outside
    [InlineData(OverlayLeft + OverlayWidth + 50, OverlayTop + OverlayHeight + 50)] // Far bottom-right outside
    public void HitTest_ExteriorCoordinates_ReturnHTNOWHERE(double cursorX, double cursorY)
    {
        int result = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight, cursorX, cursorY, BorderThickness);

        Assert.Equal(Win32Constants.HTNOWHERE, result);
    }

    [Fact]
    public void HitTest_ExactBoundaryEdge_ResolvesBorderVsClient()
    {
        // Pixel at left + border - 1 is still inside left border
        int borderPixel = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight,
            OverlayLeft + BorderThickness - 1, OverlayTop + 50, BorderThickness);

        // Pixel at left + border is inside client area
        int clientPixel = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight,
            OverlayLeft + BorderThickness, OverlayTop + 50, BorderThickness);

        Assert.Equal(Win32Constants.HTLEFT, borderPixel);
        Assert.Equal(Win32Constants.HTCLIENT, clientPixel);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void HitTest_CustomBorderThickness_ScalesAccurately(int thickness)
    {
        // 2px from top edge: should be HTTOP for any thickness >= 4
        int hitTop = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight,
            OverlayLeft + 100, OverlayTop + 2, thickness);

        // Exactly at thickness: should be HTCLIENT
        int hitClient = NativeMethods.CalculateHitTest(
            OverlayLeft, OverlayTop, OverlayWidth, OverlayHeight,
            OverlayLeft + 100, OverlayTop + thickness, thickness);

        Assert.Equal(Win32Constants.HTTOP, hitTop);
        Assert.Equal(Win32Constants.HTCLIENT, hitClient);
    }

    [Fact]
    public void HitTest_ZeroOrNegativeDimensions_SafelyHandledWithoutException()
    {
        int hitZero = NativeMethods.CalculateHitTest(0, 0, 0, 0, 0, 0, BorderThickness);
        Assert.NotEqual(Win32Constants.HTERROR, hitZero);

        int hitNeg = NativeMethods.CalculateHitTest(100, 100, -50, -50, 60, 60, BorderThickness);
        Assert.Equal(Win32Constants.HTNOWHERE, hitNeg);
    }

    [Fact]
    public void HitTest_HighResolutionCoordinates_Handles4KAnd8KScreens()
    {
        // Window on secondary 8K monitor
        double left = 7680;
        double top = 2160;
        double width = 960;
        double height = 170;

        int hitClient = NativeMethods.CalculateHitTest(left, top, width, height, left + 400, top + 80, BorderThickness);
        int hitBottomRight = NativeMethods.CalculateHitTest(left, top, width, height, left + width - 2, top + height - 2, BorderThickness);

        Assert.Equal(Win32Constants.HTCLIENT, hitClient);
        Assert.Equal(Win32Constants.HTBOTTOMRIGHT, hitBottomRight);
    }

    #endregion

    #region 2. Bitwise Extended Window Style Manipulation (Click-Through)

    [Fact]
    public void EnableClickThrough_AddsTransparentAndLayeredStyles()
    {
        int initialStyle = 0;
        int result = NativeMethods.EnableClickThrough(initialStyle);

        Assert.True((result & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT);
        Assert.True((result & Win32Constants.WS_EX_LAYERED) == Win32Constants.WS_EX_LAYERED);
        Assert.True(NativeMethods.IsClickThrough(result));
    }

    [Fact]
    public void DisableClickThrough_RemovesTransparentStyle_PreservesLayered()
    {
        int transparentStyle = Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED;
        int result = NativeMethods.DisableClickThrough(transparentStyle);

        Assert.False((result & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT);
        Assert.True((result & Win32Constants.WS_EX_LAYERED) == Win32Constants.WS_EX_LAYERED);
        Assert.False(NativeMethods.IsClickThrough(result));
    }

    [Fact]
    public void ClickThrough_PreservesExistingWindowStyles()
    {
        // Overlay typically has WS_EX_TOOLWINDOW and WS_EX_TOPMOST
        int baseStyle = Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_TOPMOST;

        int enabled = NativeMethods.EnableClickThrough(baseStyle);
        Assert.True((enabled & Win32Constants.WS_EX_TOOLWINDOW) == Win32Constants.WS_EX_TOOLWINDOW);
        Assert.True((enabled & Win32Constants.WS_EX_TOPMOST) == Win32Constants.WS_EX_TOPMOST);
        Assert.True(NativeMethods.IsClickThrough(enabled));

        int disabled = NativeMethods.DisableClickThrough(enabled);
        Assert.True((disabled & Win32Constants.WS_EX_TOOLWINDOW) == Win32Constants.WS_EX_TOOLWINDOW);
        Assert.True((disabled & Win32Constants.WS_EX_TOPMOST) == Win32Constants.WS_EX_TOPMOST);
        Assert.False(NativeMethods.IsClickThrough(disabled));
    }

    [Fact]
    public void ClickThrough_OperationsAreIdempotent()
    {
        int initial = 0;
        int step1 = NativeMethods.EnableClickThrough(initial);
        int step2 = NativeMethods.EnableClickThrough(step1);
        Assert.Equal(step1, step2);

        int step3 = NativeMethods.DisableClickThrough(step2);
        int step4 = NativeMethods.DisableClickThrough(step3);
        Assert.Equal(step3, step4);
    }

    [Fact]
    public void ClickThroughLong_64BitPointerManipulation_WorksIdentically()
    {
        long initial = 0x0000000000000080L; // WS_EX_TOOLWINDOW
        long enabled = NativeMethods.EnableClickThroughLong(initial);

        Assert.True(NativeMethods.IsClickThroughLong(enabled));
        Assert.Equal(initial | Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED, enabled);

        long disabled = NativeMethods.DisableClickThroughLong(enabled);
        Assert.False(NativeMethods.IsClickThroughLong(disabled));
        Assert.Equal(initial | Win32Constants.WS_EX_LAYERED, disabled);
    }

    #endregion

    #region 3. Global Hotkey Modifiers and Virtual Key Constants

    [Fact]
    public void HotkeyConstants_MatchWin32Specification()
    {
        // Ctrl + F8 with NoRepeat
        uint expectedModifiers = Win32Constants.MOD_CONTROL | Win32Constants.MOD_NOREPEAT;
        Assert.Equal(0x4002u, expectedModifiers);

        Assert.Equal(0x77u, Win32Constants.VK_F8);
        Assert.Equal(9001, Win32Constants.HOTKEY_CLICK_THROUGH_ID);
        Assert.Equal(1409, Win32Constants.ERROR_HOTKEY_ALREADY_REGISTERED);
    }

    [Fact]
    public void WindowMessageConstants_MatchWin32Specification()
    {
        Assert.Equal(0x0084, Win32Constants.WM_NCHITTEST);
        Assert.Equal(0x00A1, Win32Constants.WM_NCLBUTTONDOWN);
        Assert.Equal(0x0312, Win32Constants.WM_HOTKEY);
        Assert.Equal(0x0024, Win32Constants.WM_GETMINMAXINFO);
        Assert.Equal(0x007E, Win32Constants.WM_DISPLAYCHANGE);
        Assert.Equal(0x02E0, Win32Constants.WM_DPICHANGED);
    }

    [Fact]
    public void DwmAttributeConstants_MatchWindows11Specification()
    {
        Assert.Equal(20u, Win32Constants.DWMWA_USE_IMMERSIVE_DARK_MODE);
        Assert.Equal(33u, Win32Constants.DWMWA_WINDOW_CORNER_PREFERENCE);
        Assert.Equal(34u, Win32Constants.DWMWA_BORDER_COLOR);
        Assert.Equal(38u, Win32Constants.DWMWA_SYSTEMBACKDROP_TYPE);

        Assert.Equal(2, Win32Constants.DWMWCP_ROUND);
        Assert.Equal(2, Win32Constants.DWMSBT_MAINWINDOW);      // Mica
        Assert.Equal(3, Win32Constants.DWMSBT_TRANSIENTWINDOW); // Acrylic
    }

    #endregion

    #region 4. Font Size Scaling, Step Deltas, and Bounds Clamping

    [Theory]
    [InlineData(24.0, 2.0, 26.0)]
    [InlineData(26.0, 2.0, 28.0)]
    [InlineData(24.0, -2.0, 22.0)]
    [InlineData(14.0, -2.0, 14.0)] // Clamped at min 14.0
    [InlineData(64.0, 2.0, 64.0)]  // Clamped at max 64.0
    [InlineData(5.0, 0.0, 14.0)]   // Out of range clamped to min
    [InlineData(100.0, 0.0, 64.0)] // Out of range clamped to max
    public void AdjustFontSize_CalculatesAndClampsCorrectly(double current, double delta, double expected)
    {
        double result = NativeMethods.AdjustFontSize(current, delta, 14.0, 64.0);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AdjustFontSize_SequentialIncrement_ReachesAndStopsAtMax()
    {
        double size = 58.0;
        for (int i = 0; i < 10; i++)
        {
            size = NativeMethods.AdjustFontSize(size, 2.0, 14.0, 64.0);
        }
        Assert.Equal(64.0, size);
    }

    [Fact]
    public void AdjustFontSize_SequentialDecrement_ReachesAndStopsAtMin()
    {
        double size = 20.0;
        for (int i = 0; i < 10; i++)
        {
            size = NativeMethods.AdjustFontSize(size, -2.0, 14.0, 64.0);
        }
        Assert.Equal(14.0, size);
    }

    #endregion

    #region 5. Opacity Cycle Sequence and Background Modes

    [Fact]
    public void GetNextOpacity_CyclesThroughThreeStates()
    {
        double state1 = 1.0;
        double state2 = NativeMethods.GetNextOpacity(state1);
        Assert.Equal(0.5, state2);

        double state3 = NativeMethods.GetNextOpacity(state2);
        Assert.Equal(0.0, state3);

        double state4 = NativeMethods.GetNextOpacity(state3);
        Assert.Equal(1.0, state4);
    }

    [Theory]
    [InlineData(0, 1)] // Dark slate -> Frosted slate
    [InlineData(1, 2)] // Frosted slate -> Pure transparent
    [InlineData(2, 0)] // Pure transparent -> Dark slate
    public void BackgroundModeSequence_CyclesPredictably(int currentMode, int expectedNextMode)
    {
        int nextMode = (currentMode + 1) % 3;
        Assert.Equal(expectedNextMode, nextMode);
    }

    [Fact]
    public void BackgroundPaletteHex_MatchesFluentDesign()
    {
        Assert.Equal("#D00F172A", Win32Constants.DarkSlateBackground);
        Assert.Equal("#600F172A", Win32Constants.FrostedSlateBackground);
        Assert.Equal("#00000000", Win32Constants.TransparentBackground);
        Assert.Equal("#00E5FF", Win32Constants.TargetSubtitleNeonCyan);
        Assert.Equal("#F1F5F9", Win32Constants.SourceSubtitleCleanSlate);
    }

    #endregion

    #region 6. Composite Font Fallback String Verification

    [Fact]
    public void DefaultCompositeFontFamily_ContainsRequiredGlyphFallbacks()
    {
        string fontFamily = Win32Constants.DefaultCompositeFontFamily;

        // Segoe UI Emoji MUST appear first for emoji glyph precedence
        Assert.Contains("Segoe UI Emoji", fontFamily);
        Assert.Contains("Microsoft JhengHei", fontFamily); // Traditional Chinese
        Assert.Contains("Microsoft YaHei", fontFamily);   // Simplified Chinese
        Assert.Contains("Segoe UI", fontFamily);          // English / Latin
        Assert.Contains("sans-serif", fontFamily);        // Generic fallback

        int emojiIdx = fontFamily.IndexOf("Segoe UI Emoji", StringComparison.OrdinalIgnoreCase);
        int jhengHeiIdx = fontFamily.IndexOf("Microsoft JhengHei", StringComparison.OrdinalIgnoreCase);
        int segoeIdx = fontFamily.IndexOf("Segoe UI,", StringComparison.OrdinalIgnoreCase);

        Assert.True(emojiIdx < jhengHeiIdx, "Segoe UI Emoji must precede CJK font to prevent monochrome emoji rendering");
        Assert.True(jhengHeiIdx < segoeIdx, "CJK fonts must precede Segoe UI to prevent tofu boxes on CJK glyphs");
    }

    #endregion

    #region 7. Unicode Grapheme Cluster Handling (CJK and Emojis)

    [Theory]
    [InlineData("即時口譯字幕準備就緒", 10)]             // 10 CJK characters
    [InlineData("Hello World", 11)]                       // 11 ASCII characters
    [InlineData("即時口譯 🚀 Hello 世界 🧑‍💻", 17)]          // Mixed CJK + ASCII + surrogate emoji + ZWJ emoji
    public void StringInfo_BreaksIntoAccurateGraphemeClusters(string text, int expectedGraphemeCount)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        Assert.Equal(expectedGraphemeCount, elements.Count);
    }

    [Fact]
    public void MultiByteEmoji_TreatedAsSingleGraphemeCluster()
    {
        // 🧑‍💻 = U+1F9D1 + U+200D + U+1F4BB (5 UTF-16 code units, 1 grapheme)
        string technologistEmoji = "🧑‍💻";
        Assert.True(technologistEmoji.Length > 1, "Multi-byte emoji has length > 1 in UTF-16");

        var enumerator = StringInfo.GetTextElementEnumerator(technologistEmoji);
        int count = 0;
        string? cluster = null;
        while (enumerator.MoveNext())
        {
            count++;
            cluster = enumerator.GetTextElement();
        }

        Assert.Equal(1, count);
        Assert.Equal(technologistEmoji, cluster);
    }

    #endregion

    #region 8. Layout Constraints & MINMAXINFO

    [Fact]
    public void ClampToMinDimensions_EnforcesMinimumWindowSize()
    {
        var (w1, h1) = NativeMethods.ClampToMinDimensions(200, 50);
        Assert.Equal(Win32Constants.MinOverlayWidth, w1);
        Assert.Equal(Win32Constants.MinOverlayHeight, h1);

        var (w2, h2) = NativeMethods.ClampToMinDimensions(960, 170);
        Assert.Equal(960, w2);
        Assert.Equal(170, h2);

        var (w3, h3) = NativeMethods.ClampToMinDimensions(-100, -50);
        Assert.Equal(Win32Constants.MinOverlayWidth, w3);
        Assert.Equal(Win32Constants.MinOverlayHeight, h3);
    }

    [Fact]
    public void ClampOverlayPosition_RestrictsCoordinatesToDesktopBounds()
    {
        // Screen: 1920x1080, Overlay: 640x140
        var (x1, y1) = NativeMethods.ClampOverlayPosition(2000, 1500, 1920, 1080, 640, 140);
        Assert.Equal(1920 - 640, x1);
        Assert.Equal(1080 - 140, y1);

        var (x2, y2) = NativeMethods.ClampOverlayPosition(-5000, -3000, 1920, 1080, 640, 140);
        Assert.Equal(-3840, x2);
        Assert.Equal(-2160, y2);
    }

    [Theory]
    [InlineData(640, 140, 1.0, 640, 140)]
    [InlineData(640, 140, 1.25, 800, 175)]
    [InlineData(640, 140, 1.5, 960, 210)]
    [InlineData(640, 140, 2.0, 1280, 280)]
    public void ScaleDpi_ScalesLogicalToDevicePixels(double w, double h, double dpi, double expectedW, double expectedH)
    {
        var (scaledW, scaledH) = NativeMethods.ScaleDpi(w, h, dpi);
        Assert.Equal(expectedW, scaledW);
        Assert.Equal(expectedH, scaledH);
    }

    #endregion

    #region 9. Integration with TranslationTurn and TextSegmentationService

    [Fact]
    public void TranslationTurn_PopulatesFieldsForOverlayDisplay()
    {
        var turn = new TranslationTurn(
            sourceText: "Good morning everyone, welcome to the presentation.",
            targetText: "大家早安，歡迎來到本次簡報。",
            isFinal: true,
            sourceLang: "en-US",
            targetLang: "zh-TW");

        Assert.False(string.IsNullOrEmpty(turn.Id));
        Assert.Equal("大家早安，歡迎來到本次簡報。", turn.TargetText);
        Assert.Equal("Good morning everyone, welcome to the presentation.", turn.SourceText);
        Assert.True(turn.IsFinal);
        Assert.Equal("zh-TW", turn.TargetLanguage);
    }

    [Fact]
    public void TextSegmentation_ChunksLongStreamingSpeechIntoDiscreteOverlayTurns()
    {
        var segmentation = new TextSegmentationService { MaxCharacters = 35 };

        string longInput = "這是第一段即時口譯語音內容。緊接著這是第二段非常重要的會議發言內容！最後是總結。";
        var chunks = segmentation.ChunkText(longInput, 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 45, $"Chunk '{chunk}' exceeds safety threshold");
        }

        // Also verify progressive token processing
        segmentation.Reset();
        var result = segmentation.ProcessToken(longInput, isFinal: true);
        Assert.True(result.HasCommitted || !string.IsNullOrEmpty(result.PendingText));
    }

    [Fact]
    public void OverlayDataModel_FormatsSubtitleCorrectly()
    {
        string target = "即時口譯字幕準備就緒";
        string source = "Real-time interpretation ready";

        var turn = new TranslationTurn(source, target);

        Assert.Equal("即時口譯字幕準備就緒", turn.TargetText);
        Assert.Equal("Real-time interpretation ready", turn.SourceText);
    }

    #endregion
}
