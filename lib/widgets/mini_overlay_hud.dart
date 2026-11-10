import 'dart:io';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:hotkey_manager/hotkey_manager.dart';

import '../config/languages.dart';
import '../models/live_config.dart';
import '../models/translation_turn.dart';
import '../services/gemini_live_client.dart';
import '../services/window_service.dart';

/// Compact, floating picture-in-picture subtitle HUD widget.
/// Ideal for watching movies, streaming conferences, or gaming with real-time translation on top.
class MiniOverlayHud extends StatefulWidget {
  final TranslationTurn? currentTurn;
  final List<TranslationTurn> history;
  final LiveConfig config;
  final LiveConnectionState connectionState;
  final bool isRecording;
  final bool isMuted;
  final VoidCallback onToggleRecording;
  final VoidCallback onToggleMute;
  final VoidCallback onExitOverlay;
  final VoidCallback onSelectLanguage;

  const MiniOverlayHud({
    super.key,
    required this.currentTurn,
    required this.history,
    required this.config,
    required this.connectionState,
    required this.isRecording,
    required this.isMuted,
    required this.onToggleRecording,
    required this.onToggleMute,
    required this.onExitOverlay,
    required this.onSelectLanguage,
  });

  @override
  State<MiniOverlayHud> createState() => _MiniOverlayHudState();
}

class _MiniOverlayHudState extends State<MiniOverlayHud> {
  double _opacity = 0.35;
  bool _isClickThrough = false;
  double _fontSize = 22.0;

  final HotKey _toggleLockHotKey = HotKey(
    key: PhysicalKeyboardKey.f8,
    modifiers: [HotKeyModifier.control],
    scope: HotKeyScope.system,
  );

  @override
  void initState() {
    super.initState();
    _initHotKey();
  }

  Future<void> _initHotKey() async {
    if (Platform.isWindows) {
      try {
        await hotKeyManager.unregister(_toggleLockHotKey);
      } catch (_) {}
      try {
        await hotKeyManager.register(
          _toggleLockHotKey,
          keyDownHandler: (hotKey) => _toggleClickThrough(),
        );
      } catch (e) {
        debugPrint('[MiniOverlayHud] hotkey register error: $e');
      }
    }
  }

  Future<void> _toggleClickThrough() async {
    final next = !_isClickThrough;
    setState(() {
      _isClickThrough = next;
    });
    if (Platform.isWindows) {
      await WindowService.setIgnoreMouseEvents(next);
    }
  }

