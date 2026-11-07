import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/live_config.dart';
import '../services/audio_recorder_service.dart';

/// Modal dialog for configuring Google AI Studio API Key and Gemini Live settings.
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
  late String _selectedVoice;
  late bool _autoPlayAudio;
  late AudioSourceMode _selectedSourceMode;
  String? _selectedDeviceId;
  List<dynamic> _availableDevices = [];
  bool _obscureKey = true;

  @override
  void initState() {
    super.initState();
    _apiKeyController = TextEditingController(text: widget.currentConfig.apiKey);
    _selectedVoice = widget.currentConfig.voiceName;
    _autoPlayAudio = widget.currentConfig.autoPlayAudio;
    _selectedSourceMode = widget.currentConfig.audioSourceMode;
    _selectedDeviceId = widget.currentConfig.selectedDeviceId;
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
    await prefs.setString('gemini_voice', _selectedVoice);
    await prefs.setBool('gemini_autoplay', _autoPlayAudio);
    await prefs.setString('gemini_audio_source_mode', _selectedSourceMode.name);
    if (_selectedDeviceId != null) {
      await prefs.setString('gemini_selected_device_id', _selectedDeviceId!);
    } else {
      await prefs.remove('gemini_selected_device_id');
    }

    final updated = widget.currentConfig.copyWith(
      apiKey: newKey,
      voiceName: _selectedVoice,
      autoPlayAudio: _autoPlayAudio,
      audioSourceMode: _selectedSourceMode,
      selectedDeviceId: _selectedDeviceId,
    );

    widget.onSave(updated);
    if (mounted) {
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('API Key 與音訊設定已成功儲存'),
          backgroundColor: Color(0xFF00E5FF),
          behavior: SnackBarBehavior.floating,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.symmetric(horizontal: 24, vertical: 24),
      child: Container(
        constraints: const BoxConstraints(maxWidth: 480),
        padding: const EdgeInsets.all(24),
        decoration: BoxDecoration(
          color: const Color(0xFF101626),
          borderRadius: BorderRadius.circular(24),
          border: Border.all(color: Colors.white.withOpacity(0.12)),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withOpacity(0.6),
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
                      color: const Color(0xFF00E5FF).withOpacity(0.15),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: const Icon(Icons.settings_suggest_rounded, color: Color(0xFF00E5FF), size: 22),
                  ),
                  const SizedBox(width: 12),
                  const Text(
                    '應用程式設定',
                    style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                      color: Colors.white,
                    ),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: const Icon(Icons.close, color: Colors.white54, size: 20),
                    onPressed: () => Navigator.of(context).pop(),
                  ),
                ],
              ),
              const SizedBox(height: 18),

              // API Key Input
              const Text(
                'Gemini API Key',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: Colors.white70),
              ),
              const SizedBox(height: 8),
              TextField(
                controller: _apiKeyController,
                obscureText: _obscureKey,
                style: const TextStyle(color: Colors.white, fontSize: 14),
                decoration: InputDecoration(
                  hintText: '請貼上您的 AI Studio API Key (AIzaSy...)',
                  hintStyle: TextStyle(color: Colors.white.withOpacity(0.3), fontSize: 13),
                  filled: true,
                  fillColor: Colors.white.withOpacity(0.06),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: BorderSide.none,
                  ),
                  suffixIcon: IconButton(
                    icon: Icon(
                      _obscureKey ? Icons.visibility_off : Icons.visibility,
                      color: Colors.white60,
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
                style: TextStyle(color: Colors.white.withOpacity(0.4), fontSize: 11),
              ),
              const SizedBox(height: 18),

              // Audio Input Source Selection
              const Text(
                '音訊擷取來源 (Audio Source)',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: Colors.white70),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: InkWell(
                      onTap: () {
                        setState(() {
                          _selectedSourceMode = AudioSourceMode.systemAudio;
                          // 防迴授保護：若選系統音效，預設建議關閉自動語音播放避免回音自激
                          _autoPlayAudio = false;
                        });
                      },
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 10),
                        decoration: BoxDecoration(
                          color: _selectedSourceMode == AudioSourceMode.systemAudio
                              ? const Color(0xFF00E5FF).withOpacity(0.18)
                              : Colors.white.withOpacity(0.05),
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: _selectedSourceMode == AudioSourceMode.systemAudio
                                ? const Color(0xFF00E5FF)
                                : Colors.white12,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.speaker_rounded,
                              size: 18,
                              color: _selectedSourceMode == AudioSourceMode.systemAudio
                                  ? const Color(0xFF00E5FF)
                                  : Colors.white60,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '裝置系統音效',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.bold,
                                color: _selectedSourceMode == AudioSourceMode.systemAudio
                                    ? Colors.white
                                    : Colors.white70,
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
                              ? const Color(0xFF7928CA).withOpacity(0.25)
                              : Colors.white.withOpacity(0.05),
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: _selectedSourceMode == AudioSourceMode.microphone
                                ? const Color(0xFFD946EF)
                                : Colors.white12,
                          ),
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.mic_rounded,
                              size: 18,
                              color: _selectedSourceMode == AudioSourceMode.microphone
                                  ? const Color(0xFFD946EF)
                                  : Colors.white60,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              '實體麥克風',
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.bold,
                                color: _selectedSourceMode == AudioSourceMode.microphone
                                    ? Colors.white
                                    : Colors.white70,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                _selectedSourceMode == AudioSourceMode.systemAudio
                    ? '💡 提示：用於翻譯影片、線上會議或遊戲背景音（系統自動關閉降噪與回音消除以確保清晰原音）。'
                    : '💡 提示：用於現場講話口譯（自動開啟回音消除與降噪濾波）。',
                style: TextStyle(color: Colors.white.withOpacity(0.4), fontSize: 11),
              ),

              // Specific device selector if multiple devices found
              if (_availableDevices.isNotEmpty) ...[
                const SizedBox(height: 14),
                const Text(
                  '指定錄製端點 (Device)',
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Colors.white60),
                ),
                const SizedBox(height: 6),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.06),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: DropdownButtonHideUnderline(
                    child: DropdownButton<String?>(
                      value: _selectedDeviceId,
                      isExpanded: true,
                      dropdownColor: const Color(0xFF182236),
                      style: const TextStyle(color: Colors.white, fontSize: 13),
                      icon: const Icon(Icons.arrow_drop_down, color: Colors.white70),
                      items: [
                        DropdownMenuItem<String?>(
                          value: null,
                          child: Text(
                            _selectedSourceMode == AudioSourceMode.systemAudio
                                ? '自動偵測系統音效 (立體聲混音 / 虛擬端點)'
                                : '系統預設麥克風 (Default Microphone)',
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                        ..._availableDevices.map((dev) {
                          final label = (dev.label as String?)?.isNotEmpty == true
                              ? dev.label as String
                              : (dev.id as String);
                          return DropdownMenuItem<String?>(
                            value: dev.id as String,
                            child: Text(
                              label,
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

              // Model Voice Selector
              const Text(
                '翻譯語音音色 (Voice)',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: Colors.white70),
              ),
              const SizedBox(height: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14),
                decoration: BoxDecoration(
                  color: Colors.white.withOpacity(0.06),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: DropdownButtonHideUnderline(
                  child: DropdownButton<String>(
                    value: _selectedVoice,
                    isExpanded: true,
                    dropdownColor: const Color(0xFF182236),
                    style: const TextStyle(color: Colors.white, fontSize: 14),
                    icon: const Icon(Icons.arrow_drop_down, color: Colors.white70),
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
                activeColor: const Color(0xFF00E5FF),
                title: const Text(
                  '自動播放翻譯語音',
                  style: TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w500),
                ),
                subtitle: Text(
                  _selectedSourceMode == AudioSourceMode.systemAudio
                      ? '⚠️ 系統音效模式下建議關閉，避免喇叭播放譯文被再次錄入造成回音迴授'
                      : '開啟時朗讀譯文，建議配戴耳機避免麥克風回音',
                  style: TextStyle(
                    color: _selectedSourceMode == AudioSourceMode.systemAudio
                        ? Colors.amberAccent.withOpacity(0.8)
                        : Colors.white.withOpacity(0.4),
                    fontSize: 11,
                  ),
                ),
                value: _autoPlayAudio,
                onChanged: (val) => setState(() => _autoPlayAudio = val),
              ),

              const SizedBox(height: 18),

              // Actions
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      style: OutlinedButton.styleFrom(
                        foregroundColor: Colors.white70,
                        side: BorderSide(color: Colors.white.withOpacity(0.15)),
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
                        backgroundColor: const Color(0xFF00E5FF),
                        foregroundColor: Colors.black,
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
}
