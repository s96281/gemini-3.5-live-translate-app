using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.App.ViewModels;

/// <summary>
/// ViewModel for the Desktop Floating Lyrics Overlay Window (FloatingOverlayWindow).
/// Manages subtitle text state, font scaling, background opacity cycling, and click-through state.
/// </summary>
public class OverlayViewModel : INotifyPropertyChanged
{
    public const double MinOverlayFontSize = 12.0;
    public const double MaxOverlayFontSize = 72.0;
    public const double DefaultOverlayFontSize = 24.0;
    public const double DefaultFontSizeStep = 2.0;

    private string _targetText = "即時口譯字幕準備就緒";
    private string _sourceText = "Original voice transcription will appear here...";
    private double _fontSize = DefaultOverlayFontSize;
    private double _opacity = 1.0;
    private int _backgroundAlphaMode = 1; // 0: Dark slate, 1: Frosted slate, 2: Pure transparent
    private bool _isClickThrough;
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool>? ClickThroughChanged;
    public event Action<bool>? VisibilityChanged;

    public string TargetText
    {
        get => _targetText;
        set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_targetText, normalized, StringComparison.Ordinal))
            {
                _targetText = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_sourceText, normalized, StringComparison.Ordinal))
            {
                _sourceText = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSourceText));
            }
        }
    }

    public bool HasSourceText => !string.IsNullOrWhiteSpace(_sourceText);

    public double FontSize
    {
        get => _fontSize;
        set
        {
            double clamped = NativeMethods.ClampFontSize(value, MinOverlayFontSize, MaxOverlayFontSize);
            if (Math.Abs(_fontSize - clamped) > 0.001)
            {
                _fontSize = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SourceFontSize));
            }
        }
    }

    public double SourceFontSize => Math.Clamp(_fontSize * 0.65, 10.0, 48.0);

    public double Opacity
    {
        get => _opacity;
        set
        {
            double sanitized = double.IsNaN(value) || double.IsInfinity(value) ? 1.0 : Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_opacity - sanitized) > 0.001)
            {
                _opacity = sanitized;
                OnPropertyChanged();
            }
        }
    }

    public int BackgroundAlphaMode
    {
        get => _backgroundAlphaMode;
        set
        {
            int normalized = NativeMethods.NormalizeOpacityMode(value);
            if (_backgroundAlphaMode != normalized)
            {
                _backgroundAlphaMode = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundHex));
                OnPropertyChanged(nameof(BorderHex));
            }
        }
    }

    public string BackgroundHex => NativeMethods.GetOpacityModeColors(_backgroundAlphaMode).BackgroundHex;
    public string BorderHex => NativeMethods.GetOpacityModeColors(_backgroundAlphaMode).BorderHex;

    public bool IsClickThrough
    {
        get => _isClickThrough;
        set
        {
            if (_isClickThrough != value)
            {
                _isClickThrough = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadgeText));
                ClickThroughChanged?.Invoke(_isClickThrough);
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
                VisibilityChanged?.Invoke(_isVisible);
            }
        }
    }

    public string StatusBadgeText => IsClickThrough
        ? "已穿透滑鼠 · 按 Ctrl+F8 解鎖"
        : "桌面懸浮字幕";

    public ICommand ToggleVisibilityCommand { get; }
    public ICommand ToggleClickThroughCommand { get; }
    public ICommand CycleBackgroundOpacityCommand { get; }
    public ICommand IncreaseFontSizeCommand { get; }
    public ICommand DecreaseFontSizeCommand { get; }
    public ICommand CloseCommand { get; }

    public OverlayViewModel()
    {
        ToggleVisibilityCommand = new RelayCommand(ToggleVisibility);
        ToggleClickThroughCommand = new RelayCommand(ToggleClickThrough);
        CycleBackgroundOpacityCommand = new RelayCommand(CycleBackgroundOpacity);
        IncreaseFontSizeCommand = new RelayCommand(() => IncreaseFontSize());
        DecreaseFontSizeCommand = new RelayCommand(() => DecreaseFontSize());
        CloseCommand = new RelayCommand(Close);
    }

    public void UpdateSubtitle(string target, string? source = null)
    {
        TargetText = target ?? string.Empty;
        if (source != null)
        {
            SourceText = source;
        }
    }

    public void UpdateFromTurn(TranslationTurn? turn)
    {
        if (turn == null) return;
        UpdateSubtitle(turn.TargetText, turn.SourceText);
    }

    public void IncreaseFontSize(double delta = DefaultFontSizeStep)
    {
        double step = double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0 ? DefaultFontSizeStep : delta;
        FontSize = NativeMethods.AdjustFontSize(_fontSize, step, MinOverlayFontSize, MaxOverlayFontSize);
    }

    public void DecreaseFontSize(double delta = DefaultFontSizeStep)
    {
        double step = double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0 ? DefaultFontSizeStep : delta;
        FontSize = NativeMethods.AdjustFontSize(_fontSize, -step, MinOverlayFontSize, MaxOverlayFontSize);
    }

    public void CycleBackgroundOpacity()
    {
        Opacity = NativeMethods.GetNextOpacity(_opacity);
        BackgroundAlphaMode = NativeMethods.GetNextOpacityMode(_backgroundAlphaMode);
    }

    public void SetClickThrough(bool enable)
    {
        IsClickThrough = enable;
    }

    public void ToggleClickThrough()
    {
        IsClickThrough = !IsClickThrough;
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void Close()
    {
        IsVisible = false;
    }

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    public void ApplySettings(AppSettings? settings)
    {
        if (settings == null) return;
        FontSize = settings.OverlayFontSize;
        Opacity = settings.OverlayOpacity;
        IsClickThrough = settings.OverlayClickThrough;
    }

    public void WriteToSettings(AppSettings? settings)
    {
        if (settings == null) return;
        settings.OverlayFontSize = FontSize;
        settings.OverlayOpacity = Opacity;
        settings.OverlayClickThrough = IsClickThrough;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
