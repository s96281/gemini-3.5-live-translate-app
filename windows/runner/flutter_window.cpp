#include "flutter_window.h"

#include <optional>
#include <flutter/event_stream_handler_functions.h>

#include "flutter/generated_plugin_registrant.h"

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

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

  window_channel_->SetMethodCallHandler(
      [this](const flutter::MethodCall<flutter::EncodableValue>& call,
             std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>> result) {
        HWND hwnd = GetHandle();
        if (!hwnd) {
          result->Success(flutter::EncodableValue(false));
          return;
        }

        if (call.method_name() == "enterMiniOverlay") {
          // 記住原本視窗座標與尺寸
          RECT rect;
          GetWindowRect(hwnd, &rect);
          if (rect.right - rect.left > 250 && rect.bottom - rect.top > 250) {
            normal_rect_ = rect;
          }

          int mini_width = 520;
          int mini_height = 160;
          if (call.arguments()) {
            if (const auto* args = std::get_if<flutter::EncodableMap>(call.arguments())) {
              auto w_it = args->find(flutter::EncodableValue("width"));
              if (w_it != args->end()) mini_width = std::get<int>(w_it->second);
              auto h_it = args->find(flutter::EncodableValue("height"));
              if (h_it != args->end()) mini_height = std::get<int>(h_it->second);
            }
          }

          // 置頂並縮小至懸浮條尺寸
          SetWindowPos(hwnd, HWND_TOPMOST, rect.left, rect.top, mini_width, mini_height, SWP_SHOWWINDOW);
          result->Success(flutter::EncodableValue(true));
        } else if (call.method_name() == "exitMiniOverlay") {
          int w = normal_rect_.right - normal_rect_.left;
          int h = normal_rect_.bottom - normal_rect_.top;
          if (w < 300 || h < 400) {
            w = 420;
            h = 750;
          }
          int x = normal_rect_.left;
          int y = normal_rect_.top;
          // 取消置頂並恢復主視窗尺寸
          SetWindowPos(hwnd, HWND_NOTOPMOST, x, y, w, h, SWP_SHOWWINDOW);
          result->Success(flutter::EncodableValue(true));
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
        } else {
          result->NotImplemented();
        }
      });
}

void FlutterWindow::OnDestroy() {
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
