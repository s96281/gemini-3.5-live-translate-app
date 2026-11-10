import 'dart:convert';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:window_manager/window_manager.dart';
import 'package:hotkey_manager/hotkey_manager.dart';
import 'package:desktop_multi_window/desktop_multi_window.dart';

import '../services/window_service.dart';

/// Windows Desktop Floating Subtitle Window.
/// Features:
/// - Frameless, 100% transparent background, Always-on-Top.
/// - Mouse click-through (pass-through / ignoreMouseEvents) toggleable via global hotkey (Ctrl + F8).
/// - High-contrast text stroke & dual drop shadows for 100% legibility on any wallpaper / screen background.
/// - Inter-Process Communication (IPC) via desktop_multi_window to receive live transcript & translation stream.
class SubtitleOverlayWindow extends StatefulWidget {
  final int windowId;
  final Map<String, dynamic> initialArguments;

  const SubtitleOverlayWindow({
    super.key,
    required this.windowId,
    required this.initialArguments,
  });

  @override
  State<SubtitleOverlayWindow> createState() => _SubtitleOverlayWindowState();
}

class _SubtitleOverlayWindowState extends State<SubtitleOverlayWindow> {
  // Live Subtitle Content
  String _sourceText = '等待語音辨識中...';
  String _translatedText = 'Gemini 3.5 即時口譯已就緒';
  bool _isFinal = false;
  String _targetLangCode = 'zh-TW';

  // Window State
  bool _isClickThrough = false;
  double _fontSize = 22.0;

  // Global Hotkey for unlocking click-through mode: Ctrl + F8
  final HotKey _toggleLockHotKey = HotKey(
    key: PhysicalKeyboardKey.f8,
    modifiers: [HotKeyModifier.control],
    scope: HotKeyScope.system, // System-wide, active even when unfocused
  );

  @override
  void initState() {
    super.initState();
    _initWindow();
    _initHotKeys();
    _setupIpcListener();

    if (widget.initialArguments.isNotEmpty) {
      _sourceText = widget.initialArguments['sourceText'] ?? _sourceText;
      _translatedText = widget.initialArguments['translatedText'] ?? _translatedText;
      _targetLangCode = widget.initialArguments['targetLangCode'] ?? _targetLangCode;
    }
  }

