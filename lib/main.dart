import 'dart:io';
import 'package:flutter/material.dart';
import 'package:hotkey_manager/hotkey_manager.dart';
import 'config/theme.dart';
import 'screens/home_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 主程式視窗啟動流程
  await AppThemes.init();

  if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
    try {
      await hotKeyManager.unregisterAll();
    } catch (e) {
      debugPrint('[main] hotKeyManager.unregisterAll error: $e');
    }
  }

  runApp(const GeminiLiveTranslateApp());
}

class GeminiLiveTranslateApp extends StatelessWidget {
  const GeminiLiveTranslateApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<ThemeMode>(
      valueListenable: themeModeNotifier,
      builder: (context, currentMode, _) {
        return MaterialApp(
          title: 'Gemini 3.5 Live Translate',
          debugShowCheckedModeBanner: false,
          color: Colors.transparent,
          themeMode: currentMode,
          theme: AppThemes.lightTheme,
          darkTheme: AppThemes.darkTheme,
          home: const HomeScreen(),
        );
      },
    );
  }
}
