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
                if (event_sink_) {
                  event_sink_->Success(flutter::EncodableValue(data));
                }
              },
              [this](const std::string& err) {
                if (event_sink_) {
                  event_sink_->Error("WASAPI_ERROR", err);
                }
              });
          result->Success(flutter::EncodableValue(started));
        } else if (call.method_name() == "stopCapture") {
          wasapi_capture_->Stop();
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

void FlutterWindow::OnDestroy() {
  if (wasapi_capture_) {
    wasapi_capture_->Stop();
    wasapi_capture_ = nullptr;
  }
  event_sink_ = nullptr;
  method_channel_ = nullptr;
  event_channel_ = nullptr;

  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
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
