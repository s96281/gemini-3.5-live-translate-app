import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:desktop_multi_window/desktop_multi_window.dart';
import 'package:hotkey_manager/hotkey_manager.dart';
import 'window_service.dart';

/// Service managing the lifecycle and IPC streaming communication
/// between the Primary Application Window and the Floating Subtitle Window.
class SubtitleOverlayManager {
  static final SubtitleOverlayManager instance = SubtitleOverlayManager._internal();
  SubtitleOverlayManager._internal();

  int? _subtitleWindowId;
  final ValueNotifier<bool> isOverlayActive = ValueNotifier<bool>(false);
  final ValueNotifier<bool> isClickThroughLocked = ValueNotifier<bool>(false);

  bool get isOpen => _subtitleWindowId != null;

  final HotKey _toggleLockHotKey = HotKey(
    key: PhysicalKeyboardKey.f8,
    modifiers: [HotKeyModifier.control],
    scope: HotKeyScope.system,
  );

  /// Initializes IPC message listeners from the child subtitle window.
  void initMainIpcHandler() {
    if (!Platform.isWindows && !Platform.isLinux && !Platform.isMacOS) return;

    try {
      DesktopMultiWindow.setMethodHandler((call, fromWindowId) async {
        if (call.method == 'onSubtitleLockChanged') {
          final isLocked = call.arguments as bool;
          isClickThroughLocked.value = isLocked;
          debugPrint('[SubtitleOverlayManager] Subtitle lock changed: $isLocked');
          return true;
        } else if (call.method == 'setSubwindowIgnoreMouseEvents') {
          final bool enable = call.arguments as bool;
          isClickThroughLocked.value = enable;
          await WindowService.setSubwindowIgnoreMouseEvents(enable);
          return true;
        } else if (call.method == 'moveSubwindow') {
          if (call.arguments is Map) {
            final args = call.arguments as Map;
            final double dx = (args['dx'] as num?)?.toDouble() ?? 0.0;
            final double dy = (args['dy'] as num?)?.toDouble() ?? 0.0;
            await WindowService.moveSubwindow(dx, dy);
          }
          return true;
        } else if (call.method == 'startDraggingSubwindow') {
          await WindowService.startDraggingSubwindow();
          return true;
        } else if (call.method == 'closeSubtitleWindow' || call.method == 'onSubtitleWindowClosed') {
          await closeSubtitleWindow();
          debugPrint('[SubtitleOverlayManager] Subtitle window was closed.');
          return true;
        }
        return null;
      });
    } catch (e) {
      debugPrint('[SubtitleOverlayManager] initMainIpcHandler error: $e');
    }
  }

