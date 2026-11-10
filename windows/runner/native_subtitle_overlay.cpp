#include "native_subtitle_overlay.h"
#include <gdiplus.h>
#include <algorithm>
#include <memory>

#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "msimg32.lib")

using namespace Gdiplus;

namespace {

const wchar_t kOverlayClassName[] = L"GeminiNativeSubtitleOverlay";
const int kHotkeyIdClickThrough = 1001;

void AddRoundedRectangle(GraphicsPath& path, RectF rect, float radius) {
  float diameter = radius * 2.0f;
  if (diameter > rect.Width) diameter = rect.Width;
  if (diameter > rect.Height) diameter = rect.Height;

  path.AddArc(rect.X, rect.Y, diameter, diameter, 180.0f, 90.0f);
  path.AddArc(rect.X + rect.Width - diameter, rect.Y, diameter, diameter, 270.0f, 90.0f);
  path.AddArc(rect.X + rect.Width - diameter, rect.Y + rect.Height - diameter, diameter, diameter, 0.0f, 90.0f);
  path.AddArc(rect.X, rect.Y + rect.Height - diameter, diameter, diameter, 90.0f, 90.0f);
  path.CloseFigure();
}

}  // namespace

NativeSubtitleOverlay& NativeSubtitleOverlay::Instance() {
  static NativeSubtitleOverlay instance;
  return instance;
}

NativeSubtitleOverlay::NativeSubtitleOverlay() = default;

NativeSubtitleOverlay::~NativeSubtitleOverlay() {
  Shutdown();
}

bool NativeSubtitleOverlay::Initialize(HINSTANCE hInstance, HWND parentHwnd) {
  if (hwnd_) return true;

  h_instance_ = hInstance;
  parent_hwnd_ = parentHwnd;

  // Initialize GDI+
  GdiplusStartupInput gdiplusStartupInput;
  if (GdiplusStartup(&gdiplus_token_, &gdiplusStartupInput, nullptr) != Ok) {
    return false;
  }

  // Register Window Class
  WNDCLASSEXW wc = { sizeof(WNDCLASSEXW) };
  wc.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
  wc.lpfnWndProc = NativeSubtitleOverlay::WndProc;
  wc.cbClsExtra = 0;
  wc.cbWndExtra = 0;
  wc.hInstance = h_instance_;
  wc.hIcon = nullptr;
  wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
  wc.hbrBackground = nullptr;  // Managed entirely by UpdateLayeredWindow
  wc.lpszMenuName = nullptr;
  wc.lpszClassName = kOverlayClassName;
  wc.hIconSm = nullptr;

  RegisterClassExW(&wc);

  // Position at lower-center of primary monitor
  HMONITOR hMon = MonitorFromWindow(parentHwnd ? parentHwnd : GetDesktopWindow(), MONITOR_DEFAULTTONEAREST);
  MONITORINFO mi = { sizeof(mi) };
  if (GetMonitorInfo(hMon, &mi)) {
    pos_x_ = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - width_) / 2;
    pos_y_ = mi.rcWork.bottom - height_ - 60;
  }

  // Create Layered Popup Window
  hwnd_ = CreateWindowExW(
      WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW,
      kOverlayClassName,
      L"Gemini 3.5 Live Subtitle Overlay",
      WS_POPUP,
      pos_x_, pos_y_, width_, height_,
      parentHwnd, nullptr, h_instance_, this);

  if (!hwnd_) return false;

  SetWindowLongPtr(hwnd_, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));

  // Register Global Hotkey Ctrl + F8 to toggle click-through unlock
  RegisterHotKey(hwnd_, kHotkeyIdClickThrough, MOD_CONTROL, VK_F8);

  return true;
}

void NativeSubtitleOverlay::Shutdown() {
  if (hwnd_) {
    UnregisterHotKey(hwnd_, kHotkeyIdClickThrough);
    DestroyWindow(hwnd_);
    hwnd_ = nullptr;
  }
  if (gdiplus_token_ != 0) {
    GdiplusShutdown(gdiplus_token_);
    gdiplus_token_ = 0;
  }
  if (h_instance_) {
    UnregisterClassW(kOverlayClassName, h_instance_);
  }
}

