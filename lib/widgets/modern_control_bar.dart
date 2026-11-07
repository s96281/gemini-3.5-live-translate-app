import 'dart:ui';
import 'package:flutter/material.dart';

import '../models/live_config.dart';

/// A floating frosted-glass responsive control pill for instant recording, language switching,
/// audio source toggling (system audio vs mic), TTS toggling, and settings.
/// Adapts gracefully to both Dark and Light themes and never truncates on mobile screens.
class ModernControlBar extends StatelessWidget {
  final bool isRecording;
  final bool isMuted;
  final String targetLanguageDisplay;
  final AudioSourceMode audioSourceMode;
  final VoidCallback onToggleRecording;
  final VoidCallback onToggleMute;
  final VoidCallback onSelectLanguage;
  final VoidCallback onToggleAudioSource;
  final VoidCallback onClearHistory;
  final VoidCallback onOpenSettings;

  const ModernControlBar({
    super.key,
    required this.isRecording,
    required this.isMuted,
    required this.targetLanguageDisplay,
    required this.audioSourceMode,
    required this.onToggleRecording,
    required this.onToggleMute,
    required this.onSelectLanguage,
    required this.onToggleAudioSource,
    required this.onClearHistory,
    required this.onOpenSettings,
  });

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final screenWidth = MediaQuery.of(context).size.width;

