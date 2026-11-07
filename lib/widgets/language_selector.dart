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
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final cyanColor = isDark ? const Color(0xFF00E5FF) : const Color(0xFF0284C7);
    final bgColor = isDark ? const Color(0xFF101624) : Colors.white;
    final textColor = isDark ? Colors.white : const Color(0xFF0F172A);
    final subtextColor = isDark ? Colors.white.withOpacity(0.45) : const Color(0xFF64748B);
    final borderColor = isDark ? Colors.white12 : const Color(0xFFE2E8F0);
    final searchBg = isDark ? Colors.white.withOpacity(0.06) : const Color(0xFFF1F5F9);

    return Container(
      height: MediaQuery.of(context).size.height * 0.75,
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
        border: Border(
          top: BorderSide(color: borderColor, width: 1),
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
              color: isDark ? Colors.white24 : const Color(0xFFCBD5E1),
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          // Title
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
            child: Row(
              children: [
                Text(
                  '選擇目標翻譯語言',
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.bold,
                    color: textColor,
                  ),
                ),
                const Spacer(),
                IconButton(
                  icon: Icon(Icons.close, color: subtextColor),
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
              style: TextStyle(color: textColor),
              decoration: InputDecoration(
                hintText: '搜尋語言名稱或代碼 (如: 日本語, en, 繁體中文)...',
                hintStyle: TextStyle(color: subtextColor),
                prefixIcon: Icon(Icons.search, color: cyanColor),
                filled: true,
                fillColor: searchBg,
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
                      color: isSelected ? cyanColor : textColor,
                    ),
                  ),
                  subtitle: Text(
                    '${lang.nameNative} (${lang.code})',
                    style: TextStyle(
                      color: subtextColor,
                      fontSize: 12,
                    ),
                  ),
                  trailing: isSelected
                      ? Icon(Icons.check_circle, color: cyanColor, size: 20)
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