void NativeSubtitleOverlay::Show(bool visible) {
  if (!hwnd_) return;
  is_visible_ = visible;
  if (visible) {
    SetWindowPos(hwnd_, HWND_TOPMOST, pos_x_, pos_y_, width_, height_,
                 SWP_NOACTIVATE | SWP_SHOWWINDOW);
    Render();
  } else {
    ShowWindow(hwnd_, SW_HIDE);
    if (on_closed_) {
      on_closed_();
    }
  }
}

void NativeSubtitleOverlay::UpdateSubtitle(const std::wstring& sourceText, const std::wstring& targetText) {
  source_text_ = sourceText;
  target_text_ = targetText;
  if (is_visible_) {
    Render();
  }
}

void NativeSubtitleOverlay::SetClickThrough(bool ignoreMouse) {
  if (!hwnd_) return;
  is_click_through_ = ignoreMouse;

  LONG_PTR exStyle = GetWindowLongPtr(hwnd_, GWL_EXSTYLE);
  if (ignoreMouse) {
    SetWindowLongPtr(hwnd_, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
  } else {
    SetWindowLongPtr(hwnd_, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
  }

  SetWindowPos(hwnd_, nullptr, 0, 0, 0, 0,
               SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

  if (on_click_through_changed_) {
    on_click_through_changed_(is_click_through_);
  }
  if (is_visible_) {
    Render();
  }
}

void NativeSubtitleOverlay::AdjustFontSize(int delta) {
  font_size_ = (std::max)(16, (std::min)(34, font_size_ + delta));
  if (is_visible_) {
    Render();
  }
}

void NativeSubtitleOverlay::ToggleBackground() {
  bg_alpha_mode_ = (bg_alpha_mode_ + 1) % 3;
  if (is_visible_) {
    Render();
  }
}

void NativeSubtitleOverlay::ResetToCenterBottom() {
  if (!hwnd_) return;
  HMONITOR hMon = MonitorFromWindow(hwnd_, MONITOR_DEFAULTTONEAREST);
  MONITORINFO mi = { sizeof(mi) };
  if (GetMonitorInfo(hMon, &mi)) {
    pos_x_ = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - width_) / 2;
    pos_y_ = mi.rcWork.bottom - height_ - 60;
    SetWindowPos(hwnd_, HWND_TOPMOST, pos_x_, pos_y_, width_, height_,
                 SWP_NOACTIVATE | SWP_SHOWWINDOW);
    Render();
  }
}

LRESULT CALLBACK NativeSubtitleOverlay::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
  NativeSubtitleOverlay* self = reinterpret_cast<NativeSubtitleOverlay*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));

  switch (msg) {
    case WM_NCCREATE: {
      CREATESTRUCT* cs = reinterpret_cast<CREATESTRUCT*>(lParam);
      SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
      return TRUE;
    }

    case WM_HOTKEY: {
      if (self && wParam == kHotkeyIdClickThrough) {
        self->SetClickThrough(!self->is_click_through_);
      }
      return 0;
    }

    case WM_WINDOWPOSCHANGED: {
      WINDOWPOS* wp = reinterpret_cast<WINDOWPOS*>(lParam);
      if (self && !(wp->flags & SWP_NOMOVE)) {
        self->pos_x_ = wp->x;
        self->pos_y_ = wp->y;
      }
      return 0;
    }

    case WM_LBUTTONDOWN: {
      if (!self) break;
      POINT pt = { LOWORD(lParam), HIWORD(lParam) };

      // Button Click Checks
      if (PtInRect(&self->close_btn_rect_, pt)) {
        self->Show(false);
        return 0;
      }
      if (PtInRect(&self->lock_btn_rect_, pt)) {
        self->SetClickThrough(true);
        return 0;
      }
      if (PtInRect(&self->font_plus_btn_rect_, pt)) {
        self->AdjustFontSize(2);
        return 0;
      }
      if (PtInRect(&self->font_minus_btn_rect_, pt)) {
        self->AdjustFontSize(-2);
        return 0;
      }
      if (PtInRect(&self->bg_toggle_btn_rect_, pt)) {
        self->ToggleBackground();
        return 0;
      }

      // Smooth Win32 Native Dragging anywhere on the overlay
      ReleaseCapture();
      SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
      return 0;
    }

    case WM_RBUTTONUP: {
      if (!self) break;
      POINT ptScreen;
      GetCursorPos(&ptScreen);

      HMENU hMenu = CreatePopupMenu();
      AppendMenuW(hMenu, MF_STRING, 201, L"鎖定並開啟滑鼠穿透 (Ctrl+F8)");
      AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);
      AppendMenuW(hMenu, MF_STRING, 202, L"放大字幕字體 (A+)");
      AppendMenuW(hMenu, MF_STRING, 203, L"縮小字幕字體 (A-)");
      AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);
      AppendMenuW(hMenu, MF_STRING, 204, L"切換背景底色 (純透 / 半透 / 深色)");
      AppendMenuW(hMenu, MF_STRING, 205, L"重設位置至螢幕底部置中");
      AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);
      AppendMenuW(hMenu, MF_STRING, 206, L"關閉桌面懸浮字幕");

      SetForegroundWindow(hwnd);
      int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY, ptScreen.x, ptScreen.y, 0, hwnd, nullptr);
      DestroyMenu(hMenu);

      if (cmd == 201) self->SetClickThrough(true);
      else if (cmd == 202) self->AdjustFontSize(2);
      else if (cmd == 203) self->AdjustFontSize(-2);
      else if (cmd == 204) self->ToggleBackground();
      else if (cmd == 205) self->ResetToCenterBottom();
      else if (cmd == 206) self->Show(false);
      return 0;
    }

    case WM_DESTROY: {
      return 0;
    }

    default:
      break;
  }

  return DefWindowProc(hwnd, msg, wParam, lParam);
}

