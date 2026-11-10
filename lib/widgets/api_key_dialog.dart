import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config/theme.dart';
import '../models/live_config.dart';
import '../services/audio_recorder_service.dart';

/// Modal dialog for configuring Google AI Studio API Key and Gemini Live settings.
/// Fully themed for both Dark and Light modes.
class ApiKeyDialog extends StatefulWidget {
  final LiveConfig currentConfig;
  final ValueChanged<LiveConfig> onSave;

  const ApiKeyDialog({
    super.key,
    required this.currentConfig,
    required this.onSave,
  });

  static Future<void> show(
    BuildContext context, {
    required LiveConfig currentConfig,
    required ValueChanged<LiveConfig> onSave,
  }) {
    return showDialog(
      context: context,
      barrierDismissible: true,
      builder: (ctx) => ApiKeyDialog(
        currentConfig: currentConfig,
        onSave: onSave,
      ),
    );
  }

  @override
  State<ApiKeyDialog> createState() => _ApiKeyDialogState();
}

class _ApiKeyDialogState extends State<ApiKeyDialog> {
  late TextEditingController _apiKeyController;
  late String _selectedModel;
  late String _selectedVoice;
  late bool _autoPlayAudio;
  late AudioSourceMode _selectedSourceMode;
  late int _maxParagraphLength;
  String? _selectedDeviceId;
  List<dynamic> _availableDevices = [];
  bool _obscureKey = true;

  @override
  void initState() {
    super.initState();
    _apiKeyController = TextEditingController(text: widget.currentConfig.apiKey);
    _selectedModel = widget.currentConfig.modelName;
    _selectedVoice = widget.currentConfig.voiceName;
    _autoPlayAudio = widget.currentConfig.autoPlayAudio;
    _selectedSourceMode = widget.currentConfig.audioSourceMode;
    _selectedDeviceId = widget.currentConfig.selectedDeviceId;
    _maxParagraphLength = widget.currentConfig.maxParagraphLength;
    _loadDevices();
  }

  Future<void> _loadDevices() async {
    try {
      final devices = await AudioRecorderService().getAvailableInputDevices();
      if (mounted) {
        setState(() {
          _availableDevices = devices;
        });
      }
    } catch (_) {}
  }

