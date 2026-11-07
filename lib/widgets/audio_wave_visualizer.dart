import 'dart:math';
import 'package:flutter/material.dart';

/// A sleek, futuristic audio waveform visualizer with glowing neon effects
/// and organic wave ripples driven by live microphone amplitude.
class AudioWaveVisualizer extends StatefulWidget {
  final double amplitude; // 0.0 ~ 1.0 normalized
  final bool isRecording;
  final Color primaryColor;
  final Color secondaryColor;

  const AudioWaveVisualizer({
    super.key,
    required this.amplitude,
    required this.isRecording,
    this.primaryColor = const Color(0xFF00E5FF),
    this.secondaryColor = const Color(0xFF7928CA),
  });

  @override
  State<AudioWaveVisualizer> createState() => _AudioWaveVisualizerState();
}

class _AudioWaveVisualizerState extends State<AudioWaveVisualizer>
    with SingleTickerProviderStateMixin {
  late AnimationController _animController;

  @override
  void initState() {
    super.initState();
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2000),
    )..repeat();
  }

  @override
  void dispose() {
    _animController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _animController,
      builder: (context, child) {
        return CustomPaint(
          size: const Size(double.infinity, 90),
          painter: _WavePainter(
            phase: _animController.value * 2 * pi,
            amplitude: widget.isRecording ? (widget.amplitude * 0.85 + 0.15) : 0.05,
            isRecording: widget.isRecording,
            primaryColor: widget.primaryColor,
            secondaryColor: widget.secondaryColor,
          ),
        );
      },
    );
  }
}

class _WavePainter extends CustomPainter {
  final double phase;
  final double amplitude;
  final bool isRecording;
  final Color primaryColor;
  final Color secondaryColor;

  _WavePainter({
    required this.phase,
    required this.amplitude,
    required this.isRecording,
    required this.primaryColor,
    required this.secondaryColor,
  });

  @override
  void paint(Canvas canvas, Size size) {
    // Draw 3 layers of waves for glowing depth
    _drawSingleWave(
      canvas: canvas,
      size: size,
      phaseOffset: phase,
      freqMultiplier: 1.0,
      ampMultiplier: amplitude * 28.0,
      color: secondaryColor.withOpacity(isRecording ? 0.45 : 0.15),
      strokeWidth: 2.0,
    );

    _drawSingleWave(
      canvas: canvas,
      size: size,
      phaseOffset: phase * 1.3 + 1.2,
      freqMultiplier: 1.5,
      ampMultiplier: amplitude * 22.0,
      color: primaryColor.withOpacity(isRecording ? 0.75 : 0.25),
      strokeWidth: 2.5,
    );

    _drawSingleWave(
      canvas: canvas,
      size: size,
      phaseOffset: phase * 1.8 + 2.5,
      freqMultiplier: 2.2,
      ampMultiplier: amplitude * 16.0,
      color: Colors.white.withOpacity(isRecording ? 0.9 : 0.3),
      strokeWidth: 1.5,
    );
  }

  void _drawSingleWave({
    required Canvas canvas,
    required Size size,
    required double phaseOffset,
    required double freqMultiplier,
    required double ampMultiplier,
    required Color color,
    required double strokeWidth,
  }) {
    final path = Path();
    final midY = size.height / 2;
    final width = size.width;

    path.moveTo(0, midY);

    for (double x = 0; x <= width; x += 3) {
      // Taper the edges so wave fades smoothly into left and right boundaries
      final normX = x / width;
      final taper = sin(normX * pi);

      final y = midY +
          sin((x / width * 4 * pi * freqMultiplier) + phaseOffset) *
              ampMultiplier *
              taper;
      path.lineTo(x, y);
    }

    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = strokeWidth
      ..strokeCap = StrokeCap.round
      ..maskFilter = MaskFilter.blur(BlurStyle.solid, strokeWidth * 0.8);

    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant _WavePainter oldDelegate) {
    return oldDelegate.phase != phase ||
        oldDelegate.amplitude != amplitude ||
        oldDelegate.isRecording != isRecording;
  }
}
