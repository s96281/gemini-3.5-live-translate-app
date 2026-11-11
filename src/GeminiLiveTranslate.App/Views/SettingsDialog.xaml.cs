using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.App.ViewModels;

namespace GeminiLiveTranslate.App.Views;

/// <summary>
/// Windows 11 Fluent Design 2 Modal Settings Dialog for configuring DPAPI-encrypted API Key,
/// Gemini Live model, voice, auto-play TTS, paragraph hard-cut threshold, and theme.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _syncingPassword;

    public SettingsViewModel ViewModel => _viewModel;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        InitializeComponent();

        _syncingPassword = true;
        ApiKeyPasswordBox.Password = _viewModel.ApiKey;
        _syncingPassword = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.IsValidHwnd(hwnd))
        {
            ApplyFluentDialogBackdrop(hwnd);
        }
    }

    private void ApplyFluentDialogBackdrop(IntPtr hwnd)
    {
        int darkMode = string.Equals(_viewModel.SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkMode,
            sizeof(int));

        int cornerPref = Win32Constants.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPref,
            sizeof(int));

        var margins = NativeMethods.MARGINS.FullWindow;
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int backdrop = Win32Constants.DWMSBT_TRANSIENTWINDOW; // Acrylic transient backdrop
        int hr = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdrop,
            sizeof(int));

        if (hr != 0)
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x20, 0x20));
        }
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword) return;
        _viewModel.ApiKey = ApiKeyPasswordBox.Password;
    }

    private void OnToggleRevealApiKeyClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleRevealApiKey();
        if (_viewModel.IsApiKeyRevealed)
        {
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            _syncingPassword = true;
            ApiKeyPasswordBox.Password = _viewModel.ApiKey;
            _syncingPassword = false;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsApiKeyRevealed)
        {
            _viewModel.ApiKey = ApiKeyPasswordBox.Password;
        }

        _viewModel.Save();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
        DialogResult = false;
        Close();
    }
}