  @override
  void dispose() {
    if (Platform.isWindows) {
      try {
        hotKeyManager.unregister(_toggleLockHotKey);
        WindowService.setIgnoreMouseEvents(false);
      } catch (_) {}
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final cyanPrimary = const Color(0xFF64FFDA);

    // Get latest display text: either from active current turn or most recent history turn
    String sourceText = widget.currentTurn?.sourceText ?? '';
    String targetText = widget.currentTurn?.targetText ?? '';
    if (sourceText.isEmpty && targetText.isEmpty && widget.history.isNotEmpty) {
      sourceText = widget.history.first.sourceText;
      targetText = widget.history.first.targetText;
    }

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: GestureDetector(
        behavior: HitTestBehavior.translucent,
        onPanStart: (_) => WindowService.startDragging(),
        child: Container(
          width: double.infinity,
          height: double.infinity,
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          decoration: BoxDecoration(
            color: _isClickThrough
                ? Colors.transparent
                : Colors.black.withOpacity(_opacity),
            borderRadius: BorderRadius.circular(16),
            border: _isClickThrough
                ? null
                : Border.all(
                    color: Colors.white.withOpacity(0.18),
                    width: 1.0,
                  ),
            boxShadow: _isClickThrough
                ? []
                : [
                    BoxShadow(
                      color: Colors.black.withOpacity(0.4),
                      blurRadius: 16,
                      offset: const Offset(0, 4),
                    )
                  ],
          ),
          child: Column(
            mainAxisSize: MainAxisSize.max,
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              // Top Minimalist Toolbar (hidden during click-through)
              if (!_isClickThrough)
                Row(
                  children: [
                    // Status Dot
                    Container(
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: widget.isRecording ? Colors.greenAccent : Colors.amberAccent,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: (widget.isRecording ? Colors.greenAccent : Colors.amberAccent)
                                .withOpacity(0.6),
                            blurRadius: 6,
                            spreadRadius: 1,
                          )
                        ],
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      widget.isRecording ? '即時口譯中' : '口譯待命中',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: Colors.white.withOpacity(0.85),
                      ),
                    ),
                    const SizedBox(width: 8),

                    // Drag Indicator
                    const Icon(Icons.drag_indicator_rounded, size: 14, color: Colors.white60),
                    const SizedBox(width: 2),
                    Text(
                      '拖曳移動 | [Ctrl + F8] 穿透',
                      style: TextStyle(
                        color: Colors.white.withOpacity(0.65),
                        fontSize: 10,
                      ),
                    ),

                    const Spacer(),

                    // Recording Play/Stop Mini Button
                    IconButton(
                      icon: Icon(
                        widget.isRecording ? Icons.stop_circle_rounded : Icons.play_circle_fill_rounded,
                        size: 18,
                      ),
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      color: widget.isRecording ? Colors.redAccent : cyanPrimary,
                      tooltip: widget.isRecording ? '停止語音辨識' : '開始語音辨識',
                      onPressed: widget.onToggleRecording,
                    ),

                    // TTS Mute toggle
                    IconButton(
                      icon: Icon(
                        widget.isMuted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                        size: 16,
                      ),
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      color: widget.isMuted ? Colors.white38 : cyanPrimary,
                      tooltip: widget.isMuted ? '已靜音' : '朗讀中',
                      onPressed: widget.onToggleMute,
                    ),

                    // Font Size Adjuster
                    IconButton(
                      icon: const Icon(Icons.format_size_rounded, size: 16, color: Colors.white70),
                      tooltip: '縮放字幕字體 ($_fontSize px)',
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      onPressed: () {
                        setState(() {
                          if (_fontSize >= 28.0) {
                            _fontSize = 18.0;
                          } else {
                            _fontSize += 4.0;
                          }
                        });
                      },
                    ),

                    // Background Opacity cycle
                    IconButton(
                      icon: const Icon(Icons.opacity_rounded, size: 16, color: Colors.white70),
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      tooltip: '切換底色 (${(_opacity * 100).toInt()}%)',
                      onPressed: () {
                        setState(() {
                          if (_opacity >= 0.7) {
                            _opacity = 0.0;
                          } else if (_opacity >= 0.3) {
                            _opacity = 0.75;
                          } else {
                            _opacity = 0.35;
                          }
                        });
                      },
                    ),

                    // Click-Through Lock Button (Ctrl + F8)
                    IconButton(
                      icon: const Icon(Icons.lock_open_rounded, size: 16),
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      color: cyanPrimary,
                      tooltip: '鎖定並啟用滑鼠穿透 (Ctrl + F8 解鎖)',
                      onPressed: _toggleClickThrough,
                    ),

                    const SizedBox(width: 4),

                    // Restore to main window button
                    IconButton(
                      icon: const Icon(Icons.open_in_full_rounded, size: 16, color: Colors.white),
                      visualDensity: VisualDensity.compact,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(minWidth: 26, minHeight: 26),
                      tooltip: '還原回完整視窗',
                      onPressed: widget.onExitOverlay,
                    ),
                  ],
                ),

              const Spacer(),

              // Primary Translated Subtitle (Prominent Neon Stroke Text)
              _buildStrokedText(
                text: targetText.isNotEmpty
                    ? targetText
                    : (widget.isRecording ? '聆聽音訊中...' : '即時口譯已就緒，等待說話...'),
                fontSize: _fontSize,
                fontWeight: FontWeight.w700,
                textColor: cyanPrimary,
                strokeColor: Colors.black.withOpacity(0.95),
                strokeWidth: 3.5,
              ),

              const SizedBox(height: 4),

              // Secondary Source Recognition Subtitle
              if (sourceText.isNotEmpty)
                _buildStrokedText(
                  text: sourceText,
                  fontSize: (_fontSize * 0.72).clamp(12.0, 20.0),
                  fontWeight: FontWeight.w500,
                  textColor: Colors.white.withOpacity(0.92),
                  strokeColor: Colors.black.withOpacity(0.9),
                  strokeWidth: 2.5,
                ),

              const Spacer(),
            ],
          ),
        ),
      ),
    );
  }

  /// Dual-pass rendering:
  /// Bottom pass draws thick black outline stroke (PaintingStyle.stroke).
  /// Top pass renders the crisp colored fill and double drop shadows.
  Widget _buildStrokedText({
    required String text,
    required double fontSize,
    required FontWeight fontWeight,
    required Color textColor,
    required Color strokeColor,
    required double strokeWidth,
  }) {
    return Stack(
      alignment: Alignment.center,
      children: [
        // 1. Black outer stroke
        Text(
          text,
          textAlign: TextAlign.center,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: fontSize,
            fontWeight: fontWeight,
            height: 1.25,
            letterSpacing: 0.3,
            foreground: Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = strokeWidth
              ..strokeCap = StrokeCap.round
              ..strokeJoin = StrokeJoin.round
              ..color = strokeColor,
          ),
        ),

        // 2. Crisp foreground fill with ambient shadows
        Text(
          text,
          textAlign: TextAlign.center,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: fontSize,
            fontWeight: fontWeight,
            height: 1.25,
            letterSpacing: 0.3,
            color: textColor,
            shadows: const [
              Shadow(
                blurRadius: 8.0,
                color: Colors.black,
                offset: Offset(0, 2),
              ),
              Shadow(
                blurRadius: 18.0,
                color: Colors.black87,
                offset: Offset(0, 4),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
