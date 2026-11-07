import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../config/languages.dart';
import '../models/translation_turn.dart';

/// A modern, glassmorphic card presenting real-time source speech and live translation.
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
    final targetLang = SupportedLanguages.findByCode(turn.targetLanguageCode);

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 8, horizontal: 12),
      decoration: BoxDecoration(
        color: const Color(0xFF131B2B).withOpacity(isLive ? 0.75 : 0.5),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: isLive ? const Color(0xFF00E5FF).withOpacity(0.4) : Colors.white.withOpacity(0.08),
          width: isLive ? 1.5 : 1.0,
        ),
        boxShadow: isLive
            ? [
                BoxShadow(
                  color: const Color(0xFF00E5FF).withOpacity(0.12),
                  blurRadius: 16,
                  spreadRadius: 2,
                )
              ]
            : [],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(16),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Top status bar with language badges and time
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: Colors.white.withOpacity(0.08),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Row(
                        children: [
                          const Icon(Icons.mic, size: 13, color: Colors.white70),
                          const SizedBox(width: 4),
                          Text(
                            '原始語音',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w600,
                              color: Colors.white.withOpacity(0.7),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 8),
                    const Icon(Icons.arrow_forward_rounded, size: 14, color: Colors.white38),
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: const Color(0xFF00E5FF).withOpacity(0.15),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        '${targetLang.flag} ${targetLang.nameZh}',
                        style: const TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.w700,
                          color: Color(0xFF00E5FF),
                        ),
                      ),
                    ),
                    const Spacer(),
                    if (isLive)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                        decoration: BoxDecoration(
                          color: Colors.redAccent.withOpacity(0.2),
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
                          color: Colors.white.withOpacity(0.4),
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
                        color: Colors.white.withOpacity(0.5),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),

                // Source speech text
                Text(
                  turn.sourceText.isNotEmpty
                      ? turn.sourceText
                      : (isLive ? '正在聆聽輸入音訊...' : '無語音內容'),
                  style: TextStyle(
                    fontSize: 14,
                    fontStyle: turn.sourceText.isEmpty ? FontStyle.italic : FontStyle.normal,
                    color: Colors.white.withOpacity(turn.sourceText.isEmpty ? 0.4 : 0.75),
                    height: 1.4,
                  ),
                ),
                const Divider(height: 20, color: Colors.white10),

                // Translated speech text
                Text(
                  turn.targetText.isNotEmpty
                      ? turn.targetText
                      : (isLive ? '正在即時翻譯中...' : '無翻譯內容'),
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w600,
                    color: turn.targetText.isNotEmpty ? Colors.white : Colors.white38,
                    height: 1.45,
                    shadows: isLive && turn.targetText.isNotEmpty
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
