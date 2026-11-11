using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.App.Views;

/// <summary>
/// Desktop floating lyrics overlay window with Win32 8-direction edge resizing,
/// smooth dragging, click-through mode, global Ctrl+F8 hotkey, and high-contrast typography.
/// </summary>
public partial class FloatingOverlayWindow : Window
{
    private const int RESIZE_BORDER_WIDTH = 8;
    private const int HOTKEY_CLICK_THROUGH_ID = 9001;

    private HwndSource? _hwndSource;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isClickThrough = false;
    private int _bgAlphaMode = 0; // 0: Dark slate (#D00F172A), 1: Frosted slate (#600F172A), 2: Pure transparent (#00000000)

    public event Action<bool>? ClickThroughChanged;

    public bool IsClickThrough => _isClickThrough;
    public int BackgroundAlphaMode => _bgAlphaMode;

    public FloatingOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndMessageHook);

        // Register Global Hotkey Ctrl + F8
        NativeMethods.RegisterHotKey(
            _hwnd,
            HOTKEY_CLICK_THROUGH_ID,
            Win32Constants.MOD_CONTROL | Win32Constants.MOD_NOREPEAT,
            Win32Constants.VK_F8);

        PositionBottomCenter();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndMessageHook);
            _hwndSource = null;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_CLICK_THROUGH_ID);
            _hwnd = IntPtr.Zero;
        }

        base.OnClosed(e);
    }

    public void PositionBottomCenter()
    {
        try
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + workArea.Height - Height - 80.0;
        }
        catch
        {
            // Fallback for headless or non-desktop environments
        }
    }

    private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32Constants.WM_NCHITTEST:
            {
                if (_isClickThrough)
                {
                    // In click-through mode, let OS pass through to underlying windows
                    break;
                }

                // Signed screen coordinates (multi-monitor safe)
                short screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                short screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                {
                    int hit = NativeMethods.CalculateHitTest(
                        rect.Left,
                        rect.Top,
                        rect.Width,
                        rect.Height,
                        screenX,
                        screenY,
                        RESIZE_BORDER_WIDTH);

                    // If border or corner hit, return Win32 hit code
                    if (hit != Win32Constants.HTCLIENT && hit != Win32Constants.HTNOWHERE)
                    {
                        handled = true;
                        return (IntPtr)hit;
                    }
                }
                break;
            }

            case Win32Constants.WM_HOTKEY:
            {
                if (wParam.ToInt32() == HOTKEY_CLICK_THROUGH_ID)
                {
                    ToggleClickThrough();
                    handled = true;
                }
                break;
            }

            case Win32Constants.WM_GETMINMAXINFO:
            {
                var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.X = NativeMethods.MinOverlayWidth;
                mmi.ptMinTrackSize.Y = NativeMethods.MinOverlayHeight;
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
                break;
            }
        }

        return IntPtr.Zero;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough)
        {
            return;
        }

        // Do not initiate drag if user clicked an interactive control (e.g. Button)
        if (e.OriginalSource is DependencyObject depObj && FindVisualParent<Button>(depObj) != null)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && _hwnd != IntPtr.Zero)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(_hwnd, Win32Constants.WM_NCLBUTTONDOWN, (IntPtr)Win32Constants.HTCAPTION, IntPtr.Zero);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
            {
                return parent;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private readonly object _opacityLock = new();

    public void SetClickThrough(bool enable)
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetClickThrough(enable));
            return;
        }

        _isClickThrough = enable;

        if (_hwnd != IntPtr.Zero)
        {
            IntPtr currentExStyle = NativeMethods.GetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE);
            long exStyleLong = currentExStyle.ToInt64();

            if (enable)
            {
                exStyleLong = NativeMethods.EnableClickThroughLong(exStyleLong);
                HeaderBar.Visibility = Visibility.Collapsed;
                ClickThroughBadge.Visibility = Visibility.Visible;
            }
            else
            {
                exStyleLong = NativeMethods.DisableClickThroughLong(exStyleLong);
                HeaderBar.Visibility = Visibility.Visible;
                ClickThroughBadge.Visibility = Visibility.Collapsed;
            }

            NativeMethods.SetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE, (IntPtr)exStyleLong);
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOZORDER | Win32Constants.SWP_FRAMECHANGED);
        }
        else
        {
            if (enable)
            {
                HeaderBar.Visibility = Visibility.Collapsed;
                ClickThroughBadge.Visibility = Visibility.Visible;
            }
            else
            {
                HeaderBar.Visibility = Visibility.Visible;
                ClickThroughBadge.Visibility = Visibility.Collapsed;
            }
        }

        ClickThroughChanged?.Invoke(_isClickThrough);
    }

    public void ToggleClickThrough() => SetClickThrough(!_isClickThrough);

    public void CycleBackgroundOpacity()
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CycleBackgroundOpacity);
            return;
        }

        lock (_opacityLock)
        {
            _bgAlphaMode = NativeMethods.GetNextOpacityMode(_bgAlphaMode);
            ApplyBackgroundMode(_bgAlphaMode);
        }
    }

    public void SetBackgroundMode(int mode)
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBackgroundMode(mode));
            return;
        }

        lock (_opacityLock)
        {
            _bgAlphaMode = NativeMethods.NormalizeOpacityMode(mode);
            ApplyBackgroundMode(_bgAlphaMode);
        }
    }

    private void ApplyBackgroundMode(int mode)
    {
        int normalized = NativeMethods.NormalizeOpacityMode(mode);
        switch (normalized)
        {
            case 0: // 100% / High contrast dark slate
                ContainerBackgroundBrush.Color = Color.FromArgb(0xD0, 0x0F, 0x17, 0x2A);
                ContainerBorderBrush.Color = Color.FromArgb(0x60, 0x47, 0x55, 0x69);
                break;
            case 1: // 50% / Frosted slate
                ContainerBackgroundBrush.Color = Color.FromArgb(0x60, 0x0F, 0x17, 0x2A);
                ContainerBorderBrush.Color = Color.FromArgb(0x30, 0x47, 0x55, 0x69);
                break;
            case 2: // 0% / Pure transparent
                ContainerBackgroundBrush.Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                ContainerBorderBrush.Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                break;
        }
    }

    public void AdjustFontSize(double delta)
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AdjustFontSize(delta));
            return;
        }

        if (TargetSubtitleBlock != null)
        {
            TargetSubtitleBlock.FontSize = NativeMethods.AdjustFontSize(TargetSubtitleBlock.FontSize, delta, 14.0, 64.0);
        }
        if (SourceSubtitleBlock != null)
        {
            SourceSubtitleBlock.FontSize = NativeMethods.AdjustFontSize(SourceSubtitleBlock.FontSize, delta * 0.75, 10.0, 48.0);
        }
    }

    public void UpdateSubtitle(string target, string? source = null)
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateSubtitle(target, source));
            return;
        }

        if (TargetSubtitleBlock != null)
        {
            TargetSubtitleBlock.Text = target ?? string.Empty;
        }

        if (SourceSubtitleBlock != null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                SourceSubtitleBlock.Visibility = Visibility.Collapsed;
                SourceSubtitleBlock.Text = string.Empty;
            }
            else
            {
                SourceSubtitleBlock.Visibility = Visibility.Visible;
                SourceSubtitleBlock.Text = source;
            }
        }
    }

    public void UpdateFromTurn(TranslationTurn turn)
    {
        if (turn == null) return;
        UpdateSubtitle(turn.TargetText, turn.SourceText);
    }

    private void OnFontMinusClick(object sender, RoutedEventArgs e) => AdjustFontSize(-2.0);

    private void OnFontPlusClick(object sender, RoutedEventArgs e) => AdjustFontSize(2.0);

    private void OnToggleBackgroundClick(object sender, RoutedEventArgs e) => CycleBackgroundOpacity();

    private void OnEnableClickThroughClick(object sender, RoutedEventArgs e) => SetClickThrough(true);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
