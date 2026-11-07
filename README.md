# Gemini 3.5 Live Translate 跨平臺 (Windows & Android) 應用程式

由 Google 最新低延遲即時口譯模型 **`gemini-3.5-live-translate-preview`** 驅動的原生跨平臺（Windows / Android）即時雙向語音翻譯應用程式。

本專案採用 **100% 原生跨平臺架構 (Flutter / Dart)**，絕非 Webview 或網頁封裝。在 Windows 端編譯為原生 Win32 C++ 執行檔，在 Android 端編譯為 ARM64 原生機器碼，直接調用作業系統底層硬體音訊介面（Windows WASAPI / Android AAudio），提供極致低延遲、高品質的口譯體驗與現代科技風視覺介面。

---

## 🌟 核心特色 (Key Features)

1. **Google AI Studio 直連**：
   - 採用 WebSocket 雙向即時串流協議 (`BidiGenerateContent`)，直連官方 `models/gemini-3.5-live-translate-preview`。
   - 使用者只需填寫個人 Google AI Studio API Key，所有通訊端到端加密傳輸，不經過任何第三方代理伺服器。
2. **硬體級低延遲音訊串流（支援裝置系統音效與麥克風）**：
   - **裝置系統播放音效 (System Audio / Loopback)**：即時擷取裝置本機播放之影片、直播、線上會議或遊戲音效，自動關閉回音消除 (AEC) 與抑噪濾波，保留原始細膩聲音細節。
   - **實體麥克風模式 (Microphone)**：現場說話口譯，啟用硬體回音消除與抑噪處理。
   - 音訊一律以 **16kHz 16-bit Mono Linear PCM** 格式擷取，切分為 100ms（3200 bytes）封包即時串流至 Gemini 模型。
   - **防迴授保護機制 (Acoustic Feedback Protection)**：切換為系統音效時自動預設靜音 AI 語音喇叭播放（純即時雙語字幕），避免喇叭播出的譯文被二次錄入引發自激回音。
   - 同步接收模型回傳之 **24kHz PCM 翻譯語音** 與雙向即時字幕。
3. **雙語即時流式字幕**：
   - 即時顯示發話端原始語音聽打 (`inputAudioTranscription`) 與譯文 (`outputAudioTranscription`)。
   - 支援自動滾動、一鍵複製剪貼簿、歷史回話瀏覽與清除。
4. **現代科技風介面 (Cyberpunk / Modern Glassmorphism)**：
   - 深邃黑底搭配霓虹青（Cyan）與電光紫（Purple）光暈。
   - 基於 GPU Canvas 加速繪製的即時音波動態視覺化波形（`AudioWaveVisualizer`）。
   - 懸浮式毛玻璃膠囊控制列，支援**一鍵快速切換「🔊 系統音效」與「🎤 麥克風」**。
5. **支援 70+ 種語言與多款 AI 語音音色**：
   - 預設目標語言：**繁體中文 (zh-TW)**，可即時切換英語、日語、韓語、德語、法語、西班牙語等 70+ 種語言。
   - 支援 5 種 Gemini Live 預設音色：`Puck` (自然平衡)、`Charon` (沉穩渾厚)、`Kore` (溫柔親切)、`Fenrir` (清晰活力)、`Aoede` (輕柔優雅)。

---

## 📁 專案架構 (Project Structure)

```text
gemini_live_translate/
├── android/                         # Android 原生專案外殼 (宣告 RECORD_AUDIO/INTERNET 權限)
│   └── app/src/main/AndroidManifest.xml
├── windows/                         # Windows 原生 Win32 / C++ 專案外殼
│   └── runner/main.cpp              # 設定原生視窗標題與渲染管線
├── lib/
│   ├── main.dart                    # 應用程式入口與全域深色主題
│   ├── config/
│   │   └── languages.dart           # 支援的 70+ 種語言與國旗清單
│   ├── models/
│   │   ├── live_config.dart         # API Key 與設定模型
│   │   └── translation_turn.dart    # 雙語字幕回話資料結構
│   ├── services/
│   │   ├── gemini_live_client.dart  # Gemini 3.5 Live WebSocket 雙向串流客戶端
│   │   ├── audio_recorder_service.dart # 原生 16kHz PCM 音訊錄製與波形 RMS 運算
│   │   └── audio_player_service.dart   # 24kHz PCM 譯文音訊即時播放
│   ├── widgets/
│   │   ├── audio_wave_visualizer.dart  # CustomPainter 原生動態音波視覺化
│   │   ├── subtitle_card.dart          # 雙語流式字幕毛玻璃卡片
│   │   ├── language_selector.dart      # 語言搜尋與快速挑選彈窗
│   │   ├── modern_control_bar.dart     # 懸浮膠囊控制列 (錄音、靜音、設定)
│   │   └── api_key_dialog.dart         # AI Studio API Key 設定對話框
│   └── screens/
│       └── home_screen.dart            # 主畫面佈局與即時翻譯狀態調度
├── test/
│   ├── languages_test.dart          # 語言目錄單元測試
│   └── gemini_live_client_test.dart # Gemini Live 協議與封包測試
└── pubspec.yaml                     # 專案依賴宣告
```

---

## 🚀 執行與建置指南 (Getting Started)

### 1. 前置需求 (Prerequisites)
- [Flutter SDK](https://flutter.dev/) (>= 3.24.0)
- **Windows 開發**：Visual Studio 2022（需勾選 "Desktop development with C++"）
- **Android 開發**：Android Studio / Android SDK (API Level >= 24)

### 2. 下載依賴
在專案根目錄執行：
```bash
flutter pub get
```

### 3. 在 Windows 上執行原生桌面程式
```bash
flutter run -d windows
```
若要打包發佈 Windows 執行檔 (`.exe`)：
```bash
flutter build windows --release
```
打包產物位於：`build/windows/x64/runner/Release/`

### 4. 在 Android 裝置上執行原生 APK
連接 Android 手機（開啟 USB 偵錯）或啟動 Android 模擬器：
```bash
flutter run -d android
```
若要打包發佈 Android APK 安裝檔：
```bash
flutter build apk --release
```
打包產物位於：`build/app/outputs/flutter-apk/app-release.apk`

---

---

## 🚀 CI/CD 自動發布 (Automated Release on Tag Push)
本專案已完整整合 GitHub Actions CI/CD 自動化流程。只要在 Git 中建立版本標籤並推送：
```bash
git tag v1.2.0
git push origin v1.2.0
```
GitHub Actions 將自動完成：
1. 在 Windows-2022 原生環境編譯 Windows x64 執行檔並壓縮為 `GeminiLiveTranslate-Windows-x64-v1.2.0.zip`。
2. 在 Ubuntu/JDK 17/Android SDK 環境編譯 Android Release APK `GeminiLiveTranslate-v1.2.0.apk`。
3. 自動建立 GitHub Release、產生 Commit 變更日誌，並掛載雙平台安裝檔發布。

