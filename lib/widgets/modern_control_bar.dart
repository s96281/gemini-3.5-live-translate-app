import 'dart:ui';
import 'package:flutter/material.dart';

import '../models/live_config.dart';

/// A floating frosted-glass control pill for instant recording, language switching,
/// audio source toggling (system audio vs mic), TTS toggling, and settings.
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
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0xFF101726).withOpacity(0.85),
        borderRadius: BorderRadius.circular(32),
        border: Border.all(
          color: Colors.white.withOpacity(0.12),
          width: 1.2,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.4),
            blurRadius: 24,
            offset: const Offset(0, 8),
          ),
          if (isRecording)
            BoxShadow(
              color: const Color(0xFF00E5FF).withOpacity(0.2),
              blurRadius: 20,
              spreadRadius: 2,
            ),
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(32),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Language switch pill button
              InkWell(
                onTap: onSelectLanguage,
                borderRadius: BorderRadius.circular(20),
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.06),
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(color: Colors.white10),
                  ),
                  child: Row(
                    children: [
                      const Icon(Icons.translate_rounded, size: 16, color: Color(0xFF00E5FF)),
                      const SizedBox(width: 6),
                      Text(
                        targetLanguageDisplay,
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(width: 4),
                      const Icon(Icons.arrow_drop_down, size: 16, color: Colors.white70),
                    ],
                  ),
                ),
              ),
              const SizedBox(width: 8),

              // Audio Source Switch Pill (System Audio vs Microphone)
              Tooltip(
                message: audioSourceMode == AudioSourceMode.systemAudio
                    ? '音訊來源：裝置系統音效 (點擊切換為麥克風)'
                    : '音訊來源：實體麥克風 (點擊切換為系統音效)',
                child: InkWell(
                  onTap: onToggleAudioSource,
                  borderRadius: BorderRadius.circular(20),
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                    decoration: BoxDecoration(
                      color: audioSourceMode == AudioSourceMode.systemAudio
                          ? const Color(0xFF00E5FF).withOpacity(0.15)
                          : const Color(0xFF7928CA).withOpacity(0.15),
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(
                        color: audioSourceMode == AudioSourceMode.systemAudio
                            ? const Color(0xFF00E5FF).withOpacity(0.5)
                            : const Color(0xFF7928CA).withOpacity(0.5),
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          audioSourceMode == AudioSourceMode.systemAudio
                              ? Icons.speaker_rounded
                              : Icons.mic_rounded,
                          size: 15,
                          color: audioSourceMode == AudioSourceMode.systemAudio
                              ? const Color(0xFF00E5FF)
                              : const Color(0xFFD946EF),
                        ),
                        const SizedBox(width: 4),
                        Text(
                          audioSourceMode == AudioSourceMode.systemAudio ? '系統音效' : '麥克風',
                          style: TextStyle(
                            color: audioSourceMode == AudioSourceMode.systemAudio
                                ? const Color(0xFF00E5FF)
                                : const Color(0xFFE2B8FF),
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 8),

              // Clear history button
              IconButton(
                icon: const Icon(Icons.delete_outline_rounded, size: 20),
                tooltip: '清除字幕記錄',
                color: Colors.white60,
                onPressed: onClearHistory,
              ),

              const SizedBox(width: 4),

              // Main Recording Floating Button
              _RecordButton(
                isRecording: isRecording,
                audioSourceMode: audioSourceMode,
                onTap: onToggleRecording,
              ),

              const SizedBox(width: 4),

              // TTS Mute / Unmute Button
              IconButton(
                icon: Icon(
                  isMuted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                  size: 20,
                ),
                tooltip: isMuted ? '已靜音 (點擊開啟語音朗讀)' : '朗讀中 (點擊靜音)',
                color: isMuted ? Colors.white38 : const Color(0xFF00E5FF),
                onPressed: onToggleMute,
              ),

              const SizedBox(width: 4),

              // Settings button
              IconButton(
                icon: const Icon(Icons.settings_outlined, size: 20),
                tooltip: 'API Key 與設定',
                color: Colors.white70,
                onPressed: onOpenSettings,
              ),
            ],
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
        final scale = widget.isRecording ? 1.0 + (_pulseController.value * 0.1) : 1.0;
        final glow = widget.isRecording
            ? BoxShadow(
                color: Colors.redAccent.withOpacity(0.5 * _pulseController.value + 0.3),
                blurRadius: 18 * scale,
                spreadRadius: 2,
              )
            : const BoxShadow(
                color: Color(0x3300E5FF),
                blurRadius: 10,
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
                  width: 52,
                  height: 52,
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
                    size: 26,
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
