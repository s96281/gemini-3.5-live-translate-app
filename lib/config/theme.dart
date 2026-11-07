import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Global notifier for theme mode (Dark / Light)
final ValueNotifier<ThemeMode> themeModeNotifier = ValueNotifier<ThemeMode>(ThemeMode.dark);

class AppThemes {
  static const String prefsKey = 'gemini_theme_mode';

  /// Initialize theme mode from SharedPreferences
  static Future<void> init() async {
    final prefs = await SharedPreferences.getInstance();
    final savedTheme = prefs.getString(prefsKey) ?? 'dark';
    themeModeNotifier.value = (savedTheme == 'light') ? ThemeMode.light : ThemeMode.dark;
    updateSystemUiOverlay(themeModeNotifier.value == ThemeMode.dark);
  }

  /// Toggle theme between Dark and Light
  static Future<void> toggleTheme() async {
    final newMode = (themeModeNotifier.value == ThemeMode.dark) ? ThemeMode.light : ThemeMode.dark;
    await setThemeMode(newMode);
  }

  /// Explicitly set theme mode
  static Future<void> setThemeMode(ThemeMode mode) async {
    themeModeNotifier.value = mode;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(prefsKey, mode == ThemeMode.light ? 'light' : 'dark');
    updateSystemUiOverlay(mode == ThemeMode.dark);
  }

  /// Update system status bar and navigation bar brightness
  static void updateSystemUiOverlay(bool isDark) {
    SystemChrome.setSystemUIOverlayStyle(
      SystemUiOverlayStyle(
        statusBarColor: Colors.transparent,
        statusBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
        systemNavigationBarColor: isDark ? const Color(0xFF090D16) : const Color(0xFFF3F6FC),
        systemNavigationBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
      ),
    );
  }

  // Dark Theme Definition
  static final ThemeData darkTheme = ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    scaffoldBackgroundColor: const Color(0xFF090D16),
    colorScheme: const ColorScheme.dark(
      primary: Color(0xFF00E5FF),
      secondary: Color(0xFF7928CA),
      surface: Color(0xFF101726),
      onSurface: Colors.white,
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: Color(0xFF090D16),
      elevation: 0,
    ),
    fontFamily: 'Roboto',
  );

  // Light Theme Definition
  static final ThemeData lightTheme = ThemeData(
    useMaterial3: true,
    brightness: Brightness.light,
    scaffoldBackgroundColor: const Color(0xFFF3F6FC),
    colorScheme: const ColorScheme.light(
      primary: Color(0xFF0284C7),
      secondary: Color(0xFF7C3AED),
      surface: Colors.white,
      onSurface: Color(0xFF0F172A),
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: Color(0xFFF3F6FC),
      elevation: 0,
    ),
    fontFamily: 'Roboto',
  );
}
