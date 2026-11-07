import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../config/languages.dart';
import '../models/translation_turn.dart';

/// A modern, glassmorphic card presenting real-time source speech and live translation.
/// Dynamically themes between Dark and Light appearances.
class SubtitleCard extends StatelessWidget {
  final TranslationTurn turn;
  final bool isLive;

  const SubtitleCard({
    super.key,
    required this.turn,
    this.isLive = false,
  });

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final targetLang = SupportedLanguages.findByCode(turn.targetLanguageCode);

    // Theme-sensitive styles
    final cardBg = isDark
        ? const Color(0xFF131B2B).withOpacity(isLive ? 0.85 : 0.6)
        : Colors.white.withOpacity(isLive ? 0.95 : 0.85);

    final cyanColor = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);
    final borderColor = isLive
        ? cyanColor.withOpacity(0.5)
        : (isDark ? Colors.white.withOpacity(0.08) : const Color(0xFFE2E8F0));

    final shadowColor = isLive
        ? cyanColor.withOpacity(isDark ? 0.15 : 0.12)
        : (isDark ? Colors.black.withOpacity(0.2) : const Color(0x100F172A));

    final sourceTextColor = isDark
        ? Colors.white.withOpacity(turn.sourceText.isEmpty ? 0.4 : 0.75)
        : (turn.sourceText.isEmpty ? const Color(0xFF94A3B8) : const Color(0xFF475569));

    final targetTextColor = turn.targetText.isNotEmpty
        ? (isDark ? Colors.white : const Color(0xFF0F172A))
        : (isDark ? Colors.white38 : const Color(0xFF94A3B8));

    final badgeBg = isDark ? Colors.white.withOpacity(0.08) : const Color(0xFFF1F5F9);
    final badgeText = isDark ? Colors.white70 : const Color(0xFF475569);
    final dividerColor = isDark ? Colors.white10 : const Color(0xFFF1F5F9);

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 6, horizontal: 12),
      decoration: BoxDecoration(
        color: cardBg,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: borderColor, width: isLive ? 1.5 : 1.0),
        boxShadow: [
          BoxShadow(
            color: shadowColor,
            blurRadius: isLive ? 16 : 8,
            offset: const Offset(0, 3),
            spreadRadius: isLive ? 1 : 0,
          )
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(16),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Top status bar with language badges and time
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: badgeBg,
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Row(
                        children: [
                          Icon(Icons.mic, size: 13, color: badgeText),
                          const SizedBox(width: 4),
                          Text(
                            '原始語音',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w600,
                              color: badgeText,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 6),
                    Icon(
                      Icons.arrow_forward_rounded,
                      size: 13,
                      color: isDark ? Colors.white38 : const Color(0xFF94A3B8),
                    ),
                    const SizedBox(width: 6),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: isDark
                            ? const Color(0xFF00E5FF).withOpacity(0.15)
                            : const Color(0xFFE0F2FE),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        '${targetLang.flag} ${targetLang.nameZh}',
                        style: TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.w700,
                          color: cyanColor,
                        ),
                      ),
                    ),
                    const Spacer(),
                    if (isLive)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                        decoration: BoxDecoration(
                          color: Colors.redAccent.withOpacity(0.15),
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: Colors.redAccent.withOpacity(0.4)),
                        ),
                        child: Row(
                          children: [
                            Container(
                              width: 6,
                              height: 6,
                              decoration: const BoxDecoration(
                                color: Colors.redAccent,
                                shape: BoxShape.circle,
                              ),
                            ),
                            const SizedBox(width: 5),
                            const Text(
                              '即時',
                              style: TextStyle(
                                fontSize: 10,
                                fontWeight: FontWeight.bold,
                                color: Colors.redAccent,
                              ),
                            ),
                          ],
                        ),
                      )
                    else
                      Text(
                        _formatTime(turn.timestamp),
                        style: TextStyle(
                          fontSize: 11,
                          color: isDark ? Colors.white38 : const Color(0xFF94A3B8),
                        ),
                      ),
                    const SizedBox(width: 8),
                    // Copy button
                    InkWell(
                      onTap: () {
                        Clipboard.setData(ClipboardData(
                          text: '原文: ${turn.sourceText}\n譯文: ${turn.targetText}',
                        ));
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(
                            content: Text('已複製到剪貼簿'),
                            duration: Duration(seconds: 1),
                            behavior: SnackBarBehavior.floating,
                          ),
                        );
                      },
                      child: Icon(
                        Icons.copy_rounded,
                        size: 15,
                        color: isDark ? Colors.white.withOpacity(0.5) : const Color(0xFF94A3B8),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),

                // Source speech text
                Text(
                  turn.sourceText.isNotEmpty
                      ? turn.sourceText
                      : (isLive ? '正在聆聽輸入音訊...' : '無語音內容'),
                  style: TextStyle(
                    fontSize: 14,
                    fontStyle: turn.sourceText.isEmpty ? FontStyle.italic : FontStyle.normal,
                    color: sourceTextColor,
                    height: 1.4,
                  ),
                ),
                Divider(height: 18, color: dividerColor),

                // Translated speech text
                Text(
                  turn.targetText.isNotEmpty
                      ? turn.targetText
                      : (isLive ? '正在即時翻譯中...' : '無翻譯內容'),
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w600,
                    color: targetTextColor,
                    height: 1.45,
                    shadows: isLive && turn.targetText.isNotEmpty && isDark
                        ? [
                            Shadow(
                              color: const Color(0xFF00E5FF).withOpacity(0.3),
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
      ),
    );
  }

  String _formatTime(DateTime dt) {
    final h = dt.hour.toString().padLeft(2, '0');
    final m = dt.minute.toString().padLeft(2, '0');
    final s = dt.second.toString().padLeft(2, '0');
    return '$h:$m:$s';
  }
}
