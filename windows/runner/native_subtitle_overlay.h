#ifndef NATIVE_SUBTITLE_OVERLAY_H_
#define NATIVE_SUBTITLE_OVERLAY_H_

#include <windows.h>
#include <string>
#include <functional>

/// Pure Native Win32 GDI+ Layered Window Subtitle Overlay
/// Provides 100% crash-free, DWM per-pixel transparent, smooth draggable,
/// always-on-top desktop lyrics overlay with zero Flutter engine overhead.
class NativeSubtitleOverlay {
 public:
  static NativeSubtitleOverlay& Instance();

  bool Initialize(HINSTANCE hInstance, HWND parentHwnd);
  void Shutdown();

  void Show(bool visible);
  bool IsVisible() const { return is_visible_; }

  void UpdateSubtitle(const std::wstring& sourceText, const std::wstring& targetText);
  void SetClickThrough(bool ignoreMouse);
  bool IsClickThrough() const { return is_click_through_; }

  void AdjustFontSize(int delta);
  void ToggleBackground();
  void ResetToCenterBottom();

  void SetOnClosedCallback(std::function<void()> callback) {
    on_closed_ = callback;
  }
  void SetOnClickThroughChangedCallback(std::function<void(bool)> callback) {
    on_click_through_changed_ = callback;
  }

 private:
  NativeSubtitleOverlay();
  ~NativeSubtitleOverlay();

  static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
  void Render();

  HWND hwnd_ = nullptr;
  HWND parent_hwnd_ = nullptr;
  HINSTANCE h_instance_ = nullptr;
  ULONG_PTR gdiplus_token_ = 0;

  bool is_visible_ = false;
  bool is_click_through_ = false;
  int width_ = 960;
  int height_ = 160;
  int pos_x_ = 100;
  int pos_y_ = 100;
  int font_size_ = 23;      // Target font size in pt
  int bg_alpha_mode_ = 1;   // 0: Pure transparent (0), 1: Frosted slate (140), 2: Dark slate (210)

  std::wstring source_text_;
  std::wstring target_text_;

  // Interactive Button Rectangles (relative to window top-left)
  RECT close_btn_rect_ = { 0, 0, 0, 0 };
  RECT lock_btn_rect_ = { 0, 0, 0, 0 };
  RECT font_plus_btn_rect_ = { 0, 0, 0, 0 };
  RECT font_minus_btn_rect_ = { 0, 0, 0, 0 };
  RECT bg_toggle_btn_rect_ = { 0, 0, 0, 0 };

  std::function<void()> on_closed_;
  std::function<void(bool)> on_click_through_changed_;
};

#endif  // NATIVE_SUBTITLE_OVERLAY_H_
