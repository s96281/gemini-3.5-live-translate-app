import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'screens/home_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  // Set immersive dark system UI overlays on mobile
  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      statusBarIconBrightness: Brightness.light,
      systemNavigationBarColor: Color(0xFF090D16),
      systemNavigationBarIconBrightness: Brightness.light,
    ),
  );

  runApp(const GeminiLiveTranslateApp());
}

class GeminiLiveTranslateApp extends StatelessWidget {
  const GeminiLiveTranslateApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Gemini 3.5 Live Translate',
      debugShowCheckedModeBanner: false,
      themeMode: ThemeMode.dark,
      darkTheme: ThemeData(
        useMaterial3: true,
        brightness: Brightness.dark,
        scaffoldBackgroundColor: const Color(0xFF090D16),
        colorScheme: const ColorScheme.dark(
          primary: Color(0xFF00E5FF),
          secondary: Color(0xFF7928CA),
          surface: Color(0xFF101726),
        ),
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFF090D16),
          elevation: 0,
        ),
        fontFamily: 'Roboto',
      ),
      home: const HomeScreen(),
    );
  }
}
