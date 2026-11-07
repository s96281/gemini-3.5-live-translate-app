#include "wasapi_loopback_capture.h"

#include <avrt.h>
#include <algorithm>
#include <cmath>
#include <iostream>

#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "ole32.lib")

WasapiLoopbackCapture::WasapiLoopbackCapture() {}

WasapiLoopbackCapture::~WasapiLoopbackCapture() {
  Stop();
}

bool WasapiLoopbackCapture::Start(AudioDataCallback on_data, AudioErrorCallback on_error) {
  if (is_capturing_) {
    return true;
  }

  on_data_ = std::move(on_data);
  on_error_ = std::move(on_error);
  is_capturing_ = true;

  capture_thread_ = std::thread(&WasapiLoopbackCapture::CaptureThreadLoop, this);
  return true;
}

void WasapiLoopbackCapture::Stop() {
  if (!is_capturing_) {
    return;
  }

  is_capturing_ = false;
  if (capture_thread_.joinable()) {
    capture_thread_.join();
  }

  std::lock_guard<std::mutex> lock(mutex_);
  output_sample_buffer_.clear();
}

void WasapiLoopbackCapture::CaptureThreadLoop() {
  HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  bool co_initialized = SUCCEEDED(hr);

  IMMDeviceEnumerator* pEnumerator = nullptr;
  IMMDevice* pDevice = nullptr;
  IAudioClient* pAudioClient = nullptr;
  IAudioCaptureClient* pCaptureClient = nullptr;
  WAVEFORMATEX* pwfx = nullptr;

  auto cleanup = [&]() {
    if (pwfx) {
      CoTaskMemFree(pwfx);
      pwfx = nullptr;
    }
    if (pCaptureClient) {
      pCaptureClient->Release();
      pCaptureClient = nullptr;
    }
    if (pAudioClient) {
      pAudioClient->Stop();
      pAudioClient->Release();
      pAudioClient = nullptr;
    }
    if (pDevice) {
      pDevice->Release();
      pDevice = nullptr;
    }
    if (pEnumerator) {
      pEnumerator->Release();
      pEnumerator = nullptr;
    }
    if (co_initialized) {
      CoUninitialize();
    }
  };

  hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                        __uuidof(IMMDeviceEnumerator), (void**)&pEnumerator);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to create MMDeviceEnumerator");
    cleanup();
    is_capturing_ = false;
    return;
  }

  // 取得預設的播放裝置 (Render Endpoint)
  hr = pEnumerator->GetDefaultAudioEndpoint(eRender, eConsole, &pDevice);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to get default audio render endpoint");
    cleanup();
    is_capturing_ = false;
    return;
  }

  hr = pDevice->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&pAudioClient);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to activate IAudioClient");
    cleanup();
    is_capturing_ = false;
    return;
  }

  hr = pAudioClient->GetMixFormat(&pwfx);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to get mix format");
    cleanup();
    is_capturing_ = false;
    return;
  }

  // 設定 100ms 緩衝
  REFERENCE_TIME hnsBufferDuration = 1000000; // 100ms in 100-nanosecond units
  hr = pAudioClient->Initialize(AUDCLNT_SHAREMODE_SHARED,
                                AUDCLNT_STREAMFLAGS_LOOPBACK,
                                hnsBufferDuration, 0, pwfx, nullptr);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to initialize IAudioClient with AUDCLNT_STREAMFLAGS_LOOPBACK");
    cleanup();
    is_capturing_ = false;
    return;
  }

  hr = pAudioClient->GetService(__uuidof(IAudioCaptureClient), (void**)&pCaptureClient);
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to get IAudioCaptureClient");
    cleanup();
    is_capturing_ = false;
    return;
  }

  hr = pAudioClient->Start();
  if (FAILED(hr)) {
    if (on_error_) on_error_("Failed to start audio client");
    cleanup();
    is_capturing_ = false;
    return;
  }

  DWORD taskIndex = 0;
  HANDLE hTask = AvSetMmThreadCharacteristics(TEXT("Audio"), &taskIndex);

  while (is_capturing_) {
    Sleep(20);

    UINT32 packetLength = 0;
    hr = pCaptureClient->GetNextPacketSize(&packetLength);
    if (FAILED(hr)) break;

    while (packetLength > 0 && is_capturing_) {
      BYTE* pData = nullptr;
      UINT32 numFramesAvailable = 0;
      DWORD flags = 0;

      hr = pCaptureClient->GetBuffer(&pData, &numFramesAvailable, &flags, nullptr, nullptr);
      if (FAILED(hr)) break;

      if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
        pData = nullptr; // 代表目前系統播放靜音
      }

      ResampleAndEmit(pData, numFramesAvailable, pwfx);

      pCaptureClient->ReleaseBuffer(numFramesAvailable);

      hr = pCaptureClient->GetNextPacketSize(&packetLength);
      if (FAILED(hr)) break;
    }
  }

  if (hTask) {
    AvRevertMmThreadCharacteristics(hTask);
  }

  cleanup();
}

