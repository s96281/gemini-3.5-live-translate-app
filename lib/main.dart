import 'package:flutter/material.dart';
import 'config/theme.dart';
import 'screens/home_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await AppThemes.init();
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
          themeMode: currentMode,
          theme: AppThemes.lightTheme,
          darkTheme: AppThemes.darkTheme,
          home: const HomeScreen(),
        );
      },
    );
  }
}