  Future<void> _initWindow() async {
    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      try {
        await WindowService.configureSubwindowAsOverlay();
      } catch (e) {
        debugPrint('[SubtitleOverlayWindow] configureSubwindowAsOverlay error: $e');
      }
      try {
        await windowManager.ensureInitialized();
        await windowManager.setAsFrameless();
        await windowManager.setBackgroundColor(Colors.transparent);
        await windowManager.setAlwaysOnTop(true);
        await windowManager.setResizable(true);
      } catch (e) {
        debugPrint('[SubtitleOverlayWindow] window_manager init error: $e');
      }
    }
  }

  Future<void> _initHotKeys() async {
    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      try {
        await hotKeyManager.unregisterAll();
        await hotKeyManager.register(
          _toggleLockHotKey,
          keyDownHandler: (hotKey) => _toggleClickThrough(),
        );
      } catch (e) {
        debugPrint('[SubtitleOverlayWindow] hotkey register error: $e');
      }
    }
  }

  /// Toggles mouse events pass-through (Click-through mode).
  Future<void> _toggleClickThrough() async {
    final nextState = !_isClickThrough;
    setState(() {
      _isClickThrough = nextState;
    });

    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      // 1. Direct native Win32 subwindow style update
      await WindowService.setSubwindowIgnoreMouseEvents(nextState);

      // 2. window_manager fallback if available
      try {
        await windowManager.setIgnoreMouseEvents(nextState, forward: false);
      } catch (_) {}

      // 3. Notify the main window of the lock state change via IPC
      try {
        await DesktopMultiWindow.invokeMethod(
          0, // Main window ID
          'onSubtitleLockChanged',
          nextState,
        );
      } catch (_) {}
    }
  }

  /// Listens to IPC messages from the main window.
  void _setupIpcListener() {
    try {
      DesktopMultiWindow.setMethodHandler((call, fromWindowId) async {
        if (call.method == 'updateSubtitle') {
          final data = call.arguments is String
              ? jsonDecode(call.arguments as String)
              : Map<String, dynamic>.from(call.arguments as Map);

          if (mounted) {
            setState(() {
              _sourceText = data['sourceText'] ?? '';
              _translatedText = data['translatedText'] ?? '';
              _isFinal = data['isFinal'] ?? false;
              if (data['targetLangCode'] != null) {
                _targetLangCode = data['targetLangCode'];
              }
            });
          }
          return true;
        } else if (call.method == 'setClickThrough') {
          final bool enable = call.arguments as bool;
          if (_isClickThrough != enable) {
            _toggleClickThrough();
          }
          return true;
        } else if (call.method == 'setFontSize') {
          final double size = (call.arguments as num).toDouble();
          setState(() {
            _fontSize = size;
          });
          return true;
        } else if (call.method == 'closeSubtitleWindow') {
          if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
            await windowManager.close();
          }
          return true;
        }
        return null;
      });
    } catch (e) {
      debugPrint('[SubtitleOverlayWindow] IPC setup error: $e');
    }
  }

  @override
  void dispose() {
    try {
      hotKeyManager.unregister(_toggleLockHotKey);
    } catch (_) {}
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Top-level must have transparent background for Windows DWM composition
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: GestureDetector(
        behavior: HitTestBehavior.translucent,
        onPanStart: (_) => WindowService.startDraggingSubwindow(),
        child: DragToMoveArea(
          child: Container(
          width: double.infinity,
          height: double.infinity,
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          decoration: BoxDecoration(
            // When interactive: subtle translucent capsule so user sees draggable bounds.
            // When click-through: completely transparent for pure desktop floating lyrics look.
            color: _isClickThrough
                ? Colors.transparent
                : Colors.black.withOpacity(0.35),
            borderRadius: BorderRadius.circular(16),
            border: _isClickThrough
                ? null
                : Border.all(color: Colors.white.withOpacity(0.18), width: 1.0),
          ),
          child: Column(
            mainAxisSize: MainAxisSize.max,
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              // Top control handle (Hidden during click-through to avoid visual clutter)
              if (!_isClickThrough) _buildTopToolbar(),

              const Spacer(),

              // Primary Translated Subtitle (Prominent, High-contrast stroke)
              _buildStrokedText(
                text: _translatedText.isNotEmpty ? _translatedText : '等待翻譯語音...',
                fontSize: _fontSize,
                fontWeight: FontWeight.w700,
                textColor: const Color(0xFF64FFDA), // Neon Cyan / Mint
                strokeColor: Colors.black.withOpacity(0.95),
                strokeWidth: 3.5,
              ),

              const SizedBox(height: 4),

              // Secondary Source Recognition Subtitle
              if (_sourceText.isNotEmpty)
                _buildStrokedText(
                  text: _sourceText,
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
    ),
    );
  }

  Widget _buildTopToolbar() {
    return Row(
      children: [
        // Drag Indicator
        const Icon(Icons.drag_indicator_rounded, size: 16, color: Colors.white60),
        const SizedBox(width: 4),
        Text(
          '拖曳移動 | [Ctrl + F8] 切換穿透模式',
          style: TextStyle(
            color: Colors.white.withOpacity(0.75),
            fontSize: 11,
            fontWeight: FontWeight.w500,
          ),
        ),
        const Spacer(),

        // Font Size Adjuster
        IconButton(
          icon: const Icon(Icons.format_size_rounded, size: 15, color: Colors.white70),
          tooltip: '縮放字幕字體 ($_fontSize px)',
          visualDensity: VisualDensity.compact,
          padding: EdgeInsets.zero,
          constraints: const BoxConstraints(minWidth: 24, minHeight: 24),
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
        const SizedBox(width: 6),

        // Lock & Click-Through Button
        IconButton(
          icon: const Icon(Icons.lock_open_rounded, size: 16, color: Color(0xFF64FFDA)),
          tooltip: '鎖定並啟用滑鼠穿透 (Ctrl + F8 解鎖)',
          visualDensity: VisualDensity.compact,
          padding: EdgeInsets.zero,
          constraints: const BoxConstraints(minWidth: 24, minHeight: 24),
          onPressed: _toggleClickThrough,
        ),
        const SizedBox(width: 6),

        // Close Subtitle Window Button
        IconButton(
          icon: const Icon(Icons.close_rounded, size: 16, color: Colors.white70),
          tooltip: '關閉懸浮字幕視窗',
          visualDensity: VisualDensity.compact,
          padding: EdgeInsets.zero,
          constraints: const BoxConstraints(minWidth: 24, minHeight: 24),
          onPressed: () async {
            try {
              await DesktopMultiWindow.invokeMethod(0, 'onSubtitleWindowClosed');
            } catch (_) {}
            if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
              await windowManager.close();
            }
          },
        ),
      ],
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
