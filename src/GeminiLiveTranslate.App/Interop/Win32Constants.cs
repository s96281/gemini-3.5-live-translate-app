using System;

namespace GeminiLiveTranslate.App.Interop;

/// <summary>
/// Authoritative Win32 and DWM API constants for Windows 11 Fluent 2 overlay,
/// mouse click-through, global hotkeys, and border resizing.
/// </summary>
public static class Win32Constants
{
    #region Window Messages

    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_HOTKEY = 0x0312;

    #endregion

    #region Hit-Test Result Codes

    public const int HTERROR = -2;
    public const int HTTRANSPARENT = -1;
    public const int HTNOWHERE = 0;
    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    #endregion

    #region Window Long Indices & Extended Styles

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    #endregion

    #region SetWindowPos Flags

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    #endregion

    #region Hotkey Modifiers & Virtual Keys

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint VK_F8 = 0x77;

    public const int HOTKEY_CLICK_THROUGH_ID = 9001;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    #endregion

    #region DWM Window Attributes

    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    #endregion

    #region DWM Corner Preferences

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    #endregion

    #region DWM System Backdrop Types

    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;       // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

    #endregion

    #region Typography & Overlay Defaults

    public const string DefaultCompositeFontFamily = "Segoe UI Emoji, Microsoft JhengHei, Microsoft YaHei, Segoe UI, sans-serif";

    public const double DefaultFontSize = 24.0;
    public const double MinFontSize = 14.0;
    public const double MaxFontSize = 64.0;
    public const double DefaultStrokeThickness = 4.5;

    public const int DefaultOverlayWidth = 960;
    public const int DefaultOverlayHeight = 170;
    public const int MinOverlayWidth = 360;
    public const int MinOverlayHeight = 100;

    public const string TargetSubtitleNeonCyan = "#00E5FF";
    public const string SourceSubtitleCleanSlate = "#F1F5F9";
    public const string DarkSlateBackground = "#D00F172A";
    public const string FrostedSlateBackground = "#600F172A";
    public const string TransparentBackground = "#00000000";
    public const string DarkSlateBorder = "#60475569";
    public const string FrostedSlateBorder = "#30475569";
    public const string TransparentBorder = "#00000000";

    #endregion
}
