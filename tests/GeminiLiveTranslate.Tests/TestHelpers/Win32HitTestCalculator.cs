using System;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Mathematical hit-test calculator implementing 8-direction edge and corner hit-testing for floating overlay window.
/// </summary>
public static class Win32HitTestCalculator
{
    public const int DefaultBorderThickness = 8;
    public const int MinOverlayWidth = 360;
    public const int MinOverlayHeight = 100;
    public const int DefaultOverlayWidth = 640;
    public const int DefaultOverlayHeight = 140;

    /// <summary>
    /// Calculates Win32 WM_NCHITTEST result based on window geometry and cursor screen coordinates.
    /// </summary>
    public static int CalculateHitTest(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        double cursorX,
        double cursorY,
        int borderThickness = DefaultBorderThickness)
    {
        // Check if cursor is completely outside window
        if (cursorX < windowLeft || cursorX > windowLeft + windowWidth ||
            cursorY < windowTop || cursorY > windowTop + windowHeight)
        {
            return Win32InteropConstants.HTNOWHERE;
        }

        double relX = cursorX - windowLeft;
        double relY = cursorY - windowTop;

        bool onLeft = relX < borderThickness;
        bool onRight = relX >= windowWidth - borderThickness;
        bool onTop = relY < borderThickness;
        bool onBottom = relY >= windowHeight - borderThickness;

        // 4 Corners
        if (onTop && onLeft) return Win32InteropConstants.HTTOPLEFT;
        if (onTop && onRight) return Win32InteropConstants.HTTOPRIGHT;
        if (onBottom && onLeft) return Win32InteropConstants.HTBOTTOMLEFT;
        if (onBottom && onRight) return Win32InteropConstants.HTBOTTOMRIGHT;

        // 4 Edges
        if (onTop) return Win32InteropConstants.HTTOP;
        if (onBottom) return Win32InteropConstants.HTBOTTOM;
        if (onLeft) return Win32InteropConstants.HTLEFT;
        if (onRight) return Win32InteropConstants.HTRIGHT;

        // Interior: client area (allows dragging / control bar interaction)
        return Win32InteropConstants.HTCLIENT;
    }

    /// <summary>
    /// Applies bitwise extended style enabling click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED).
    /// </summary>
    public static int EnableClickThrough(int currentExStyle)
    {
        return currentExStyle | Win32InteropConstants.WS_EX_TRANSPARENT | Win32InteropConstants.WS_EX_LAYERED;
    }

    /// <summary>
    /// Applies bitwise extended style disabling click-through (removes WS_EX_TRANSPARENT).
    /// </summary>
    public static int DisableClickThrough(int currentExStyle)
    {
        return currentExStyle & ~Win32InteropConstants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Checks if the style has WS_EX_TRANSPARENT active.
    /// </summary>
    public static bool IsClickThrough(int currentExStyle)
    {
        return (currentExStyle & Win32InteropConstants.WS_EX_TRANSPARENT) == Win32InteropConstants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Clamps requested window dimension to MINMAXINFO constraints.
    /// </summary>
    public static (double Width, double Height) ClampToMinDimensions(double width, double height)
    {
        double clampedW = Math.Max(MinOverlayWidth, width);
        double clampedH = Math.Max(MinOverlayHeight, height);
        return (clampedW, clampedH);
    }

    /// <summary>
    /// Cycles opacity between 1.0 -> 0.5 -> 0.0 -> 1.0.
    /// </summary>
    public static double GetNextOpacity(double current) => current switch
    {
        >= 0.9 => 0.5,
        >= 0.4 => 0.0,
        _ => 1.0
    };

    /// <summary>
    /// Clamps overlay position within virtual desktop bounds.
    /// </summary>
    public static (double X, double Y) ClampOverlayPosition(
        double x,
        double y,
        double screenW,
        double screenH,
        double overlayW,
        double overlayH,
        double minScreenX = -3840,
        double minScreenY = -2160)
    {
        double clampedX = Math.Clamp(x, minScreenX, screenW - overlayW);
        double clampedY = Math.Clamp(y, minScreenY, screenH - overlayH);
        return (clampedX, clampedY);
    }

    /// <summary>
    /// Scales logical dimensions to physical pixels using DPI scaling factor.
    /// </summary>
    public static (double W, double H) ScaleDpi(double w, double h, double dpiScale) => (w * dpiScale, h * dpiScale);

    /// <summary>
    /// Validates whether a window handle is non-zero.
    /// </summary>
    public static bool IsValidHwnd(IntPtr hwnd) => hwnd != IntPtr.Zero;

    /// <summary>
    /// Resolves backdrop type considering high-contrast accessibility mode and valid range [0, 4].
    /// </summary>
    public static int ResolveBackdropType(int type, bool highContrast = false) =>
        highContrast ? Win32InteropConstants.DWMSBT_NONE : (type is >= 0 and <= 4 ? type : Win32InteropConstants.DWMSBT_AUTO);

    /// <summary>
    /// Clamps corner preference to valid Win32 range [0, 3].
    /// </summary>
    public static int ClampCornerPreference(int pref) => Math.Clamp(pref, 0, 3);
}

/// <summary>
/// Specification defaults for FloatingOverlayWindow.
/// </summary>
public static class OverlayWindowDefaults
{
    public const bool AllowsTransparency = true;
    public const bool Topmost = true;
    public const bool ShowInTaskbar = false;
    public const string WindowStyle = "None";
}