void WasapiLoopbackCapture::ResampleAndEmit(const uint8_t* pData, UINT32 numFrames, const WAVEFORMATEX* pwfx) {
  if (numFrames == 0) return;

  const int inChannels = pwfx->nChannels;
  const int inSampleRate = pwfx->nSamplesPerSec;
  const bool isFloat = (pwfx->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) ||
                       (pwfx->wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
                        reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(pwfx)->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
  const int bitsPerSample = pwfx->wBitsPerSample;

  std::vector<float> monoSourceSamples(numFrames, 0.0f);

  if (pData != nullptr) {
    if (isFloat && bitsPerSample == 32) {
      const float* pFloat = reinterpret_cast<const float*>(pData);
      for (UINT32 i = 0; i < numFrames; ++i) {
        float sum = 0.0f;
        for (int ch = 0; ch < inChannels; ++ch) {
          sum += pFloat[i * inChannels + ch];
        }
        monoSourceSamples[i] = sum / inChannels;
      }
    } else if (bitsPerSample == 16) {
      const int16_t* pShort = reinterpret_cast<const int16_t*>(pData);
      for (UINT32 i = 0; i < numFrames; ++i) {
        int32_t sum = 0;
        for (int ch = 0; ch < inChannels; ++ch) {
          sum += pShort[i * inChannels + ch];
        }
        monoSourceSamples[i] = (static_cast<float>(sum) / inChannels) / 32768.0f;
      }
    } else if (bitsPerSample == 24) {
      const uint8_t* pBytes = pData;
      for (UINT32 i = 0; i < numFrames; ++i) {
        float sum = 0.0f;
        for (int ch = 0; ch < inChannels; ++ch) {
          size_t offset = (i * inChannels + ch) * 3;
          int32_t sample24 = (pBytes[offset]) | (pBytes[offset + 1] << 8) | (pBytes[offset + 2] << 16);
          if (sample24 & 0x800000) {
            sample24 |= 0xFF000000; // Sign extend
          }
          sum += static_cast<float>(sample24) / 8388608.0f;
        }
        monoSourceSamples[i] = sum / inChannels;
      }
    } else if (bitsPerSample == 32) {
      const int32_t* pInt = reinterpret_cast<const int32_t*>(pData);
      for (UINT32 i = 0; i < numFrames; ++i) {
        double sum = 0.0;
        for (int ch = 0; ch < inChannels; ++ch) {
          sum += pInt[i * inChannels + ch];
        }
        monoSourceSamples[i] = static_cast<float>((sum / inChannels) / 2147483648.0);
      }
    }
  }

  // 線性重採樣到 16000Hz
  const double targetRate = 16000.0;
  const double ratio = targetRate / static_cast<double>(inSampleRate);
  const size_t outSampleCount = static_cast<size_t>(std::ceil(numFrames * ratio));

  std::vector<int16_t> convertedSamples;
  convertedSamples.reserve(outSampleCount);

  for (size_t i = 0; i < outSampleCount; ++i) {
    double srcIndex = i / ratio;
    size_t idx0 = static_cast<size_t>(srcIndex);
    size_t idx1 = std::min(idx0 + 1, static_cast<size_t>(numFrames - 1));
    double frac = srcIndex - idx0;

    float s0 = (idx0 < numFrames) ? monoSourceSamples[idx0] : 0.0f;
    float s1 = (idx1 < numFrames) ? monoSourceSamples[idx1] : 0.0f;
    float sampleFloat = s0 + static_cast<float>(frac * (s1 - s0));

    // 限制範圍並轉換成 16-bit signed integer
    sampleFloat = std::max(-1.0f, std::min(1.0f, sampleFloat));
    int16_t sample16 = static_cast<int16_t>(sampleFloat * 32767.0f);
    convertedSamples.push_back(sample16);
  }

  std::lock_guard<std::mutex> lock(mutex_);
  output_sample_buffer_.insert(output_sample_buffer_.end(), convertedSamples.begin(), convertedSamples.end());

  // 每累積約 1600 samples (100ms 於 16kHz) 派發一次給 Flutter
  const size_t chunk_size = 1600;
  while (output_sample_buffer_.size() >= chunk_size) {
    std::vector<uint8_t> bytes(chunk_size * sizeof(int16_t));
    std::memcpy(bytes.data(), output_sample_buffer_.data(), bytes.size());
    output_sample_buffer_.erase(output_sample_buffer_.begin(), output_sample_buffer_.begin() + chunk_size);

    if (on_data_) {
      on_data_(bytes);
    }
  }
}