  Future<void> initHotKey() async {
    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      try {
        await hotKeyManager.unregister(_toggleLockHotKey);
      } catch (_) {}
      try {
        await hotKeyManager.register(
          _toggleLockHotKey,
          keyDownHandler: (hotKey) => toggleClickThrough(),
        );
      } catch (e) {
        debugPrint('[SubtitleOverlayManager] hotkey register error: $e');
      }
    }
  }

  Future<void> disposeHotKey() async {
    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      try {
        await hotKeyManager.unregister(_toggleLockHotKey);
      } catch (_) {}
    }
  }

  Future<void> toggleClickThrough() async {
    final next = !isClickThroughLocked.value;
    isClickThroughLocked.value = next;
    await WindowService.setSubwindowIgnoreMouseEvents(next);
    if (_subtitleWindowId != null) {
      try {
        await DesktopMultiWindow.invokeMethod(
          _subtitleWindowId!,
          'setClickThrough',
          next,
        );
      } catch (_) {}
    }
  }

  /// Launches or focuses the desktop floating subtitle window.
  Future<bool> openSubtitleWindow({
    String initialSource = '',
    String initialTarget = '',
    String targetLangCode = 'zh-TW',
  }) async {
    if (!Platform.isWindows && !Platform.isLinux && !Platform.isMacOS) {
      return false;
    }

    if (_subtitleWindowId != null) {
      // Subtitle window is already opened; bring it forward
      try {
        WindowController.fromWindowId(_subtitleWindowId!).show();
        return true;
      } catch (_) {
        _subtitleWindowId = null;
      }
    }

    try {
      const double initialWidth = 880.0;
      const double initialHeight = 160.0;

      final window = await DesktopMultiWindow.createWindow(jsonEncode({
        'type': 'subtitle_overlay',
        'sourceText': initialSource,
        'translatedText': initialTarget,
        'targetLangCode': targetLangCode,
        'width': initialWidth,
        'height': initialHeight,
      }));

      _subtitleWindowId = window.windowId;
      isOverlayActive.value = true;
      isClickThroughLocked.value = false;

      // Position the subtitle window near the center of the screen
      window
        ..setFrame(const Offset(100, 100) & const Size(initialWidth, initialHeight))
        ..center()
        ..show();

      // Configure Win32 styles: remove standard borders/titlebar, apply DWM full transparency, set always-on-top
      await WindowService.configureSubwindowAsOverlay();
      Future.delayed(const Duration(milliseconds: 150), () {
        WindowService.configureSubwindowAsOverlay();
      });
      Future.delayed(const Duration(milliseconds: 400), () {
        WindowService.configureSubwindowAsOverlay();
      });

      // Safely register global hotkey now that the window is launched
      await initHotKey();

      debugPrint('[SubtitleOverlayManager] Subtitle window launched (id: $_subtitleWindowId)');
      return true;
    } catch (e) {
      debugPrint('[SubtitleOverlayManager] Failed to create subtitle window: $e');
      return false;
    }
  }

  /// Closes the desktop floating subtitle window.
  Future<void> closeSubtitleWindow() async {
    if (_subtitleWindowId == null) return;
    final id = _subtitleWindowId!;
    _subtitleWindowId = null;
    isOverlayActive.value = false;
    isClickThroughLocked.value = false;

    await disposeHotKey();

    try {
      await DesktopMultiWindow.invokeMethod(id, 'closeSubtitleWindow');
    } catch (_) {}
    try {
      await WindowController.fromWindowId(id).close();
    } catch (_) {}
    try {
      await WindowService.closeSubwindow();
    } catch (_) {}
  }

  /// Toggles opening or closing the subtitle window.
  Future<void> toggleSubtitleWindow({
    String initialSource = '',
    String initialTarget = '',
    String targetLangCode = 'zh-TW',
  }) async {
    if (isOpen) {
      await closeSubtitleWindow();
    } else {
      await openSubtitleWindow(
        initialSource: initialSource,
        initialTarget: initialTarget,
        targetLangCode: targetLangCode,
      );
    }
  }

  /// Sends streaming recognition transcript and live translation chunks to the subtitle overlay.
  Future<void> sendStreamingSubtitle({
    required String sourceText,
    required String translatedText,
    required bool isFinal,
    String? targetLangCode,
  }) async {
    if (_subtitleWindowId == null) return;

    final payload = jsonEncode({
      'sourceText': sourceText,
      'translatedText': translatedText,
      'isFinal': isFinal,
      'targetLangCode': targetLangCode,
      'timestamp': DateTime.now().millisecondsSinceEpoch,
    });

    try {
      await DesktopMultiWindow.invokeMethod(
        _subtitleWindowId!,
        'updateSubtitle',
        payload,
      );
    } catch (e) {
      debugPrint('[SubtitleOverlayManager] sendStreamingSubtitle error: $e');
    }
  }

  /// Programmatically toggles the child subtitle window click-through mode from the main window.
  Future<void> setSubtitleClickThrough(bool enable) async {
    if (_subtitleWindowId == null) return;
    try {
      await DesktopMultiWindow.invokeMethod(
        _subtitleWindowId!,
        'setClickThrough',
        enable,
      );
      isClickThroughLocked.value = enable;
    } catch (e) {
      debugPrint('[SubtitleOverlayManager] setSubtitleClickThrough error: $e');
    }
  }
}
