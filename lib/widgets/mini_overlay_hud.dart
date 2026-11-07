import 'dart:ui';
import 'package:flutter/material.dart';

import '../config/languages.dart';
import '../models/live_config.dart';
import '../models/translation_turn.dart';
import '../services/gemini_live_client.dart';

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
  double _opacity = 0.92;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final targetLang = SupportedLanguages.findByCode(widget.config.targetLanguageCode);
    final cyanPrimary = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);

    // Get latest display text: either from active current turn or most recent history turn
    String sourceText = widget.currentTurn?.sourceText ?? '';
    String targetText = widget.currentTurn?.targetText ?? '';
    if (sourceText.isEmpty && targetText.isEmpty && widget.history.isNotEmpty) {
      sourceText = widget.history.first.sourceText;
      targetText = widget.history.first.targetText;
    }

    final hudBg = isDark
        ? const Color(0xFF0A0F1D).withOpacity(_opacity)
        : Colors.white.withOpacity(_opacity);
    final borderColor = isDark
        ? (widget.isRecording ? cyanPrimary.withOpacity(0.5) : Colors.white12)
        : (widget.isRecording ? cyanPrimary.withOpacity(0.5) : const Color(0xFFCBD5E1));

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        width: double.infinity,
        height: double.infinity,
        decoration: BoxDecoration(
          color: hudBg,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: borderColor, width: 1.2),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withOpacity(isDark ? 0.5 : 0.15),
              blurRadius: 18,
              offset: const Offset(0, 4),
            )
          ],
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(16),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Top Minimalist Toolbar
                  Row(
                    children: [
                      // Status Dot
                      Container(
                        width: 7,
                        height: 7,
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
                        '即時口譯浮窗',
                        style: TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.bold,
                          color: isDark ? Colors.white70 : const Color(0xFF475569),
                        ),
                      ),
                      const SizedBox(width: 8),

                      // Language pill
                      InkWell(
                        onTap: widget.onSelectLanguage,
                        borderRadius: BorderRadius.circular(10),
                        child: Container(
                          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                          decoration: BoxDecoration(
                            color: cyanPrimary.withOpacity(0.12),
                            borderRadius: BorderRadius.circular(10),
                          ),
                          child: Text(
                            '${targetLang.flag} ${targetLang.nameZh}',
                            style: TextStyle(
                              fontSize: 10,
                              fontWeight: FontWeight.w700,
                              color: cyanPrimary,
                            ),
                          ),
                        ),
                      ),

                      const Spacer(),

                      // Recording Play/Stop Mini Button
                      IconButton(
                        icon: Icon(
                          widget.isRecording ? Icons.stop_circle_rounded : Icons.play_circle_fill_rounded,
                          size: 19,
                        ),
                        visualDensity: VisualDensity.compact,
                        padding: EdgeInsets.zero,
                        constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                        color: widget.isRecording ? Colors.redAccent : cyanPrimary,
                        tooltip: widget.isRecording ? '停止翻譯' : '開始翻譯',
                        onPressed: widget.onToggleRecording,
                      ),

                      // TTS Mute toggle
                      IconButton(
                        icon: Icon(
                          widget.isMuted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                          size: 17,
                        ),
                        visualDensity: VisualDensity.compact,
                        padding: EdgeInsets.zero,
                        constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                        color: widget.isMuted
                            ? (isDark ? Colors.white38 : const Color(0xFF94A3B8))
                            : cyanPrimary,
                        tooltip: widget.isMuted ? '已靜音' : '朗讀中',
                        onPressed: widget.onToggleMute,
                      ),

                      // Opacity cycle (100% -> 75% -> 50%)
                      IconButton(
                        icon: const Icon(Icons.opacity_rounded, size: 16),
                        visualDensity: VisualDensity.compact,
                        padding: EdgeInsets.zero,
                        constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                        color: isDark ? Colors.white60 : const Color(0xFF64748B),
                        tooltip: '切換浮窗透明度 (${(_opacity * 100).toInt()}%)',
                        onPressed: () {
                          setState(() {
                            if (_opacity >= 0.9) {
                              _opacity = 0.65;
                            } else if (_opacity >= 0.6) {
                              _opacity = 0.45;
                            } else {
                              _opacity = 0.92;
                            }
                          });
                        },
                      ),

                      // Restore to main window button
                      IconButton(
                        icon: const Icon(Icons.open_in_full_rounded, size: 16),
                        visualDensity: VisualDensity.compact,
                        padding: EdgeInsets.zero,
                        constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                        color: isDark ? Colors.white : const Color(0xFF0F172A),
                        tooltip: '還原回完整視窗',
                        onPressed: widget.onExitOverlay,
                      ),
                    ],
                  ),

                  const SizedBox(height: 6),

                  // Subtitles Content
                  Expanded(
                    child: SingleChildScrollView(
                      physics: const BouncingScrollPhysics(),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          if (sourceText.isNotEmpty)
                            Text(
                              sourceText,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                fontSize: 11,
                                height: 1.3,
                                color: isDark ? Colors.white54 : const Color(0xFF64748B),
                              ),
                            ),
                          const SizedBox(height: 2),
                          Text(
                            targetText.isNotEmpty
                                ? targetText
                                : (widget.isRecording ? '聆聽音訊中...' : '浮窗待命中，點擊上方播放按鈕開始'),
                            style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              height: 1.35,
                              color: targetText.isNotEmpty
                                  ? (isDark ? Colors.white : const Color(0xFF0F172A))
                                  : (isDark ? Colors.white38 : const Color(0xFF94A3B8)),
                              shadows: targetText.isNotEmpty && isDark
                                  ? [
                                      Shadow(
                                        color: cyanPrimary.withOpacity(0.35),
                                        blurRadius: 8,
                                      )
                                    ]
                                  : null,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