void NativeSubtitleOverlay::Render() {
  if (!hwnd_) return;

  Bitmap bitmap(width_, height_, PixelFormat32bppPARGB);
  Graphics g(&bitmap);

  g.SetSmoothingMode(SmoothingModeAntiAlias);
  g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
  g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);

  // 1. Clear with completely transparent black
  g.Clear(Color(0, 0, 0, 0));

  // 2. Rounded Translucent Pill Background
  int alpha = 0;
  if (bg_alpha_mode_ == 1) alpha = 140;      // Frosted slate
  else if (bg_alpha_mode_ == 2) alpha = 210; // High contrast dark slate

  if (alpha > 0) {
    GraphicsPath bgPath;
    RectF rect(4.0f, 4.0f, static_cast<REAL>(width_ - 8), static_cast<REAL>(height_ - 8));
    AddRoundedRectangle(bgPath, rect, 20.0f);

    BYTE bAlpha = static_cast<BYTE>(alpha);
    BYTE bBorder = static_cast<BYTE>((std::min)(255, alpha + 50));

    SolidBrush bgBrush(Color(bAlpha, 15, 23, 42)); // #0F172A slate
    g.FillPath(&bgBrush, &bgPath);

    Pen borderPen(Color(bBorder, 71, 85, 105), 1.0f);
    g.DrawPath(&borderPen, &bgPath);
  }

  // 3. Fonts
  std::unique_ptr<FontFamily> fontFamily = std::make_unique<FontFamily>(L"Microsoft JhengHei");
  if (fontFamily->GetLastStatus() != Ok) {
    fontFamily = std::make_unique<FontFamily>(L"Microsoft YaHei");
  }
  if (fontFamily->GetLastStatus() != Ok) {
    fontFamily = std::make_unique<FontFamily>(L"Segoe UI");
  }
  if (fontFamily->GetLastStatus() != Ok) {
    const FontFamily* generic = FontFamily::GenericSansSerif();
    if (generic) {
      fontFamily = std::unique_ptr<FontFamily>(generic->Clone());
    }
  }

  // 4. Header Bar (Status badge & Buttons)
  if (!is_click_through_) {
    // Top-Left LIVE Badge
    Gdiplus::Font badgeFont(fontFamily.get(), 9.0f, FontStyleBold, UnitPoint);
    SolidBrush badgeBg(Color(180, 2, 132, 199));
    RectF badgeRect(22.0f, 10.0f, 85.0f, 20.0f);
    GraphicsPath badgePath;
    AddRoundedRectangle(badgePath, badgeRect, 6.0f);
    g.FillPath(&badgeBg, &badgePath);

    StringFormat centerFormat;
    centerFormat.SetAlignment(StringAlignmentCenter);
    centerFormat.SetLineAlignment(StringAlignmentCenter);
    SolidBrush badgeText(Color(255, 255, 255, 255));
    g.DrawString(L"● 懸浮字幕", -1, &badgeFont, badgeRect, &centerFormat, &badgeText);

    // Top-Right Interactive Buttons
    Gdiplus::Font btnFont(fontFamily.get(), 9.0f, FontStyleBold, UnitPoint);
    SolidBrush btnBg(Color(160, 30, 41, 59));
    SolidBrush btnText(Color(240, 255, 255, 255));
    SolidBrush cyanText(Color(255, 0, 229, 255));

    int rightX = width_ - 24;

    // Close Button [✕]
    close_btn_rect_ = { rightX - 28, 8, rightX, 32 };
    RectF closeRect(static_cast<REAL>(close_btn_rect_.left), static_cast<REAL>(close_btn_rect_.top), 28.0f, 24.0f);
    GraphicsPath closePath;
    AddRoundedRectangle(closePath, closeRect, 6.0f);
    SolidBrush closeBg(Color(180, 239, 68, 68)); // red-500
    g.FillPath(&closeBg, &closePath);
    g.DrawString(L"✕", -1, &btnFont, closeRect, &centerFormat, &badgeText);

    rightX -= (28 + 8);

    // Lock/Click-Through Button [穿透]
    lock_btn_rect_ = { rightX - 46, 8, rightX, 32 };
    RectF lockRect(static_cast<REAL>(lock_btn_rect_.left), static_cast<REAL>(lock_btn_rect_.top), 46.0f, 24.0f);
    GraphicsPath lockPath;
    AddRoundedRectangle(lockPath, lockRect, 6.0f);
    g.FillPath(&btnBg, &lockPath);
    g.DrawString(L"穿透", -1, &btnFont, lockRect, &centerFormat, &cyanText);

    rightX -= (46 + 8);

    // Background toggle button [背景]
    bg_toggle_btn_rect_ = { rightX - 46, 8, rightX, 32 };
    RectF bgBtnRect(static_cast<REAL>(bg_toggle_btn_rect_.left), static_cast<REAL>(bg_toggle_btn_rect_.top), 46.0f, 24.0f);
    GraphicsPath bgBtnPath;
    AddRoundedRectangle(bgBtnPath, bgBtnRect, 6.0f);
    g.FillPath(&btnBg, &bgBtnPath);
    g.DrawString(L"背景", -1, &btnFont, bgBtnRect, &centerFormat, &btnText);

    rightX -= (46 + 8);

    // Font Plus [A+]
    font_plus_btn_rect_ = { rightX - 32, 8, rightX, 32 };
    RectF plusRect(static_cast<REAL>(font_plus_btn_rect_.left), static_cast<REAL>(font_plus_btn_rect_.top), 32.0f, 24.0f);
    GraphicsPath plusPath;
    AddRoundedRectangle(plusPath, plusRect, 6.0f);
    g.FillPath(&btnBg, &plusPath);
    g.DrawString(L"A+", -1, &btnFont, plusRect, &centerFormat, &btnText);

    rightX -= (32 + 6);

    // Font Minus [A-]
    font_minus_btn_rect_ = { rightX - 32, 8, rightX, 32 };
    RectF minusRect(static_cast<REAL>(font_minus_btn_rect_.left), static_cast<REAL>(font_minus_btn_rect_.top), 32.0f, 24.0f);
    GraphicsPath minusPath;
    AddRoundedRectangle(minusPath, minusRect, 6.0f);
    g.FillPath(&btnBg, &minusPath);
    g.DrawString(L"A-", -1, &btnFont, minusRect, &centerFormat, &btnText);
  } else {
    // In Click-Through Mode: Clear button rects and show small subtle unlock guide
    close_btn_rect_ = { 0, 0, 0, 0 };
    lock_btn_rect_ = { 0, 0, 0, 0 };
    font_plus_btn_rect_ = { 0, 0, 0, 0 };
    font_minus_btn_rect_ = { 0, 0, 0, 0 };
    bg_toggle_btn_rect_ = { 0, 0, 0, 0 };

    Gdiplus::Font hintFont(fontFamily.get(), 9.0f, FontStyleRegular, UnitPoint);
    StringFormat rightFormat;
    rightFormat.SetAlignment(StringAlignmentFar);
    rightFormat.SetLineAlignment(StringAlignmentCenter);
    RectF hintRect(static_cast<REAL>(width_ - 260), 10.0f, 230.0f, 20.0f);
    SolidBrush hintBrush(Color(200, 0, 229, 255));
    g.DrawString(L"已穿透滑鼠 · 按 Ctrl+F8 解鎖", -1, &hintFont, hintRect, &rightFormat, &hintBrush);
  }

  // 5. Subtitle Text Rendering
  StringFormat textFormat;
  textFormat.SetAlignment(StringAlignmentCenter);
  textFormat.SetLineAlignment(StringAlignmentCenter);
  textFormat.SetTrimming(StringTrimmingWord);

  bool hasTarget = !target_text_.empty();
  bool hasSource = !source_text_.empty();

  // Target Text (Primary translation)
  if (hasTarget) {
    float effFontSize = static_cast<float>(font_size_);
    if (target_text_.length() > 70) {
      effFontSize = (std::max)(15.0f, effFontSize * 0.75f);
    } else if (target_text_.length() > 45) {
      effFontSize = (std::max)(17.0f, effFontSize * 0.85f);
    }

    REAL targetY = hasSource ? 35.0f : 40.0f;
    REAL targetHeight = hasSource ? 68.0f : 105.0f;
    RectF targetRect(24.0f, targetY, static_cast<REAL>(width_ - 48), targetHeight);
    GraphicsPath targetPath;
    targetPath.AddString(
        target_text_.c_str(), -1, fontFamily.get(),
        FontStyleBold, effFontSize,
        targetRect, &textFormat);

    // Black outer stroke (4.5px thick, round join)
    Pen targetStroke(Color(255, 0, 0, 0), 4.5f);
    targetStroke.SetLineJoin(LineJoinRound);
    g.DrawPath(&targetStroke, &targetPath);

    // Neon Cyan fill
    SolidBrush targetFill(Color(255, 0, 229, 255)); // #00E5FF
    g.FillPath(&targetFill, &targetPath);
  }

  // Source Text (Original speech)
  if (hasSource) {
    float effSrcFontSize = static_cast<float>(font_size_ * 0.68f);
    if (source_text_.length() > 70) {
      effSrcFontSize = (std::max)(12.0f, effSrcFontSize * 0.78f);
    }

    REAL srcY = hasTarget ? 104.0f : 55.0f;
    REAL srcHeight = hasTarget ? 46.0f : 85.0f;
    RectF sourceRect(24.0f, srcY, static_cast<REAL>(width_ - 48), srcHeight);
    GraphicsPath sourcePath;
    sourcePath.AddString(
        source_text_.c_str(), -1, fontFamily.get(),
        FontStyleRegular, effSrcFontSize,
        sourceRect, &textFormat);

    Pen sourceStroke(Color(240, 0, 0, 0), 3.0f);
    sourceStroke.SetLineJoin(LineJoinRound);
    g.DrawPath(&sourceStroke, &sourcePath);

    SolidBrush sourceFill(Color(240, 241, 245, 249)); // Clean bright slate
    g.FillPath(&sourceFill, &sourcePath);
  }

  // 6. Update Layered Window with Pre-multiplied ARGB
  HBITMAP hBmp = nullptr;
  bitmap.GetHBITMAP(Color(0, 0, 0, 0), &hBmp);
  if (!hBmp) return;

  HDC hScreenDC = GetDC(nullptr);
  HDC hMemDC = CreateCompatibleDC(hScreenDC);
  HGDIOBJ hOld = SelectObject(hMemDC, hBmp);

  POINT ptSrc = { 0, 0 };
  SIZE sizeWnd = { width_, height_ };
  POINT ptDst = { pos_x_, pos_y_ };
  BLENDFUNCTION bf = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };

  UpdateLayeredWindow(hwnd_, hScreenDC, &ptDst, &sizeWnd, hMemDC, &ptSrc, 0, &bf, ULW_ALPHA);

  SelectObject(hMemDC, hOld);
  DeleteObject(hBmp);
  DeleteDC(hMemDC);
  ReleaseDC(nullptr, hScreenDC);
}
