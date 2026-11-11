using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.App.ViewModels;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.App.Views;

/// <summary>
/// Windows 11 Fluent Design 2 Main Dashboard Window with DWM Mica system backdrop,
/// real-time 36-band dynamic audio spectrum waveform visualizer, virtualized bilingual
/// translation card stream, audio source quick switcher, and FloatingOverlayWindow management.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private FloatingOverlayWindow? _overlayWindow;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow()
        : this(new MainViewModel(uiDispatcher: DispatchToUi))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Overlay.PropertyChanged += OnOverlayViewModelPropertyChanged;
        _viewModel.OpenSettingsRequested += OnOpenSettingsRequested;
        _viewModel.HistoryCards.CollectionChanged += OnHistoryCardsCollectionChanged;

        ApplyThemeBrushes(_viewModel.Theme);
    }

    private static void DispatchToUi(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.IsValidHwnd(hwnd))
        {
            ApplyFluentDesignBackdrop(hwnd);
        }
    }

    /// <summary>
    /// Applies Windows 11 Fluent Design 2 DWM attributes:
    /// Immersive dark/light title bar, rounded corners (DWMWCP_ROUND), frame extension,
    /// and hardware-accelerated Mica system backdrop (DWMSBT_MAINWINDOW) with graceful fallback.
    /// </summary>
    public void ApplyFluentDesignBackdrop(IntPtr hwnd)
    {
        if (!NativeMethods.IsValidHwnd(hwnd)) return;

        // 1. Dark / Light Title Bar
        int darkMode = _viewModel.IsDarkTheme ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkMode,
            sizeof(int));

        // 2. Windows 11 Standard Rounded Corners (8-12px)
        int cornerPreference = NativeMethods.ClampCornerPreference(Win32Constants.DWMWCP_ROUND);
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPreference,
            sizeof(int));

        // 3. Extend frame into client area for DWM backdrop
        var margins = NativeMethods.MARGINS.FullWindow;
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 4. Apply Windows 11 Mica System Backdrop (DWMSBT_MAINWINDOW = 2)
        int backdrop = NativeMethods.ResolveBackdropType(
            Win32Constants.DWMSBT_MAINWINDOW,
            SystemParameters.HighContrast);
        int hr = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            Win32Constants.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdrop,
            sizeof(int));

        if (hr != 0)
        {
            ApplyThemeBrushes(_viewModel.Theme);
        }
    }

    private void ApplyThemeBrushes(string theme)
    {
        bool isLight = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        Background = new SolidColorBrush(
            isLight
                ? Color.FromRgb(0xF3, 0xF3, 0xF3)
                : Color.FromRgb(0x0A, 0x0D, 0x14));
        Foreground = new SolidColorBrush(
            isLight
                ? Color.FromRgb(0x0F, 0x17, 0x2A)
                : Color.FromRgb(0xF8, 0xFA, 0xFC));

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.IsValidHwnd(hwnd))
        {
            int darkMode = isLight ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(
                hwnd,
                Win32Constants.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkMode,
                sizeof(int));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentLevels))
        {
            var levels = _viewModel.CurrentLevels.ToArray();
            AudioSpectrumVisualizer?.UpdateSpectrum(levels);
        }
        else if (e.PropertyName == nameof(MainViewModel.Theme))
        {
            ApplyThemeBrushes(_viewModel.Theme);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsOverlayVisible))
        {
            SyncOverlayWindowVisibility(_viewModel.IsOverlayVisible);
        }
    }

    private void OnOverlayViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_overlayWindow == null) return;

        if (e.PropertyName == nameof(OverlayViewModel.TargetText) ||
            e.PropertyName == nameof(OverlayViewModel.SourceText))
        {
            _overlayWindow.UpdateSubtitle(_viewModel.Overlay.TargetText, _viewModel.Overlay.SourceText);
        }
        else if (e.PropertyName == nameof(OverlayViewModel.IsClickThrough))
        {
            if (_overlayWindow.IsClickThrough != _viewModel.Overlay.IsClickThrough)
            {
                _overlayWindow.SetClickThrough(_viewModel.Overlay.IsClickThrough);
            }
        }
        else if (e.PropertyName == nameof(OverlayViewModel.BackgroundAlphaMode))
        {
            _overlayWindow.SetBackgroundMode(_viewModel.Overlay.BackgroundAlphaMode);
        }
    }

    private void SyncOverlayWindowVisibility(bool visible)
    {
        if (visible)
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new FloatingOverlayWindow();
                _overlayWindow.ClickThroughChanged += OnOverlayWindowClickThroughChanged;
                _overlayWindow.Closed += OnOverlayWindowClosed;
                _overlayWindow.UpdateSubtitle(_viewModel.Overlay.TargetText, _viewModel.Overlay.SourceText);
                _overlayWindow.SetBackgroundMode(_viewModel.Overlay.BackgroundAlphaMode);
                if (_viewModel.Overlay.IsClickThrough)
                {
                    _overlayWindow.SetClickThrough(true);
                }
            }
            _overlayWindow.Show();
        }
        else
        {
            _overlayWindow?.Hide();
        }
    }

    private void OnOverlayWindowClickThroughChanged(bool isClickThrough)
    {
        _viewModel.Overlay.IsClickThrough = isClickThrough;
    }

    private void OnOverlayWindowClosed(object? sender, EventArgs e)
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.ClickThroughChanged -= OnOverlayWindowClickThroughChanged;
            _overlayWindow.Closed -= OnOverlayWindowClosed;
            _overlayWindow = null;
        }
        _viewModel.Overlay.IsVisible = false;
    }

    private void OnHistoryCardsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            HistoryScrollViewer?.ScrollToBottom();
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        OpenSettingsDialog();
    }

    public void OpenSettingsDialog()
    {
        var settingsVm = _viewModel.CreateSettingsViewModel();
        var dialog = new SettingsDialog(settingsVm)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnCopyTurnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TranslationTurn turn)
        {
            string formatted = _viewModel.CopyTurn(turn);
            try
            {
                if (!string.IsNullOrEmpty(formatted))
                {
                    Clipboard.SetText(formatted);
                }
            }
            catch
            {
                // Clipboard access may fail if locked by another process
            }
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Overlay.PropertyChanged -= OnOverlayViewModelPropertyChanged;
        _viewModel.OpenSettingsRequested -= OnOpenSettingsRequested;
        _viewModel.HistoryCards.CollectionChanged -= OnHistoryCardsCollectionChanged;

        if (_overlayWindow != null)
        {
            _overlayWindow.ClickThroughChanged -= OnOverlayWindowClickThroughChanged;
            _overlayWindow.Closed -= OnOverlayWindowClosed;
            try { _overlayWindow.Close(); } catch { }
            _overlayWindow = null;
        }

        try
        {
            await _viewModel.StopSessionAsync();
            await _viewModel.DisposeAsync();
        }
        catch
        {
        }

        base.OnClosed(e);
    }
}
