using System;

namespace GeminiLiveTranslate.Core.Models;

/// <summary>
/// Language definition with BCP-47 code, display names, and emoji flag.
/// </summary>
public class LanguageInfo
{
    public string Code { get; }
    public string DisplayName { get; }
    public string NativeName { get; }
    public string FlagEmoji { get; }

    public LanguageInfo(string code, string displayName, string nativeName, string flagEmoji)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        NativeName = nativeName ?? throw new ArgumentNullException(nameof(nativeName));
        FlagEmoji = flagEmoji ?? string.Empty;
    }

    public override string ToString() => $"{FlagEmoji} {DisplayName} ({NativeName}) [{Code}]";
}
