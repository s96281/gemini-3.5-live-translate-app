#ifndef RUNNER_WASAPI_LOOPBACK_CAPTURE_H_
#define RUNNER_WASAPI_LOOPBACK_CAPTURE_H_

#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functional>
#include <vector>
#include <string>
#include <thread>
#include <atomic>
#include <mutex>

class WasapiLoopbackCapture {
 public:
  using AudioDataCallback = std::function<void(const std::vector<uint8_t>&)>;
  using AudioErrorCallback = std::function<void(const std::string&)>;

  WasapiLoopbackCapture();
  ~WasapiLoopbackCapture();

  bool Start(AudioDataCallback on_data, AudioErrorCallback on_error);
  void Stop();
  bool IsCapturing() const { return is_capturing_; }

 private:
  void CaptureThreadLoop();
  void ResampleAndEmit(const uint8_t* pData, UINT32 numFrames, const WAVEFORMATEX* pwfx);

  std::atomic<bool> is_capturing_{false};
  std::thread capture_thread_;

  AudioDataCallback on_data_;
  AudioErrorCallback on_error_;

  std::vector<int16_t> output_sample_buffer_;
  std::mutex mutex_;
};

#endif  // RUNNER_WASAPI_LOOPBACK_CAPTURE_H_