    // Theme-driven colors
    final barBgColor = isDark
        ? const Color(0xFF101726).withOpacity(0.88)
        : Colors.white.withOpacity(0.92);
    final borderColor = isDark
        ? Colors.white.withOpacity(0.12)
        : const Color(0xFFCBD5E1);
    final shadowColor = isDark
        ? Colors.black.withOpacity(0.4)
        : const Color(0x200F172A);
    final iconColor = isDark ? Colors.white70 : const Color(0xFF475569);
    final iconColorMuted = isDark ? Colors.white38 : const Color(0xFF94A3B8);
    final cyanPrimary = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);

    return Container(
      margin: EdgeInsets.symmetric(
        horizontal: screenWidth < 380 ? 8 : 14,
        vertical: 10,
      ),
      decoration: BoxDecoration(
        color: barBgColor,
        borderRadius: BorderRadius.circular(36),
        border: Border.all(color: borderColor, width: 1.2),
        boxShadow: [
          BoxShadow(
            color: shadowColor,
            blurRadius: 20,
            offset: const Offset(0, 6),
          ),
          if (isRecording)
            BoxShadow(
              color: cyanPrimary.withOpacity(0.25),
              blurRadius: 18,
              spreadRadius: 2,
            ),
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(36),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
            child: FittedBox(
              fit: BoxFit.scaleDown,
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  // 1. Language switch pill button
                  InkWell(
                    onTap: onSelectLanguage,
                    borderRadius: BorderRadius.circular(20),
                    child: Container(
                      constraints: const BoxConstraints(maxWidth: 135),
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
                      decoration: BoxDecoration(
                        color: isDark
                            ? Colors.white.withOpacity(0.06)
                            : const Color(0xFFF1F5F9),
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(
                          color: isDark ? Colors.white10 : const Color(0xFFE2E8F0),
                        ),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(Icons.translate_rounded, size: 15, color: cyanPrimary),
                          const SizedBox(width: 5),
                          Flexible(
                            child: Text(
                              targetLanguageDisplay,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                color: isDark ? Colors.white : const Color(0xFF0F172A),
                                fontSize: 12,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                          const SizedBox(width: 2),
                          Icon(
                            Icons.arrow_drop_down,
                            size: 16,
                            color: isDark ? Colors.white70 : const Color(0xFF64748B),
                          ),
                        ],
                      ),
                    ),
                  ),

                  const SizedBox(width: 6),

                  // 2. Audio Source Switch Pill (System Audio vs Microphone)
                  Tooltip(
                    message: audioSourceMode == AudioSourceMode.systemAudio
                        ? '音訊來源：裝置系統音效 (點擊切換為麥克風)'
                        : '音訊來源：實體麥克風 (點擊切換為系統音效)',
                    child: InkWell(
                      onTap: onToggleAudioSource,
                      borderRadius: BorderRadius.circular(20),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 7),
                        decoration: BoxDecoration(
                          color: audioSourceMode == AudioSourceMode.systemAudio
                              ? (isDark
                                  ? const Color(0xFF00E5FF).withOpacity(0.15)
                                  : const Color(0xFFE0F2FE))
                              : (isDark
                                  ? const Color(0xFF7928CA).withOpacity(0.15)
                                  : const Color(0xFFF3E8FF)),
                          borderRadius: BorderRadius.circular(20),
                          border: Border.all(
                            color: audioSourceMode == AudioSourceMode.systemAudio
                                ? (isDark
                                    ? const Color(0xFF00E5FF).withOpacity(0.4)
                                    : const Color(0xFFBAE6FD))
                                : (isDark
                                    ? const Color(0xFF7928CA).withOpacity(0.4)
                                    : const Color(0xFFDDD6FE)),
                          ),
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(
                              audioSourceMode == AudioSourceMode.systemAudio
                                  ? Icons.speaker_rounded
                                  : Icons.mic_rounded,
                              size: 14,
                              color: audioSourceMode == AudioSourceMode.systemAudio
                                  ? cyanPrimary
                                  : (isDark ? const Color(0xFFD946EF) : const Color(0xFF7C3AED)),
                            ),
                            const SizedBox(width: 4),
                            Text(
                              audioSourceMode == AudioSourceMode.systemAudio ? '系統音效' : '麥克風',
                              style: TextStyle(
                                color: audioSourceMode == AudioSourceMode.systemAudio
                                    ? cyanPrimary
                                    : (isDark ? const Color(0xFFE2B8FF) : const Color(0xFF7C3AED)),
                                fontSize: 11,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),

                  const SizedBox(width: 4),

                  // 3. Clear history button
                  IconButton(
                    icon: const Icon(Icons.delete_outline_rounded, size: 19),
                    tooltip: '清除字幕記錄',
                    visualDensity: VisualDensity.compact,
                    padding: const EdgeInsets.all(6),
                    constraints: const BoxConstraints(minWidth: 34, minHeight: 34),
                    color: iconColor,
                    onPressed: onClearHistory,
                  ),

                  const SizedBox(width: 2),

                  // 4. Main Recording / Translating Core Button
                  _RecordButton(
                    isRecording: isRecording,
                    audioSourceMode: audioSourceMode,
                    onTap: onToggleRecording,
                  ),

                  const SizedBox(width: 2),

                  // 5. TTS Mute / Unmute Button
                  IconButton(
                    icon: Icon(
                      isMuted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                      size: 19,
                    ),
                    tooltip: isMuted ? '已靜音 (點擊開啟語音朗讀)' : '朗讀中 (點擊靜音)',
                    visualDensity: VisualDensity.compact,
                    padding: const EdgeInsets.all(6),
                    constraints: const BoxConstraints(minWidth: 34, minHeight: 34),
                    color: isMuted ? iconColorMuted : cyanPrimary,
                    onPressed: onToggleMute,
                  ),

                  const SizedBox(width: 2),

                  // 6. Settings button
                  IconButton(
                    icon: const Icon(Icons.settings_outlined, size: 19),
                    tooltip: 'API Key 與設定',
                    visualDensity: VisualDensity.compact,
                    padding: const EdgeInsets.all(6),
                    constraints: const BoxConstraints(minWidth: 34, minHeight: 34),
                    color: iconColor,
                    onPressed: onOpenSettings,
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

class _RecordButton extends StatefulWidget {
  final bool isRecording;
  final AudioSourceMode audioSourceMode;
  final VoidCallback onTap;

  const _RecordButton({
    required this.isRecording,
    required this.audioSourceMode,
    required this.onTap,
  });

  @override
  State<_RecordButton> createState() => _RecordButtonState();
}

class _RecordButtonState extends State<_RecordButton>
    with SingleTickerProviderStateMixin {
  late AnimationController _pulseController;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1400),
    )..repeat(reverse: true);
  }

  @override
  void dispose() {
    _pulseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _pulseController,
      builder: (context, child) {
        final scale = widget.isRecording ? 1.0 + (_pulseController.value * 0.08) : 1.0;
        final glow = widget.isRecording
            ? BoxShadow(
                color: Colors.redAccent.withOpacity(0.5 * _pulseController.value + 0.3),
                blurRadius: 16 * scale,
                spreadRadius: 2,
              )
            : const BoxShadow(
                color: Color(0x3300E5FF),
                blurRadius: 8,
                spreadRadius: 1,
              );

        return Transform.scale(
          scale: scale,
          child: Container(
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              boxShadow: [glow],
            ),
            child: Material(
              color: Colors.transparent,
              child: InkWell(
                onTap: widget.onTap,
                customBorder: const CircleBorder(),
                child: Container(
                  width: 48,
                  height: 48,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: LinearGradient(
                      colors: widget.isRecording
                          ? [const Color(0xFFFF416C), const Color(0xFFFF4B2B)]
                          : (widget.audioSourceMode == AudioSourceMode.systemAudio
                              ? [const Color(0xFF00E5FF), const Color(0xFF0072FF)]
                              : [const Color(0xFF9D4EDD), const Color(0xFF7209B7)]),
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                    ),
                  ),
                  child: Icon(
                    widget.isRecording
                        ? Icons.stop_rounded
                        : (widget.audioSourceMode == AudioSourceMode.systemAudio
                            ? Icons.speaker_rounded
                            : Icons.mic_rounded),
                    color: Colors.white,
                    size: 24,
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}
