import 'dart:async';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config/languages.dart';
import '../config/theme.dart';
import '../models/live_config.dart';
import '../models/translation_turn.dart';
import '../services/audio_player_service.dart';
import '../services/audio_recorder_service.dart';
import '../services/gemini_live_client.dart';
import '../widgets/api_key_dialog.dart';
import '../widgets/audio_wave_visualizer.dart';
import '../widgets/language_selector.dart';
import '../widgets/modern_control_bar.dart';
import '../widgets/subtitle_card.dart';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  // Services
  GeminiLiveClient? _liveClient;
  final AudioRecorderService _recorderService = AudioRecorderService();
  final AudioPlayerService _playerService = AudioPlayerService();

  // App State
  LiveConfig _config = const LiveConfig(apiKey: '');
  LiveConnectionState _connectionState = LiveConnectionState.disconnected;
  double _currentAmplitude = 0.0;
  bool _isMuted = false;

  // Subtitle / Turn State
  TranslationTurn? _currentTurn;
  final List<TranslationTurn> _history = [];
  final ScrollController _scrollController = ScrollController();

  // Stream Subscriptions
  StreamSubscription? _stateSub;
  StreamSubscription? _sourceSub;
  StreamSubscription? _targetSub;
  StreamSubscription? _audioOutSub;
  StreamSubscription? _errorSub;
  StreamSubscription? _recorderChunkSub;
  StreamSubscription? _recorderAmpSub;

  @override
  void initState() {
    super.initState();
    _loadPreferences();
    _initRecorderListeners();
  }

  @override
  void dispose() {
    _cancelSubscriptions();
    _recorderChunkSub?.cancel();
    _recorderAmpSub?.cancel();
    _liveClient?.dispose();
    _recorderService.dispose();
    _playerService.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _loadPreferences() async {
    final prefs = await SharedPreferences.getInstance();
    final savedKey = prefs.getString('gemini_api_key') ?? '';
    final savedModel = prefs.getString('gemini_model_name') ?? 'models/gemini-2.0-flash-exp';
    final savedLang = prefs.getString('gemini_target_lang') ?? 'zh-TW';
    final savedVoice = prefs.getString('gemini_voice') ?? 'Puck';
    final savedAutoPlay = prefs.getBool('gemini_autoplay') ?? false;
    final savedSourceStr = prefs.getString('gemini_audio_source_mode') ?? 'systemAudio';
    final savedDeviceId = prefs.getString('gemini_selected_device_id');
    final sourceMode = (savedSourceStr == 'microphone')
        ? AudioSourceMode.microphone
        : AudioSourceMode.systemAudio;

    setState(() {
      _config = LiveConfig(
        apiKey: savedKey,
        modelName: savedModel,
        targetLanguageCode: savedLang,
        voiceName: savedVoice,
        autoPlayAudio: savedAutoPlay,
        audioSourceMode: sourceMode,
        selectedDeviceId: savedDeviceId,
      );
      _isMuted = !savedAutoPlay;
      _playerService.isMuted = _isMuted;
    });

    if (savedKey.isEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _openSettings();
      });
    }
  }

  void _initRecorderListeners() {
    // Pipe recorded PCM audio chunks directly to Gemini Live WebSocket
    _recorderChunkSub = _recorderService.audioChunkStream.listen((chunk) {
      if (_liveClient != null &&
          (_connectionState == LiveConnectionState.connected ||
           _connectionState == LiveConnectionState.translating)) {
        _liveClient!.sendAudioChunk(chunk);
      }
    });

    // Pipe amplitude to visualizer
    _recorderAmpSub = _recorderService.amplitudeStream.listen((amp) {
      setState(() {
        _currentAmplitude = amp;
      });
    });
  }

  Future<void> _toggleTranslationSession() async {
    if (_recorderService.isRecording) {
      await _stopSession();
    } else {
      await _startSession();
    }
  }

  Future<void> _toggleAudioSourceMode() async {
    final newMode = _config.audioSourceMode == AudioSourceMode.systemAudio
        ? AudioSourceMode.microphone
        : AudioSourceMode.systemAudio;

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('gemini_audio_source_mode', newMode.name);

    setState(() {
      _config = _config.copyWith(audioSourceMode: newMode);
    });

    _showToast(newMode == AudioSourceMode.systemAudio
        ? '已切換至「裝置系統音效」來源'
        : '已切換至「實體麥克風」來源');

    // 若正在連線翻譯中，重啟以套用新音訊來源端點與 DSP 設定
    if (_recorderService.isRecording) {
      await _stopSession();
      await _startSession();
    }
  }

  Future<void> _startSession() async {
    if (_config.apiKey.trim().isEmpty) {
      _showToast('請先設定 Google AI Studio API Key');
      _openSettings();
      return;
    }

    try {
      // 1. Initialize Gemini Live Client
      _cancelSubscriptions();
      _liveClient?.dispose();

      _liveClient = GeminiLiveClient(config: _config);

      _stateSub = _liveClient!.stateStream.listen((st) {
        setState(() => _connectionState = st);
        if (st == LiveConnectionState.error || st == LiveConnectionState.disconnected) {
          if (_recorderService.isRecording) {
            _recorderService.stopRecording();
          }
        }
      });

      _sourceSub = _liveClient!.sourceTranscriptStream.listen((text) {
        _onSourceSpeechReceived(text);
      });

      _targetSub = _liveClient!.targetTranscriptStream.listen((text) {
        _onTargetSpeechReceived(text);
      });

      _audioOutSub = _liveClient!.audioOutputStream.listen((pcmBytes) {
        if (!_isMuted) {
          _playerService.feedPcmChunk(pcmBytes);
        }
      });

      _errorSub = _liveClient!.errorStream.listen((err) {
        _showToast(err);
      });

      // 2. Connect WebSocket
      await _liveClient!.connect();

      // 3. Start Native Recording (System Audio or Mic at 16kHz PCM)
      await _recorderService.startRecording(
        sourceMode: _config.audioSourceMode,
        deviceId: _config.selectedDeviceId,
      );

      setState(() {
        _currentTurn = TranslationTurn(
          id: DateTime.now().millisecondsSinceEpoch.toString(),
          timestamp: DateTime.now(),
          targetLanguageCode: _config.targetLanguageCode,
        );
      });
    } catch (e) {
      _showToast('啟動即時翻譯失敗: $e');
      await _stopSession();
    }
  }

  Future<void> _stopSession() async {
    await _recorderService.stopRecording();
    await _liveClient?.disconnect();
    await _playerService.stop();

    setState(() {
      if (_currentTurn != null &&
          (_currentTurn!.sourceText.isNotEmpty || _currentTurn!.targetText.isNotEmpty)) {
        _currentTurn!.isFinal = true;
        _history.insert(0, _currentTurn!);
      }
      _currentTurn = null;
      _connectionState = LiveConnectionState.disconnected;
      _currentAmplitude = 0.0;
    });
  }

  void _onSourceSpeechReceived(String text) {
    setState(() {
      _currentTurn ??= TranslationTurn(
        id: DateTime.now().millisecondsSinceEpoch.toString(),
        timestamp: DateTime.now(),
        targetLanguageCode: _config.targetLanguageCode,
      );
      _currentTurn!.sourceText += text;
    });
    _scrollToBottom();
  }

  void _onTargetSpeechReceived(String text) {
    setState(() {
      _currentTurn ??= TranslationTurn(
        id: DateTime.now().millisecondsSinceEpoch.toString(),
        timestamp: DateTime.now(),
        targetLanguageCode: _config.targetLanguageCode,
      );
      _currentTurn!.targetText += text;
    });
    _scrollToBottom();
  }

  void _scrollToBottom() {
    if (_scrollController.hasClients) {
      _scrollController.animateTo(
        0.0,
        duration: const Duration(milliseconds: 200),
        curve: Curves.easeOut,
      );
    }
  }

  void _cancelSubscriptions() {
    _stateSub?.cancel();
    _sourceSub?.cancel();
    _targetSub?.cancel();
    _audioOutSub?.cancel();
    _errorSub?.cancel();
  }

  void _showToast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(msg),
        backgroundColor: const Color(0xFF1E293B),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }

  void _openSettings() {
    ApiKeyDialog.show(
      context,
      currentConfig: _config,
      onSave: (newCfg) {
        setState(() {
          _config = newCfg;
          _isMuted = !newCfg.autoPlayAudio;
          _playerService.isMuted = _isMuted;
        });
      },
    );
  }

  void _openLanguageSelector() {
    LanguageSelectorModal.show(
      context,
      currentCode: _config.targetLanguageCode,
      onSelected: (lang) async {
        setState(() {
          _config = _config.copyWith(targetLanguageCode: lang.code);
        });
        final prefs = await SharedPreferences.getInstance();
        await prefs.setString('gemini_target_lang', lang.code);

        // If currently translating, restart session with new target language
        if (_recorderService.isRecording) {
          await _stopSession();
          await _startSession();
        }
      },
    );
  }

  void _clearHistory() {
    setState(() {
      _history.clear();
      _currentTurn = null;
    });
    _showToast('字幕歷史已清除');
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final targetLang = SupportedLanguages.findByCode(_config.targetLanguageCode);
    final isRecording = _recorderService.isRecording;

    return Scaffold(
      backgroundColor: isDark ? const Color(0xFF090D16) : const Color(0xFFF3F6FC),
      body: SafeArea(
        child: Stack(
          children: [
            // Ambient Neon Glow in Background
            Positioned(
              top: -80,
              left: -80,
              child: Container(
                width: 240,
                height: 240,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: (isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7))
                      .withOpacity(isDark ? 0.08 : 0.05),
                ),
              ),
            ),
            Positioned(
              bottom: 40,
              right: -60,
              child: Container(
                width: 280,
                height: 280,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: (isDark ? const Color(0xFF7928CA) : const Color(0xFF7C3AED))
                      .withOpacity(isDark ? 0.08 : 0.05),
                ),
              ),
            ),

            // Main Layout
            Column(
              children: [
                // Top Header Bar
                _buildHeader(targetLang, isDark),

                // Live Audio Wave Visualizer
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: AudioWaveVisualizer(
                    amplitude: _currentAmplitude,
                    isRecording: isRecording,
                  ),
                ),

                // Subtitle Feed
                Expanded(
                  child: _history.isEmpty && _currentTurn == null
                      ? _buildEmptyState(isDark)
                      : ListView.builder(
                          controller: _scrollController,
                          reverse: true, // Auto scrolls latest to bottom
                          padding: const EdgeInsets.only(bottom: 90, top: 10),
                          itemCount: (_currentTurn != null ? 1 : 0) + _history.length,
                          itemBuilder: (context, index) {
                            if (_currentTurn != null && index == 0) {
                              return SubtitleCard(
                                turn: _currentTurn!,
                                isLive: true,
                              );
                            }
                            final histIndex = _currentTurn != null ? index - 1 : index;
                            return SubtitleCard(
                              turn: _history[histIndex],
                              isLive: false,
                            );
                          },
                        ),
                ),
              ],
            ),

            // Floating Bottom Control Bar (Never truncated on mobile)
            Positioned(
              bottom: 0,
              left: 0,
              right: 0,
              child: Center(
                child: ModernControlBar(
                  isRecording: isRecording,
                  isMuted: _isMuted,
                  targetLanguageDisplay: '${targetLang.flag} ${targetLang.nameZh}',
                  audioSourceMode: _config.audioSourceMode,
                  onToggleRecording: _toggleTranslationSession,
                  onToggleAudioSource: _toggleAudioSourceMode,
                  onToggleMute: () {
                    setState(() {
                      _isMuted = !_isMuted;
                      _playerService.isMuted = _isMuted;
                    });
                  },
                  onSelectLanguage: _openLanguageSelector,
                  onClearHistory: _clearHistory,
                  onOpenSettings: _openSettings,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildHeader(LanguageInfo targetLang, bool isDark) {
    final isSystemAudio = (_config.audioSourceMode == AudioSourceMode.systemAudio);
    final cyanPrimary = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(
        children: [
          // Logo & Title
          Container(
            padding: const EdgeInsets.all(7),
            decoration: BoxDecoration(
              gradient: const LinearGradient(
                colors: [Color(0xFF00E5FF), Color(0xFF7928CA)],
              ),
              borderRadius: BorderRadius.circular(10),
            ),
            child: const Icon(Icons.bolt, color: Colors.white, size: 20),
          ),
          const SizedBox(width: 8),
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Gemini Live Translate',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.bold,
                  color: isDark ? Colors.white : const Color(0xFF0F172A),
                  letterSpacing: 0.2,
                ),
              ),
              Text(
                'gemini-3.5-live-translate-preview',
                style: TextStyle(
                  fontSize: 10,
                  color: cyanPrimary,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ),
          const Spacer(),

          // Dark / Light Theme Toggle Button
          Tooltip(
            message: isDark ? '切換為明亮風格' : '切換為黑暗風格',
            child: InkWell(
              onTap: AppThemes.toggleTheme,
              borderRadius: BorderRadius.circular(12),
              child: Container(
                margin: const EdgeInsets.only(right: 6),
                padding: const EdgeInsets.all(6),
                decoration: BoxDecoration(
                  color: isDark ? Colors.white.withOpacity(0.06) : Colors.white,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: isDark ? Colors.white10 : const Color(0xFFCBD5E1),
                  ),
                  boxShadow: isDark
                      ? []
                      : [
                          const BoxShadow(
                            color: Color(0x0C000000),
                            blurRadius: 4,
                            offset: Offset(0, 2),
                          ),
                        ],
                ),
                child: Icon(
                  isDark ? Icons.light_mode_rounded : Icons.dark_mode_rounded,
                  size: 15,
                  color: isDark ? Colors.amberAccent : const Color(0xFF475569),
                ),
              ),
            ),
          ),

          // Audio Source Badge
          InkWell(
            onTap: _toggleAudioSourceMode,
            borderRadius: BorderRadius.circular(12),
            child: Container(
              margin: const EdgeInsets.only(right: 6),
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 5),
              decoration: BoxDecoration(
                color: isSystemAudio
                    ? (isDark ? const Color(0xFF00E5FF).withOpacity(0.12) : const Color(0xFFE0F2FE))
                    : (isDark ? const Color(0xFF7928CA).withOpacity(0.12) : const Color(0xFFF3E8FF)),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(
                  color: isSystemAudio
                      ? (isDark ? const Color(0xFF00E5FF).withOpacity(0.4) : const Color(0xFFBAE6FD))
                      : (isDark ? const Color(0xFF7928CA).withOpacity(0.4) : const Color(0xFFDDD6FE)),
                ),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    isSystemAudio ? Icons.speaker_rounded : Icons.mic_rounded,
                    size: 12,
                    color: isSystemAudio
                        ? cyanPrimary
                        : (isDark ? const Color(0xFFD946EF) : const Color(0xFF7C3AED)),
                  ),
                  const SizedBox(width: 3),
                  Text(
                    isSystemAudio ? '系統音效' : '麥克風',
                    style: TextStyle(
                      fontSize: 10,
                      color: isSystemAudio
                          ? cyanPrimary
                          : (isDark ? const Color(0xFFE2B8FF) : const Color(0xFF7C3AED)),
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ),
          ),

          // Connection State Pill
          _buildConnectionPill(isDark),
        ],
      ),
    );
  }

  Widget _buildConnectionPill(bool isDark) {
    Color dotColor;
    String statusText;

    switch (_connectionState) {
      case LiveConnectionState.connecting:
        dotColor = Colors.amberAccent;
        statusText = '連線中...';
        break;
      case LiveConnectionState.connected:
        dotColor = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);
        statusText = 'Ready';
        break;
      case LiveConnectionState.translating:
        dotColor = isDark ? Colors.greenAccent : const Color(0xFF16A34A);
        statusText = 'Live 翻譯中';
        break;
      case LiveConnectionState.error:
        dotColor = Colors.redAccent;
        statusText = '錯誤';
        break;
      case LiveConnectionState.disconnected:
      default:
        dotColor = isDark ? Colors.white30 : const Color(0xFF94A3B8);
        statusText = '待命';
        break;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
      decoration: BoxDecoration(
        color: isDark ? Colors.white.withOpacity(0.06) : Colors.white,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: isDark ? Colors.white10 : const Color(0xFFCBD5E1)),
        boxShadow: isDark
            ? []
            : [
                const BoxShadow(
                  color: Color(0x0C000000),
                  blurRadius: 4,
                  offset: Offset(0, 2),
                ),
              ],
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(
              color: dotColor,
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: dotColor.withOpacity(0.6),
                  blurRadius: 5,
                  spreadRadius: 1,
                )
              ],
            ),
          ),
          const SizedBox(width: 5),
          Text(
            statusText,
            style: TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.w600,
              color: dotColor,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildEmptyState(bool isDark) {
    final isSystemAudio = (_config.audioSourceMode == AudioSourceMode.systemAudio);

    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            isSystemAudio ? Icons.speaker_rounded : Icons.graphic_eq_rounded,
            size: 64,
            color: isDark ? Colors.white.withOpacity(0.14) : const Color(0xFFCBD5E1),
          ),
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 24),
            child: Text(
              isSystemAudio ? '播放電腦/手機音效，並點擊下方按鈕開始即時口譯' : '對著麥克風說話，並點擊下方按鈕開始即時口譯',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: isDark ? Colors.white.withOpacity(0.8) : const Color(0xFF1E293B),
              ),
            ),
          ),
          const SizedBox(height: 6),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 28),
            child: Text(
              isSystemAudio
                  ? '已設定為裝置音效模式（影片/會議/遊戲），自動關閉抑噪以保留原始聲音細節'
                  : '裝置將以 16kHz PCM 麥克風音訊即時串流至 Gemini 3.5 Live',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 12,
                color: isDark ? Colors.white.withOpacity(0.35) : const Color(0xFF64748B),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
