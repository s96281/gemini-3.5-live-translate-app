import 'dart:convert';
import 'package:flutter/material.dart';
import 'config/theme.dart';
import 'screens/home_screen.dart';
import 'screens/subtitle_overlay_window.dart';
import 'services/subtitle_overlay_manager.dart';

void main(List<String> args) async {
  WidgetsFlutterBinding.ensureInitialized();

  // 若為 desktop_multi_window 喚起的子視窗實例
  if (args.firstOrNull == 'multi_window') {
    final int windowId = int.parse(args[1]);
    final Map<String, dynamic> initArgs =
        args.length > 2 ? jsonDecode(args[2]) : {};

    runApp(MaterialApp(
      title: 'Gemini 3.5 Desktop Subtitle',
      debugShowCheckedModeBanner: false,
      color: Colors.transparent,
      theme: ThemeData(
        scaffoldBackgroundColor: Colors.transparent,
        canvasColor: Colors.transparent,
      ),
      home: SubtitleOverlayWindow(
        windowId: windowId,
        initialArguments: initArgs,
      ),
    ));
    return;
  }

  // 主程式視窗啟動流程
  await AppThemes.init();
  SubtitleOverlayManager.instance.initMainIpcHandler();
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
