using System;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Win32 and DWM API constants for Windows 11 Fluent 2 overlay, hotkeys, and glassmorphism.
/// </summary>
public static class Win32InteropConstants
{
    // Window Messages
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_DISPLAYCHANGE = 0x007E;

    // Hit-Test result codes
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

    // Window Extended Styles (WS_EX_*)
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // DWM Window Attributes
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // DWM Corner Preferences
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // DWM System Backdrop Types
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;       // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

    // Hotkey Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual Key Codes
    public const uint VK_F8 = 0x77;

    // Win32 Error Codes
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
}

/// <summary>
/// Fluent 2 theme brush definitions and palette mappings.
/// </summary>
public static class Fluent2ThemeBrushes
{
    public const string DarkBackground = "#202020";
    public const string LightBackground = "#F3F3F3";
    public const string AccentBlue = "#0078D4";
    public const string DarkCardBackground = "#2C2C2C";
    public const string LightCardBackground = "#FFFFFF";

    public static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> Backgrounds =
        new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dark", DarkBackground },
            { "Light", LightBackground }
        };

    public static string GetBackground(string theme) =>
        Backgrounds.TryGetValue(theme, out var color) ? color : DarkBackground;
}

/// <summary>
/// Fixture modeling global hotkey registration lifecycle.
/// </summary>
public class HotkeyManagerFixture
{
    public bool IsRegistered { get; private set; }
    public uint RegisteredModifier { get; private set; }
    public uint RegisteredVk { get; private set; }

    public bool Register(uint modifier, uint vk)
    {
        RegisteredModifier = modifier;
        RegisteredVk = vk;
        IsRegistered = true;
        return true;
    }

    public void Unregister()
    {
        IsRegistered = false;
        RegisteredModifier = 0;
        RegisteredVk = 0;
    }
}
