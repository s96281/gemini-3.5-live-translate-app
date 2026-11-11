using System;
using System.Runtime.InteropServices;

namespace GeminiLiveTranslate.App.Interop;

/// <summary>
/// Win32 P/Invoke declarations, interoperability structures, and mathematical hit-test calculation helpers.
/// </summary>
public static class NativeMethods
{
    #region Win32 Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;

        public static MARGINS FullWindow => new MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };
    }

    #endregion

    #region P/Invoke Declarations

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    #endregion

    #region Calculation Helpers

    public const int DefaultBorderThickness = 8;
    public const int MinOverlayWidth = 360;
    public const int MinOverlayHeight = 100;
    public const double MinFontSize = 14.0;
    public const double MaxFontSize = 64.0;

    /// <summary>
    /// Calculates Win32 WM_NCHITTEST result based on window geometry and cursor screen coordinates.
    /// Supports all 8 border/corner directions, interior client area, outside boundary detection,
    /// and multi-monitor coordinates. Safely returns HTNOWHERE for NaN or Infinity inputs.
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
        // Safe check for NaN or Infinity inputs
        if (double.IsNaN(cursorX) || double.IsNaN(cursorY) ||
            double.IsNaN(windowLeft) || double.IsNaN(windowTop) ||
            double.IsNaN(windowWidth) || double.IsNaN(windowHeight) ||
            double.IsInfinity(cursorX) || double.IsInfinity(cursorY) ||
            double.IsInfinity(windowLeft) || double.IsInfinity(windowTop) ||
            double.IsInfinity(windowWidth) || double.IsInfinity(windowHeight))
        {
            return Win32Constants.HTNOWHERE;
        }

        // Safe check for inverted or negative dimensions
        if (windowWidth < 0 || windowHeight < 0)
        {
            return Win32Constants.HTNOWHERE;
        }

        if (windowWidth == 0 || windowHeight == 0)
        {
            if (cursorX == windowLeft && cursorY == windowTop)
                return Win32Constants.HTCLIENT;
            return Win32Constants.HTNOWHERE;
        }

        // Check if cursor is outside window bounds
        if (cursorX < windowLeft || cursorX > windowLeft + windowWidth ||
            cursorY < windowTop || cursorY > windowTop + windowHeight)
        {
            return Win32Constants.HTNOWHERE;
        }

        double relX = cursorX - windowLeft;
        double relY = cursorY - windowTop;

        int effBorder = Math.Max(0, borderThickness);

        bool onLeft = relX < effBorder;
        bool onRight = relX >= windowWidth - effBorder;
        bool onTop = relY < effBorder;
        bool onBottom = relY >= windowHeight - effBorder;

        // 4 Corners
        if (onTop && onLeft) return Win32Constants.HTTOPLEFT;
        if (onTop && onRight) return Win32Constants.HTTOPRIGHT;
        if (onBottom && onLeft) return Win32Constants.HTBOTTOMLEFT;
        if (onBottom && onRight) return Win32Constants.HTBOTTOMRIGHT;

        // 4 Edges
        if (onTop) return Win32Constants.HTTOP;
        if (onBottom) return Win32Constants.HTBOTTOM;
        if (onLeft) return Win32Constants.HTLEFT;
        if (onRight) return Win32Constants.HTRIGHT;

        // Interior: client area (allows dragging / control bar interaction)
        return Win32Constants.HTCLIENT;
    }

    /// <summary>
    /// Applies bitwise extended style enabling click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED).
    /// </summary>
    public static int EnableClickThrough(int currentExStyle)
    {
        return currentExStyle | Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED;
    }

    /// <summary>
    /// Applies bitwise extended style enabling click-through for 64-bit style pointers.
    /// </summary>
    public static long EnableClickThroughLong(long currentExStyle)
    {
        return currentExStyle | Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED;
    }

    /// <summary>
    /// Applies bitwise extended style disabling click-through (removes WS_EX_TRANSPARENT).
    /// </summary>
    public static int DisableClickThrough(int currentExStyle)
    {
        return currentExStyle & ~Win32Constants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Applies bitwise extended style disabling click-through for 64-bit style pointers.
    /// </summary>
    public static long DisableClickThroughLong(long currentExStyle)
    {
        return currentExStyle & ~Win32Constants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Checks if the style has WS_EX_TRANSPARENT active.
    /// </summary>
    public static bool IsClickThrough(int currentExStyle)
    {
        return (currentExStyle & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Checks if the 64-bit style has WS_EX_TRANSPARENT active.
    /// </summary>
    public static bool IsClickThroughLong(long currentExStyle)
    {
        return (currentExStyle & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT;
    }

    /// <summary>
    /// Clamps requested window dimension to MINMAXINFO constraints.
    /// </summary>
    public static (double Width, double Height) ClampToMinDimensions(double width, double height, double minW = MinOverlayWidth, double minH = MinOverlayHeight)
    {
        double clampedW = Math.Max(minW, width);
        double clampedH = Math.Max(minH, height);
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
    /// Normalizes opacity mode integer strictly into [0, 2] even with negative or extreme integer values.
    /// 0: Dark slate (#D00F172A), 1: Frosted slate (#600F172A), 2: Pure transparent (#00000000).
    /// </summary>
    public static int NormalizeOpacityMode(int mode)
    {
        int normalized = mode % 3;
        return normalized < 0 ? normalized + 3 : normalized;
    }

    /// <summary>
    /// Cycles opacity mode index strictly through 0 -> 1 -> 2 -> 0.
    /// </summary>
    public static int GetNextOpacityMode(int currentMode)
    {
        return (NormalizeOpacityMode(currentMode) + 1) % 3;
    }

    /// <summary>
    /// Returns exact Hex RGBA colors for background container and border for specified mode.
    /// </summary>
    public static (string BackgroundHex, string BorderHex) GetOpacityModeColors(int mode)
    {
        return NormalizeOpacityMode(mode) switch
        {
            0 => (Win32Constants.DarkSlateBackground, Win32Constants.DarkSlateBorder),
            1 => (Win32Constants.FrostedSlateBackground, Win32Constants.FrostedSlateBorder),
            _ => (Win32Constants.TransparentBackground, Win32Constants.TransparentBorder)
        };
    }

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
    /// Clamps font size within valid bounds [min, max], safely handling NaN and Infinity.
    /// </summary>
    public static double ClampFontSize(double size, double min = MinFontSize, double max = MaxFontSize)
    {
        if (double.IsNaN(size)) return min;
        if (double.IsPositiveInfinity(size)) return max;
        if (double.IsNegativeInfinity(size)) return min;
        return Math.Clamp(size, min, max);
    }

    /// <summary>
    /// Adjusts font size by delta and clamps to [min, max], safely handling NaN and Infinity.
    /// Invariant: returned font size is always finite and strictly within [min, max].
    /// </summary>
    public static double AdjustFontSize(double current, double delta, double min = MinFontSize, double max = MaxFontSize)
    {
        if (double.IsNaN(current)) current = min;
        if (double.IsPositiveInfinity(current)) current = max;
        if (double.IsNegativeInfinity(current)) current = min;

        if (double.IsNaN(delta)) return Math.Clamp(current, min, max);
        if (double.IsPositiveInfinity(delta)) return max;
        if (double.IsNegativeInfinity(delta)) return min;

        double sum = current + delta;
        if (double.IsNaN(sum)) return Math.Clamp(current, min, max);
        return Math.Clamp(sum, min, max);
    }

    /// <summary>
    /// Resolves backdrop type considering high-contrast accessibility mode and valid range [0, 4].
    /// </summary>
    public static int ResolveBackdropType(int type, bool highContrast = false) =>
        highContrast ? Win32Constants.DWMSBT_NONE : (type is >= 0 and <= 4 ? type : Win32Constants.DWMSBT_AUTO);

    /// <summary>
    /// Clamps corner preference to valid Win32 range [0, 3].
    /// </summary>
    public static int ClampCornerPreference(int pref) => Math.Clamp(pref, 0, 3);

    #endregion
}
