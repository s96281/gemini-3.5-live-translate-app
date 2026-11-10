#include "flutter_window.h"

#include <optional>
#include <vector>
#include <string>
#include <dwmapi.h>
#include <flutter/event_stream_handler_functions.h>

#include "flutter/generated_plugin_registrant.h"
#include "native_subtitle_overlay.h"

namespace {

std::wstring Utf8ToUtf16(const std::string& utf8) {
  if (utf8.empty()) return std::wstring();
  int size_needed = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), nullptr, 0);
  std::wstring utf16(size_needed, 0);
  MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), &utf16[0], size_needed);
  return utf16;
}

}  // namespace

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  // Initialize Native Win32 Subtitle Overlay (100% crash-free layered window)
  NativeSubtitleOverlay::Instance().Initialize(GetModuleHandle(nullptr), GetHandle());

  RECT frame = GetClientArea();

  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  SetupSystemAudioChannels();
  SetupWindowChannels();
  SetChildContent(flutter_controller_->view()->GetNativeWindow());

  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    this->Show();
  });

  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::SetupSystemAudioChannels() {
  auto messenger = flutter_controller_->engine()->messenger();
  wasapi_capture_ = std::make_unique<WasapiLoopbackCapture>();

  method_channel_ = std::make_unique<flutter::MethodChannel<flutter::EncodableValue>>(
      messenger, "com.gemini.livetranslate/system_audio",
      &flutter::StandardMethodCodec::GetInstance());

  method_channel_->SetMethodCallHandler(
      [this](const flutter::MethodCall<flutter::EncodableValue>& call,
             std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>> result) {
        if (call.method_name() == "isSupported") {
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "startCapture") {
          bool started = wasapi_capture_->Start(
              [this](const std::vector<uint8_t>& data) {
                // 背景執行緒收到音訊：放入隊列並通知 UI 執行緒派發
                {
                  std::lock_guard<std::mutex> lock(audio_queue_mutex_);
                  audio_queue_.push(data);
                }
                HWND hwnd = GetHandle();
                if (hwnd) {
                  PostMessage(hwnd, WM_WASAPI_AUDIO_CHUNK, 0, 0);
                }
              },
              [this](const std::string& err) {
                // 錯誤處理
              });
          result->Success(flutter::EncodableValue(started));
        } else if (call.method_name() == "stopCapture") {
          wasapi_capture_->Stop();
          {
            std::lock_guard<std::mutex> lock(audio_queue_mutex_);
            while (!audio_queue_.empty()) audio_queue_.pop();
          }
          result->Success(flutter::EncodableValue(true));
        } else {
          result->NotImplemented();
        }
      });

  event_channel_ = std::make_unique<flutter::EventChannel<flutter::EncodableValue>>(
      messenger, "com.gemini.livetranslate/system_audio_stream",
      &flutter::StandardMethodCodec::GetInstance());

  auto handler = std::make_unique<flutter::StreamHandlerFunctions<flutter::EncodableValue>>(
      [this](const flutter::EncodableValue* arguments,
             std::unique_ptr<flutter::EventSink<flutter::EncodableValue>>&& events)
          -> std::unique_ptr<flutter::StreamHandlerError<flutter::EncodableValue>> {
        event_sink_ = std::move(events);
        return nullptr;
      },
      [this](const flutter::EncodableValue* arguments)
          -> std::unique_ptr<flutter::StreamHandlerError<flutter::EncodableValue>> {
        event_sink_ = nullptr;
        return nullptr;
      });

  event_channel_->SetStreamHandler(std::move(handler));
}

void FlutterWindow::FlushAudioQueueToSink() {
  std::vector<std::vector<uint8_t>> chunks;
  {
    std::lock_guard<std::mutex> lock(audio_queue_mutex_);
    while (!audio_queue_.empty()) {
      chunks.push_back(std::move(audio_queue_.front()));
      audio_queue_.pop();
    }
  }

  if (event_sink_) {
    for (const auto& chunk : chunks) {
      event_sink_->Success(flutter::EncodableValue(chunk));
    }
  }
}

