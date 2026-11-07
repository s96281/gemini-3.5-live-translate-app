import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/live_config.dart';

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
  bool _obscureKey = true;

  @override
  void initState() {
    super.initState();
    _apiKeyController = TextEditingController(text: widget.currentConfig.apiKey);
    _selectedVoice = widget.currentConfig.voiceName;
    _autoPlayAudio = widget.currentConfig.autoPlayAudio;
  }

  @override
  void dispose() {
    _apiKeyController.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    final newKey = _apiKeyController.text.trim();

    // Persist API Key to local secure preferences
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('gemini_api_key', newKey);
    await prefs.setString('gemini_voice', _selectedVoice);
    await prefs.setBool('gemini_autoplay', _autoPlayAudio);

    final updated = widget.currentConfig.copyWith(
      apiKey: newKey,
      voiceName: _selectedVoice,
      autoPlayAudio: _autoPlayAudio,
    );

    widget.onSave(updated);
    if (mounted) {
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('API Key 與設定已成功儲存'),
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
                  child: const Icon(Icons.vpn_key_rounded, color: Color(0xFF00E5FF), size: 22),
                ),
                const SizedBox(width: 12),
                const Text(
                  'Google AI Studio 設定',
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
            const SizedBox(height: 8),
            Text(
              '※ Key 僅儲存於本機安全儲存區，直接連線至 Google 官方 Gemini Live 伺服器。',
              style: TextStyle(color: Colors.white.withOpacity(0.4), fontSize: 11),
            ),
            const SizedBox(height: 18),

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
            const SizedBox(height: 16),

            // Autoplay switch
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              activeColor: const Color(0xFF00E5FF),
              title: const Text(
                '自動播放翻譯語音',
                style: TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w500),
              ),
              subtitle: Text(
                '開啟時朗讀譯文，建議配戴耳機避免麥克風回音',
                style: TextStyle(color: Colors.white.withOpacity(0.4), fontSize: 11),
              ),
              value: _autoPlayAudio,
              onChanged: (val) => setState(() => _autoPlayAudio = val),
            ),

            const SizedBox(height: 20),

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
    );
  }
}