  @override
  void dispose() {
    _apiKeyController.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    final newKey = _apiKeyController.text.trim();

    // Persist API Key & audio preferences to local secure preferences
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('gemini_api_key', newKey);
    await prefs.setString('gemini_model_name', _selectedModel);
    await prefs.setString('gemini_voice', _selectedVoice);
    await prefs.setBool('gemini_autoplay', _autoPlayAudio);
    await prefs.setString('gemini_audio_source_mode', _selectedSourceMode.name);
    await prefs.setInt('gemini_max_paragraph_length', _maxParagraphLength);
    if (_selectedDeviceId != null) {
      await prefs.setString('gemini_selected_device_id', _selectedDeviceId!);
    } else {
      await prefs.remove('gemini_selected_device_id');
    }

    final updated = widget.currentConfig.copyWith(
      apiKey: newKey,
      modelName: _selectedModel,
      voiceName: _selectedVoice,
      autoPlayAudio: _autoPlayAudio,
      audioSourceMode: _selectedSourceMode,
      selectedDeviceId: _selectedDeviceId,
      maxParagraphLength: _maxParagraphLength,
    );

    widget.onSave(updated);
    if (mounted) {
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('設定已成功儲存'),
          duration: Duration(seconds: 2),
          behavior: SnackBarBehavior.floating,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final cyanPrimary = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);
    final dialogBg = isDark ? const Color(0xFF101626) : Colors.white;
    final textColor = isDark ? Colors.white : const Color(0xFF0F172A);
    final textSubColor = isDark ? Colors.white70 : const Color(0xFF475569);
    final textMutedColor = isDark ? Colors.white.withOpacity(0.4) : const Color(0xFF94A3B8);
    final borderColor = isDark ? Colors.white.withOpacity(0.12) : const Color(0xFFE2E8F0);
    final inputBg = isDark ? Colors.white.withOpacity(0.06) : const Color(0xFFF1F5F9);
    final cardBg = isDark ? Colors.white.withOpacity(0.04) : const Color(0xFFF8FAFC);
    final dropdownBg = isDark ? const Color(0xFF182236) : Colors.white;

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 20),
      child: Container(
        constraints: const BoxConstraints(maxWidth: 480),
        padding: const EdgeInsets.all(22),
        decoration: BoxDecoration(
          color: dialogBg,
          borderRadius: BorderRadius.circular(24),
          border: Border.all(color: borderColor),
          boxShadow: [
            BoxShadow(
              color: isDark ? Colors.black.withOpacity(0.6) : const Color(0x200F172A),
              blurRadius: 30,
              offset: const Offset(0, 10),
            )
          ],
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Header
              Row(
                children: [
                  Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: cyanPrimary.withOpacity(0.15),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Icon(Icons.settings_suggest_rounded, color: cyanPrimary, size: 22),
                  ),
                  const SizedBox(width: 12),
                  Text(
                    '應用程式設定',
                    style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                      color: textColor,
                    ),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: Icon(Icons.close, color: textSubColor, size: 20),
                    onPressed: () => Navigator.of(context).pop(),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              // Theme Style (Dark / Light)
              Text(
                '外觀主題風格 (Theme Style)',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: InkWell(
                      onTap: () async {
                        await AppThemes.setThemeMode(ThemeMode.dark);
                        setState(() {});
                      },
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        padding: const EdgeInsets.symmetric(vertical: 10),
                        decoration: BoxDecoration(
                          color: (themeModeNotifier.value == ThemeMode.dark)
                              ? cyanPrimary.withOpacity(0.18)
                              : cardBg,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: (themeModeNotifier.value == ThemeMode.dark)
                                ? cyanPrimary
                                : borderColor,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.dark_mode_rounded,
                              size: 16,
                              color: (themeModeNotifier.value == ThemeMode.dark)
                                  ? cyanPrimary
                                  : textSubColor,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '深色模式',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                color: (themeModeNotifier.value == ThemeMode.dark)
                                    ? cyanPrimary
                                    : textSubColor,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: InkWell(
                      onTap: () async {
                        await AppThemes.setThemeMode(ThemeMode.light);
                        setState(() {});
                      },
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        padding: const EdgeInsets.symmetric(vertical: 10),
                        decoration: BoxDecoration(
                          color: (themeModeNotifier.value == ThemeMode.light)
                              ? cyanPrimary.withOpacity(0.18)
                              : cardBg,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: (themeModeNotifier.value == ThemeMode.light)
                                ? cyanPrimary
                                : borderColor,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.light_mode_rounded,
                              size: 16,
                              color: (themeModeNotifier.value == ThemeMode.light)
                                  ? cyanPrimary
                                  : textSubColor,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '明亮模式',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                color: (themeModeNotifier.value == ThemeMode.light)
                                    ? cyanPrimary
                                    : textSubColor,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              // API Key Input
              Text(
                'Gemini API Key',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 8),
              TextField(
                controller: _apiKeyController,
                obscureText: _obscureKey,
                style: TextStyle(color: textColor, fontSize: 14),
                decoration: InputDecoration(
                  hintText: '請貼上您的 AI Studio API Key (AIzaSy...)',
                  hintStyle: TextStyle(color: textMutedColor, fontSize: 13),
                  filled: true,
                  fillColor: inputBg,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: BorderSide.none,
                  ),
                  suffixIcon: IconButton(
                    icon: Icon(
                      _obscureKey ? Icons.visibility_off : Icons.visibility,
                      color: textSubColor,
                      size: 18,
                    ),
                    onPressed: () => setState(() => _obscureKey = !_obscureKey),
                  ),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                ),
              ),
              const SizedBox(height: 6),
              Text(
                '※ Key 僅儲存於本機安全儲存區，直接連線至 Google 官方 Gemini Live 伺服器。',
                style: TextStyle(color: textMutedColor, fontSize: 11),
              ),
              const SizedBox(height: 16),

              // Audio Input Source Selection
              Text(
                '音訊擷取來源 (Audio Source)',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: InkWell(
                      onTap: () {
                        setState(() {
                          _selectedSourceMode = AudioSourceMode.systemAudio;
                          _autoPlayAudio = false;
                        });
                      },
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 10),
                        decoration: BoxDecoration(
                          color: _selectedSourceMode == AudioSourceMode.systemAudio
                              ? cyanPrimary.withOpacity(0.18)
                              : cardBg,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: _selectedSourceMode == AudioSourceMode.systemAudio
                                ? cyanPrimary
                                : borderColor,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.speaker_rounded,
                              size: 18,
                              color: _selectedSourceMode == AudioSourceMode.systemAudio
                                  ? cyanPrimary
                                  : textSubColor,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '裝置系統音效',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                color: _selectedSourceMode == AudioSourceMode.systemAudio
                                    ? cyanPrimary
                                    : textSubColor,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: InkWell(
                      onTap: () {
                        setState(() {
                          _selectedSourceMode = AudioSourceMode.microphone;
                        });
                      },
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 10),
                        decoration: BoxDecoration(
                          color: _selectedSourceMode == AudioSourceMode.microphone
                              ? (isDark ? const Color(0xFF7928CA).withOpacity(0.2) : const Color(0xFFF3E8FF))
                              : cardBg,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: _selectedSourceMode == AudioSourceMode.microphone
                                ? (isDark ? const Color(0xFF9D4EDD) : const Color(0xFF7C3AED))
                                : borderColor,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.mic_rounded,
                              size: 18,
                              color: _selectedSourceMode == AudioSourceMode.microphone
                                  ? (isDark ? const Color(0xFFD946EF) : const Color(0xFF7C3AED))
                                  : textSubColor,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '實體麥克風',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                color: _selectedSourceMode == AudioSourceMode.microphone
                                    ? (isDark ? const Color(0xFFE2B8FF) : const Color(0xFF7C3AED))
                                    : textSubColor,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ],
              ),

              if (_availableDevices.isNotEmpty) ...[
                const SizedBox(height: 12),
                Text(
                  '指定錄製輸入端點 (選用)',
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.w500, color: textSubColor),
                ),
                const SizedBox(height: 6),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                  decoration: BoxDecoration(
                    color: inputBg,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: DropdownButtonHideUnderline(
                    child: DropdownButton<String?>(
                      value: _selectedDeviceId,
                      isExpanded: true,
                      dropdownColor: dropdownBg,
                      style: TextStyle(color: textColor, fontSize: 13),
                      icon: Icon(Icons.arrow_drop_down, color: textSubColor),
                      items: [
                        const DropdownMenuItem<String?>(
                          value: null,
                          child: Text('預設端點 (Default)'),
                        ),
                        ..._availableDevices.map((dev) {
                          return DropdownMenuItem<String?>(
                            value: dev.id as String?,
                            child: Text(
                              dev.label?.toString() ?? dev.id.toString(),
                              overflow: TextOverflow.ellipsis,
                            ),
                          );
                        }),
                      ],
                      onChanged: (val) {
                        setState(() => _selectedDeviceId = val);
                      },
                    ),
                  ),
                ),
              ],

              const SizedBox(height: 16),

              // Gemini Model Selector
              Text(
                'Live 模型架構',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14),
                decoration: BoxDecoration(
                  color: inputBg,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: DropdownButtonHideUnderline(
                  child: DropdownButton<String>(
                    value: _selectedModel,
                    isExpanded: true,
                    dropdownColor: dropdownBg,
                    style: TextStyle(color: textColor, fontSize: 13),
                    icon: Icon(Icons.arrow_drop_down, color: textSubColor),
                    items: LiveConfig.availableModels.map((model) {
                      return DropdownMenuItem<String>(
                        value: model,
                        child: Text(model),
                      );
                    }).toList(),
                    onChanged: (val) {
                      if (val != null) setState(() => _selectedModel = val);
                    },
                  ),
                ),
              ),
              const SizedBox(height: 16),

              // Model Voice Selector
              Text(
                '翻譯語音音色 (Voice)',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14),
                decoration: BoxDecoration(
                  color: inputBg,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: DropdownButtonHideUnderline(
                  child: DropdownButton<String>(
                    value: _selectedVoice,
                    isExpanded: true,
                    dropdownColor: dropdownBg,
                    style: TextStyle(color: textColor, fontSize: 14),
                    icon: Icon(Icons.arrow_drop_down, color: textSubColor),
                    items: LiveConfig.availableVoices.map((voice) {
                      return DropdownMenuItem<String>(
                        value: voice,
                        child: Text('Voice: $voice'),
                      );
                    }).toList(),
                    onChanged: (val) {
                      if (val != null) setState(() => _selectedVoice = val);
                    },
                  ),
                ),
              ),
              const SizedBox(height: 14),

              // Autoplay switch
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                activeColor: cyanPrimary,
                title: Text(
                  '自動播放翻譯語音',
                  style: TextStyle(color: textColor, fontSize: 13, fontWeight: FontWeight.w500),
                ),
                subtitle: Text(
                  _selectedSourceMode == AudioSourceMode.systemAudio
                      ? '⚠️ 系統音效模式下建議關閉，避免喇叭播放譯文被再次錄入造成回音迴授'
                      : '開啟時朗讀譯文，建議配戴耳機避免麥克風回音',
                  style: TextStyle(
                    color: _selectedSourceMode == AudioSourceMode.systemAudio
                        ? (isDark ? Colors.amberAccent : const Color(0xFFD97706))
                        : textMutedColor,
                    fontSize: 11,
                  ),
                ),
                value: _autoPlayAudio,
                onChanged: (val) => setState(() => _autoPlayAudio = val),
              ),

              const SizedBox(height: 16),

              // Paragraph Hard Cut Section
              Text(
                '長段落自動硬切割（段落長度上限）',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: textSubColor),
              ),
              const SizedBox(height: 4),
              Text(
                '連續聆聽時達到指定字數強制自動分段，避免產生單一超長段落。',
                style: TextStyle(fontSize: 11, color: textMutedColor),
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  _buildCutOptionChip(35, '35 字 (短句)', cyanPrimary, cardBg, borderColor, textColor, textSubColor),
                  _buildCutOptionChip(50, '50 字 (推薦)', cyanPrimary, cardBg, borderColor, textColor, textSubColor),
                  _buildCutOptionChip(75, '75 字 (中長)', cyanPrimary, cardBg, borderColor, textColor, textSubColor),
                  _buildCutOptionChip(100, '100 字 (長段)', cyanPrimary, cardBg, borderColor, textColor, textSubColor),
                  _buildCutOptionChip(0, '不限制 (關閉)', cyanPrimary, cardBg, borderColor, textColor, textSubColor),
                ],
              ),

              const SizedBox(height: 18),

              // Actions
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      style: OutlinedButton.styleFrom(
                        foregroundColor: textSubColor,
                        side: BorderSide(color: borderColor),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        padding: const EdgeInsets.symmetric(vertical: 12),
                      ),
                      onPressed: () => Navigator.of(context).pop(),
                      child: const Text('取消'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton(
                      style: ElevatedButton.styleFrom(
                        backgroundColor: cyanPrimary,
                        foregroundColor: isDark ? Colors.black : Colors.white,
                        elevation: 0,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        padding: const EdgeInsets.symmetric(vertical: 12),
                      ),
                      onPressed: _handleSave,
                      child: const Text('儲存設定', style: TextStyle(fontWeight: FontWeight.bold)),
                    ),
                  ),
                ],
              )
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildCutOptionChip(
    int value,
    String label,
    Color cyanPrimary,
    Color cardBg,
    Color borderColor,
    Color textColor,
    Color textSubColor,
  ) {
    final isSelected = (_maxParagraphLength == value);
    return InkWell(
      onTap: () {
        setState(() => _maxParagraphLength = value);
      },
      borderRadius: BorderRadius.circular(10),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
        decoration: BoxDecoration(
          color: isSelected ? cyanPrimary.withOpacity(0.18) : cardBg,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(
            color: isSelected ? cyanPrimary : borderColor,
            width: isSelected ? 1.5 : 1.0,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 12,
            fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
            color: isSelected ? cyanPrimary : textSubColor,
          ),
        ),
      ),
    );
  }
}