void FlutterWindow::SetupWindowChannels() {
  auto messenger = flutter_controller_->engine()->messenger();
  window_channel_ = std::make_unique<flutter::MethodChannel<flutter::EncodableValue>>(
      messenger, "com.gemini.livetranslate/window",
      &flutter::StandardMethodCodec::GetInstance());

  // Setup callbacks from Native Subtitle Overlay to Flutter
  NativeSubtitleOverlay::Instance().SetOnClosedCallback([this]() {
    if (window_channel_) {
      window_channel_->InvokeMethod("onNativeOverlayClosed", nullptr);
    }
  });

  NativeSubtitleOverlay::Instance().SetOnClickThroughChangedCallback([this](bool clickThrough) {
    if (window_channel_) {
      window_channel_->InvokeMethod(
          "onNativeOverlayClickThroughChanged",
          std::make_unique<flutter::EncodableValue>(clickThrough));
    }
  });

  window_channel_->SetMethodCallHandler(
      [this](const flutter::MethodCall<flutter::EncodableValue>& call,
             std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>> result) {
        HWND hwnd = GetHandle();
        if (!hwnd) {
          result->Success(flutter::EncodableValue(false));
          return;
        }

        if (call.method_name() == "showNativeOverlay") {
          bool show = true;
          if (call.arguments()) {
            if (const auto* args = std::get_if<flutter::EncodableMap>(call.arguments())) {
              auto it = args->find(flutter::EncodableValue("show"));
              if (it != args->end() && std::holds_alternative<bool>(it->second)) {
                show = std::get<bool>(it->second);
              }
            }
          }
          NativeSubtitleOverlay::Instance().Show(show);
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "updateNativeOverlaySubtitle") {
          std::wstring source_w;
          std::wstring target_w;
          if (call.arguments()) {
            if (const auto* args = std::get_if<flutter::EncodableMap>(call.arguments())) {
              auto src_it = args->find(flutter::EncodableValue("source"));
              if (src_it != args->end() && std::holds_alternative<std::string>(src_it->second)) {
                source_w = Utf8ToUtf16(std::get<std::string>(src_it->second));
              }
              auto tgt_it = args->find(flutter::EncodableValue("target"));
              if (tgt_it != args->end() && std::holds_alternative<std::string>(tgt_it->second)) {
                target_w = Utf8ToUtf16(std::get<std::string>(tgt_it->second));
              }
            }
          }
          NativeSubtitleOverlay::Instance().UpdateSubtitle(source_w, target_w);
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "setNativeOverlayClickThrough") {
          bool ignore = false;
          if (call.arguments()) {
            if (const auto* args = std::get_if<flutter::EncodableMap>(call.arguments())) {
              auto it = args->find(flutter::EncodableValue("ignore"));
              if (it != args->end() && std::holds_alternative<bool>(it->second)) {
                ignore = std::get<bool>(it->second);
              }
            }
          }
          NativeSubtitleOverlay::Instance().SetClickThrough(ignore);
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "isNativeOverlayVisible") {
          result->Success(flutter::EncodableValue(NativeSubtitleOverlay::Instance().IsVisible()));
        } else if (call.method_name() == "setAlwaysOnTop") {
          bool on_top = false;
          if (call.arguments()) {
            if (const auto* args = std::get_if<flutter::EncodableMap>(call.arguments())) {
              auto it = args->find(flutter::EncodableValue("alwaysOnTop"));
              if (it != args->end()) on_top = std::get<bool>(it->second);
            }
          }
          SetWindowPos(hwnd, on_top ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                       SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "startDragging") {
          ReleaseCapture();
          SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
          result->Success(flutter::EncodableValue(true));
        } else {
          result->NotImplemented();
        }
      });
}

void FlutterWindow::OnDestroy() {
  NativeSubtitleOverlay::Instance().Shutdown();

  if (wasapi_capture_) {
    wasapi_capture_->Stop();
    wasapi_capture_ = nullptr;
  }
  {
    std::lock_guard<std::mutex> lock(audio_queue_mutex_);
    while (!audio_queue_.empty()) audio_queue_.pop();
  }
  event_sink_ = nullptr;
  method_channel_ = nullptr;
  event_channel_ = nullptr;
  window_channel_ = nullptr;

  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  if (message == WM_WASAPI_AUDIO_CHUNK) {
    FlushAudioQueueToSink();
    return 0;
  }

  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}
