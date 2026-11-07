import 'package:flutter/material.dart';
import '../config/languages.dart';

/// A sleek, searchable dialog to select the target translation language.
class LanguageSelectorModal extends StatefulWidget {
  final String currentCode;
  final ValueChanged<LanguageInfo> onSelected;

  const LanguageSelectorModal({
    super.key,
    required this.currentCode,
    required this.onSelected,
  });

  static Future<void> show(
    BuildContext context, {
    required String currentCode,
    required ValueChanged<LanguageInfo> onSelected,
  }) {
    return showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (ctx) => LanguageSelectorModal(
        currentCode: currentCode,
        onSelected: onSelected,
      ),
    );
  }

  @override
  State<LanguageSelectorModal> createState() => _LanguageSelectorModalState();
}

class _LanguageSelectorModalState extends State<LanguageSelectorModal> {
  final TextEditingController _searchController = TextEditingController();
  List<LanguageInfo> _filteredList = SupportedLanguages.list;

  @override
  void initState() {
    super.initState();
    _searchController.addListener(_onSearchChanged);
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  void _onSearchChanged() {
    final query = _searchController.text.trim().toLowerCase();
    setState(() {
      if (query.isEmpty) {
        _filteredList = SupportedLanguages.list;
      } else {
        _filteredList = SupportedLanguages.list.where((lang) {
          return lang.nameZh.toLowerCase().contains(query) ||
              lang.nameNative.toLowerCase().contains(query) ||
              lang.code.toLowerCase().contains(query);
        }).toList();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      height: MediaQuery.of(context).size.height * 0.75,
      decoration: const BoxDecoration(
        color: Color(0xFF101624),
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
        border: Border(
          top: BorderSide(color: Colors.white12, width: 1),
        ),
      ),
      child: Column(
        children: [
          // Drag handle
          Container(
            margin: const EdgeInsets.only(top: 12, bottom: 8),
            width: 40,
            height: 4,
            decoration: BoxDecoration(
              color: Colors.white24,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          // Title
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
            child: Row(
              children: [
                const Text(
                  '選擇目標翻譯語言',
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.bold,
                    color: Colors.white,
                  ),
                ),
                const Spacer(),
                IconButton(
                  icon: const Icon(Icons.close, color: Colors.white60),
                  onPressed: () => Navigator.of(context).pop(),
                ),
              ],
            ),
          ),
          // Search box
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            child: TextField(
              controller: _searchController,
              style: const TextStyle(color: Colors.white),
              decoration: InputDecoration(
                hintText: '搜尋語言名稱或代碼 (如: 日本語, en, 繁體中文)...',
                hintStyle: TextStyle(color: Colors.white.withOpacity(0.35)),
                prefixIcon: const Icon(Icons.search, color: Color(0xFF00E5FF)),
                filled: true,
                fillColor: Colors.white.withOpacity(0.06),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(14),
                  borderSide: BorderSide.none,
                ),
                contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              ),
            ),
          ),
          const SizedBox(height: 8),
          // Language list
          Expanded(
            child: ListView.builder(
              itemCount: _filteredList.length,
              itemBuilder: (context, index) {
                final lang = _filteredList[index];
                final isSelected = lang.code.toLowerCase() == widget.currentCode.toLowerCase();

                return ListTile(
                  leading: Text(
                    lang.flag,
                    style: const TextStyle(fontSize: 26),
                  ),
                  title: Text(
                    lang.nameZh,
                    style: TextStyle(
                      fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
                      color: isSelected ? const Color(0xFF00E5FF) : Colors.white,
                    ),
                  ),
                  subtitle: Text(
                    '${lang.nameNative} (${lang.code})',
                    style: TextStyle(
                      color: Colors.white.withOpacity(0.45),
                      fontSize: 12,
                    ),
                  ),
                  trailing: isSelected
                      ? const Icon(Icons.check_circle, color: Color(0xFF00E5FF), size: 20)
                      : null,
                  onTap: () {
                    widget.onSelected(lang);
                    Navigator.of(context).pop();
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
