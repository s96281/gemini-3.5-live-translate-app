import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Service to handle native Windows Desktop Floating Subtitle Overlay (Option 2: Pure Native Layered Window)
/// and Always-on-Top desktop window management.
class WindowService {
  static const MethodChannel _channel = MethodChannel('com.gemini.livetranslate/window');

  static VoidCallback? _onClosedCallback;
  static ValueChanged<bool>? _onClickThroughChangedCallback;
  static bool _handlerInitialized = false;

  /// Initializes callbacks from the native Win32 overlay
  static void initNativeCallbacks({
    VoidCallback? onClosed,
    ValueChanged<bool>? onClickThroughChanged,
  }) {
    _onClosedCallback = onClosed;
    _onClickThroughChangedCallback = onClickThroughChanged;

    if (!_handlerInitialized && Platform.isWindows) {
      _handlerInitialized = true;
      _channel.setMethodCallHandler((call) async {
        if (call.method == 'onNativeOverlayClosed') {
          _onClosedCallback?.call();
        } else if (call.method == 'onNativeOverlayClickThroughChanged') {
          final bool clickThrough = call.arguments == true;
          _onClickThroughChangedCallback?.call(clickThrough);
        }
      });
    }
  }

  /// Shows or hides the pure Win32 GDI+ Layered Desktop Floating Subtitle Overlay
  static Future<bool> showNativeOverlay(bool show) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('showNativeOverlay', {
        'show': show,
      });
      return res ?? false;
    } catch (e) {
      debugPrint('[WindowService] showNativeOverlay error: $e');
      return false;
    }
  }

  /// Updates live transcription & translation subtitles on the native overlay
  static Future<bool> updateNativeOverlaySubtitle({
    String source = '',
    String target = '',
  }) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('updateNativeOverlaySubtitle', {
        'source': source,
        'target': target,
      });
      return res ?? false;
    } catch (e) {
      debugPrint('[WindowService] updateNativeOverlaySubtitle error: $e');
      return false;
    }
  }

  /// Enables or disables mouse event pass-through on the native overlay
  static Future<bool> setNativeOverlayClickThrough(bool ignore) async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('setNativeOverlayClickThrough', {
        'ignore': ignore,
      });
      return res ?? false;
    } catch (e) {
      debugPrint('[WindowService] setNativeOverlayClickThrough error: $e');
      return false;
    }
  }

  /// Queries whether the native subtitle overlay is currently visible
  static Future<bool> isNativeOverlayVisible() async {
    if (!Platform.isWindows) return false;
    try {
      final res = await _channel.invokeMethod<bool>('isNativeOverlayVisible');
      return res ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Sets main window Always-on-Top state.
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
}
