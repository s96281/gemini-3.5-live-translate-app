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
import '../services/window_service.dart';
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
  bool _isNativeOverlayActive = false;

  // Subtitle / Turn State
  TranslationTurn? _currentTurn;
  final List<TranslationTurn> _history = [];
  final ScrollController _scrollController = ScrollController();
  Timer? _segmentTimer;

  // Stream Subscriptions
  StreamSubscription? _stateSub;
  StreamSubscription? _sourceSub;
  StreamSubscription? _targetSub;
  StreamSubscription? _turnCompleteSub;
  StreamSubscription? _audioOutSub;
  StreamSubscription? _errorSub;
  StreamSubscription? _recorderChunkSub;
  StreamSubscription? _recorderAmpSub;

  @override
  void initState() {
    super.initState();
    _loadPreferences();
    _initRecorderListeners();
    WindowService.initNativeCallbacks(
      onClosed: () {
        if (mounted) {
          setState(() {
            _isNativeOverlayActive = false;
          });
        }
      },
    );
  }

  @override
  void dispose() {
    _cancelSubscriptions();
    _segmentTimer?.cancel();
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
    final savedMaxParagraphLength = prefs.getInt('gemini_max_paragraph_length') ?? 50;
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
        maxParagraphLength: savedMaxParagraphLength,
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

      _turnCompleteSub = _liveClient!.turnCompleteStream.listen((_) {
        _finalizeCurrentTurn();
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
    _segmentTimer?.cancel();
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
    if (text.isEmpty) return;

    _currentTurn ??= TranslationTurn(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      timestamp: DateTime.now(),
      targetLanguageCode: _config.targetLanguageCode,
    );

    _currentTurn!.sourceText += text;

    // 若僅有原始語音辨識（譯文為空），依照段落硬切割字數自動分段
    final maxLen = _config.maxParagraphLength;
    if (maxLen > 0 && _currentTurn!.targetText.isEmpty) {
      final cutPos = _findSourceOnlyCutIndex(_currentTurn!.sourceText, maxLen);
      if (cutPos != null && cutPos > 0 && cutPos < _currentTurn!.sourceText.length) {
        final sourcePart1 = _currentTurn!.sourceText.substring(0, cutPos).trim();
        final sourcePart2 = _currentTurn!.sourceText.substring(cutPos).trimLeft();

        _performHardCut(
          targetPart1: '',
          targetPart2: '',
          sourcePart1: sourcePart1,
          sourcePart2: sourcePart2,
        );
        _scrollToBottom();
        _scheduleAutoSegment(text);
        return;
      }
    }

    setState(() {});
    if (_isNativeOverlayActive) {
      WindowService.updateNativeOverlaySubtitle(
        source: _currentTurn!.sourceText,
        target: _currentTurn!.targetText,
      );
    }
    _scrollToBottom();
    _scheduleAutoSegment(text);
  }

  void _onTargetSpeechReceived(String text) {
    if (text.isEmpty) return;

    _currentTurn ??= TranslationTurn(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      timestamp: DateTime.now(),
      targetLanguageCode: _config.targetLanguageCode,
    );

    _currentTurn!.targetText += text;

    // 長段落自動分段（硬切割）：達到指定長度時自動切分回合，防止超長段落堆疊
    final maxLen = _config.maxParagraphLength;
    if (maxLen > 0) {
      final cutPos = _findTargetCutIndex(_currentTurn!.targetText, maxLen);
      if (cutPos != null && cutPos > 0 && cutPos < _currentTurn!.targetText.length) {
        final targetPart1 = _currentTurn!.targetText.substring(0, cutPos).trim();
        final targetPart2 = _currentTurn!.targetText.substring(cutPos).trimLeft();

        final source = _currentTurn!.sourceText.trim();
        String sourcePart1 = source;
        String sourcePart2 = '';
        if (source.isNotEmpty) {
          final totalLen = targetPart1.length + targetPart2.length;
          final ratio = totalLen > 0 ? (targetPart1.length / totalLen) : 1.0;
          final srcCutPos = _findSourceCutIndex(source, ratio);
          if (srcCutPos > 0 && srcCutPos < source.length) {
            sourcePart1 = source.substring(0, srcCutPos).trim();
            sourcePart2 = source.substring(srcCutPos).trimLeft();
          }
        }

        _performHardCut(
          targetPart1: targetPart1,
          targetPart2: targetPart2,
          sourcePart1: sourcePart1,
          sourcePart2: sourcePart2,
        );
        _scrollToBottom();
        _scheduleAutoSegment(text);
        return;
      }
    }

    setState(() {});
    if (_isNativeOverlayActive) {
      WindowService.updateNativeOverlaySubtitle(
        source: _currentTurn!.sourceText,
        target: _currentTurn!.targetText,
      );
    }
    _scrollToBottom();
    _scheduleAutoSegment(text);
  }

  /// 執行段落長度硬切割：將上半段結算存入歷史記錄，下半段作為當前活躍回合繼續接收
  void _performHardCut({
    required String targetPart1,
    required String targetPart2,
    required String sourcePart1,
    required String sourcePart2,
  }) {
    final finishedTurn = _currentTurn!;
    finishedTurn.targetText = targetPart1;
    finishedTurn.sourceText = sourcePart1;
    finishedTurn.isFinal = true;

    TranslationTurn? newTurn;
    if (targetPart2.isNotEmpty || sourcePart2.isNotEmpty) {
      newTurn = TranslationTurn(
        id: DateTime.now().millisecondsSinceEpoch.toString(),
        timestamp: DateTime.now(),
        targetLanguageCode: _config.targetLanguageCode,
      );
      newTurn.targetText = targetPart2;
      newTurn.sourceText = sourcePart2;
    }

    setState(() {
      _history.insert(0, finishedTurn);
      _currentTurn = newTurn;
    });

    if (_isNativeOverlayActive) {
      final dispSource = (newTurn != null && newTurn.sourceText.isNotEmpty)
          ? newTurn.sourceText
          : finishedTurn.sourceText;
      final dispTarget = (newTurn != null && newTurn.targetText.isNotEmpty)
          ? newTurn.targetText
          : finishedTurn.targetText;
      WindowService.updateNativeOverlaySubtitle(
        source: dispSource,
        target: dispTarget,
      );
    }
  }

  /// 尋找譯文目標長度硬切割位置：
  /// 1. 當長度達到 70% maxLen 時，尋找自然句子結尾（。！？!?\n）
  /// 2. 當長度達到 100% maxLen 時，尋找子句停頓（，、；,;）
  /// 3. 當長度達到 130% maxLen 時，強制硬切割（空格或指定字數）
  int? _findTargetCutIndex(String text, int maxLen) {
    final minScan = (maxLen * 0.7).round();
    if (text.length < minScan) return null;

    // 1. 優先在 [minScan, end] 區間尋找句尾標點
    const primaryPunct = ['。', '！', '？', '\n', '!', '?'];
    for (int i = text.length - 1; i >= minScan; i--) {
      if (primaryPunct.contains(text[i])) {
        return i + 1;
      }
    }

    // 2. 超過 maxLen 時，尋找逗號或分號停頓
    if (text.length >= maxLen) {
      const secondaryPunct = ['，', '、', '；', ',', ';'];
      final minClauseScan = (maxLen * 0.6).round();
      for (int i = text.length - 1; i >= minClauseScan; i--) {
        if (secondaryPunct.contains(text[i])) {
          return i + 1;
        }
      }
    }

    // 3. 超過 130% maxLen 時強制硬切斷
    if (text.length >= (maxLen * 1.3).round()) {
      final spaceIdx = text.lastIndexOf(' ', maxLen);
      if (spaceIdx >= (maxLen * 0.8).round()) {
        return spaceIdx + 1;
      }
      return maxLen;
    }

    return null;
  }

  /// 依比例與標點符號切割原始語音句子，維持原文與譯文對齊
  int _findSourceCutIndex(String source, double ratio) {
    if (source.length <= 40) return source.length;

    final targetPos = (source.length * ratio).round().clamp(15, source.length);
    const terminators = ['. ', '! ', '? ', '.\n', '!\n', '?\n'];

    int bestIdx = -1;
    int minDiff = 999999;

    for (final term in terminators) {
      int idx = 0;
      while ((idx = source.indexOf(term, idx)) != -1) {
        final cutAt = idx + term.length;
        final diff = (cutAt - targetPos).abs();
        if (diff < minDiff) {
          minDiff = diff;
          bestIdx = cutAt;
        }
        idx += term.length;
      }
    }

    if (bestIdx != -1) return bestIdx;

    // 次要逗號匹配
    const clauseTerms = [', ', '; '];
    for (final term in clauseTerms) {
      int idx = 0;
      while ((idx = source.indexOf(term, idx)) != -1) {
        final cutAt = idx + term.length;
        final diff = (cutAt - targetPos).abs();
        if (diff < minDiff) {
          minDiff = diff;
          bestIdx = cutAt;
        }
        idx += term.length;
      }
    }

    if (bestIdx != -1) return bestIdx;

    final spaceIdx = source.lastIndexOf(' ', targetPos);
    if (spaceIdx > 15) return spaceIdx + 1;

    return source.length;
  }

  /// 針對純原始語音輸入（無譯文）時的長段落硬切割
  int? _findSourceOnlyCutIndex(String text, int maxLen) {
    final sourceMaxLen = (maxLen * 2.4).round();
    final minScan = (sourceMaxLen * 0.7).round();
    if (text.length < minScan) return null;

    const terminators = ['. ', '! ', '? ', '.\n', '!\n', '?\n'];
    int bestIdx = -1;
    for (final term in terminators) {
      int idx = minScan;
      while ((idx = text.indexOf(term, idx)) != -1) {
        final cutAt = idx + term.length;
        if (cutAt > bestIdx) {
          bestIdx = cutAt;
        }
        idx += term.length;
      }
    }
    if (bestIdx != -1 && bestIdx <= text.length) return bestIdx;

    if (text.length >= sourceMaxLen) {
      final spaceIdx = text.lastIndexOf(' ', sourceMaxLen);
      if (spaceIdx >= (sourceMaxLen * 0.7).round()) {
        return spaceIdx + 1;
      }
      if (text.length >= (sourceMaxLen * 1.3).round()) {
        return sourceMaxLen;
      }
    }

    return null;
  }

  /// 自然停頓與短句分段定時器：
  /// 當句子包含明確句尾終止標點停頓 800ms，或短暫靜音 1800ms 無新文字時結算回合
  void _scheduleAutoSegment(String recentChunk) {
    _segmentTimer?.cancel();

    final trimmed = recentChunk.trimRight();
    final bool hasTerminalPunctuation = trimmed.endsWith('。') ||
        trimmed.endsWith('！') ||
        trimmed.endsWith('？') ||
        trimmed.endsWith('.') ||
        trimmed.endsWith('!') ||
        trimmed.endsWith('?') ||
        trimmed.endsWith('\n');

    final durationMs = hasTerminalPunctuation ? 800 : 1800;

    _segmentTimer = Timer(Duration(milliseconds: durationMs), () {
      _finalizeCurrentTurn();
    });
  }

  void _finalizeCurrentTurn() {
    _segmentTimer?.cancel();
    if (_currentTurn == null) return;
    if (_currentTurn!.sourceText.trim().isEmpty && _currentTurn!.targetText.trim().isEmpty) {
      return;
    }

    final finishedTurn = _currentTurn!;
    setState(() {
      finishedTurn.isFinal = true;
      _history.insert(0, finishedTurn);
      _currentTurn = null;
    });

    if (_isNativeOverlayActive) {
      WindowService.updateNativeOverlaySubtitle(
        source: finishedTurn.sourceText,
        target: finishedTurn.targetText,
      );
    }
  }

  Future<void> _toggleNativeSubtitleOverlay() async {
    final next = !_isNativeOverlayActive;
    await WindowService.showNativeOverlay(next);
    if (next) {
      final initialSource = _currentTurn?.sourceText ??
          (_history.isNotEmpty ? _history.first.sourceText : '');
      final initialTarget = _currentTurn?.targetText ??
          (_history.isNotEmpty ? _history.first.targetText : '');
      await WindowService.updateNativeOverlaySubtitle(
        source: initialSource,
        target: initialTarget,
      );
    }
    setState(() {
      _isNativeOverlayActive = next;
    });
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
    _segmentTimer?.cancel();
    _stateSub?.cancel();
    _sourceSub?.cancel();
    _targetSub?.cancel();
    _turnCompleteSub?.cancel();
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
    if (_isNativeOverlayActive) {
      WindowService.updateNativeOverlaySubtitle(source: '', target: '');
    }
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

          // Desktop Floating Transparent Subtitle Window Button (Lyrics Overlay)
          Tooltip(
            message: _isNativeOverlayActive
                ? '關閉桌面懸浮字幕 (置頂/穿透模式)'
                : '開啟桌面全透明懸浮字幕 (歌詞置頂/穿透模式)',
            child: InkWell(
              onTap: _toggleNativeSubtitleOverlay,
              borderRadius: BorderRadius.circular(12),
              child: Container(
                margin: const EdgeInsets.only(right: 6),
                padding: const EdgeInsets.all(6),
                decoration: BoxDecoration(
                  color: _isNativeOverlayActive
                      ? cyanPrimary.withOpacity(0.18)
                      : (isDark ? Colors.white.withOpacity(0.06) : Colors.white),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: _isNativeOverlayActive
                        ? cyanPrimary
                        : (isDark ? Colors.white10 : const Color(0xFFCBD5E1)),
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
                  Icons.subtitles_rounded,
                  size: 15,
                  color: _isNativeOverlayActive
                      ? cyanPrimary
                      : (isDark ? Colors.white70 : const Color(0xFF475569)),
                ),
              ),
            ),
          ),

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
