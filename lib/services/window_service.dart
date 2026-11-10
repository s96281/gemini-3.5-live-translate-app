import 'dart:io';
import 'package:flutter/services.dart';

/// Service to handle native window actions like Mini Overlay (Floating Picture-in-Picture)
/// and Always-on-Top on desktop platforms (Windows).
class WindowService {
  static const MethodChannel _channel = MethodChannel('com.gemini.livetranslate/window');

  /// Requests the OS to switch into a compact, always-on-top floating subtitle overlay.
  static Future<bool> enterMiniOverlay({int width = 520, int height = 160}) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('enterMiniOverlay', {
        'width': width,
        'height': height,
      });
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Requests the OS to restore the window to standard full interface dimensions.
  static Future<bool> exitMiniOverlay() async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('exitMiniOverlay');
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Sets window Always-on-Top state.
  static Future<bool> setAlwaysOnTop(bool alwaysOnTop) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('setAlwaysOnTop', {
        'alwaysOnTop': alwaysOnTop,
      });
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Sets whether mouse events pass straight through the window (click-through mode).
  static Future<bool> setIgnoreMouseEvents(bool ignore) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('setIgnoreMouseEvents', {
        'ignore': ignore,
      });
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Triggers smooth native window dragging on Windows (WM_NCLBUTTONDOWN).
  static Future<bool> startDragging() async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('startDragging');
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Configures any created secondary subwindows as frameless, DWM transparent, and topmost overlay.
  static Future<bool> configureSubwindowAsOverlay() async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('configureSubwindowAsOverlay');
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Sets mouse click-through on secondary subwindows.
  static Future<bool> setSubwindowIgnoreMouseEvents(bool ignore) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('setSubwindowIgnoreMouseEvents', {
        'ignore': ignore,
      });
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Native drag for secondary subwindows.
  static Future<bool> startDraggingSubwindow() async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('startDraggingSubwindow');
      return res ?? false;
    } catch (_) {
      return false;
    }
  }
}
