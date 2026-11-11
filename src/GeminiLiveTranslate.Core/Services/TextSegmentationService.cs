using System;
using System.Collections.Generic;
using System.Text;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Production text segmentation engine implementing 4-tier punctuation hierarchy and paragraph length thresholds.
/// </summary>
public class TextSegmentationService : ITextSegmentationService
{
    // Tier 1: Terminal punctuation
    private static readonly char[] TerminalPunctuation = { '。', '！', '？', '!', '?', '.', '\n' };
    private static readonly HashSet<char> TerminalSet = new(TerminalPunctuation);

    // Tier 2: Sub-clause punctuation
    private static readonly char[] SubClausePunctuation = { '，', '、', '；', '：', ',', ';', ':' };
    private static readonly HashSet<char> SubClauseSet = new(SubClausePunctuation);

    private readonly StringBuilder _pendingBuffer = new();
    private readonly object _lock = new();

    public int MaxCharacters { get; set; } = 50;

    /// <inheritdoc/>
    public SegmentationResult ProcessToken(string incomingToken, bool isFinal)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(incomingToken))
            {
                _pendingBuffer.Append(incomingToken);
            }

            string currentText = _pendingBuffer.ToString();
            if (string.IsNullOrWhiteSpace(currentText))
            {
                if (isFinal)
                {
                    _pendingBuffer.Clear();
                    return new SegmentationResult(string.Empty, string.Empty, true, 800);
                }
                return new SegmentationResult(string.Empty, string.Empty, false, 1800);
            }

            bool hasTerminal = HasTerminalPunctuation(currentText);
            int silenceInterval = hasTerminal ? 800 : 1800;

            if (isFinal)
            {
                string committed = currentText.Trim();
                _pendingBuffer.Clear();
                return new SegmentationResult(committed, string.Empty, true, silenceInterval);
            }

            // 1. If MaxCharacters is specified (> 0) and current text reaches or exceeds it
            if (MaxCharacters > 0 && currentText.Length >= MaxCharacters)
            {
                var chunks = ChunkText(currentText, MaxCharacters);
                if (chunks.Count > 1)
                {
                    string committed = chunks[0];
                    int consumed = currentText.IndexOf(committed, StringComparison.Ordinal) + committed.Length;
                    string pending = currentText.Substring(consumed).TrimStart();
                    _pendingBuffer.Clear();
                    _pendingBuffer.Append(pending);

                    bool endOfSentence = committed.Length > 0 && TerminalSet.Contains(committed[^1]);
                    return new SegmentationResult(committed, pending, endOfSentence, silenceInterval);
                }
            }

            // 2. Check if there is a completed terminal sentence in the buffer
            int terminalIdx = FindFirstTerminalSentenceEnd(currentText);
            if (terminalIdx > 0 && (MaxCharacters <= 0 || terminalIdx <= MaxCharacters))
            {
                string committed = currentText.Substring(0, terminalIdx).Trim();
                string pending = currentText.Substring(terminalIdx).TrimStart();
                _pendingBuffer.Clear();
                _pendingBuffer.Append(pending);

                return new SegmentationResult(committed, pending, true, 800);
            }

            // 3. Check if there is a completed sub-clause punctuation with following text
            int subClauseIdx = FindFirstSubClauseEnd(currentText);
            if (subClauseIdx > 0 && subClauseIdx < currentText.Length)
            {
                string pending = currentText.Substring(subClauseIdx).TrimStart();
                if (!string.IsNullOrEmpty(pending))
                {
                    string committed = currentText.Substring(0, subClauseIdx).Trim();
                    _pendingBuffer.Clear();
                    _pendingBuffer.Append(pending);

                    return new SegmentationResult(committed, pending, false, 1800);
                }
            }

            return new SegmentationResult(string.Empty, currentText, hasTerminal, silenceInterval);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ChunkText(string input, int maxCharacters)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        string trimmed = input.Trim();
        if (maxCharacters <= 0)
        {
            result.Add(trimmed);
            return result;
        }

        // Tier 1: Split into sentences by terminal punctuation
        var sentences = SplitByPunctuation(trimmed, TerminalSet);

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence)) continue;
            string s = sentence.Trim();

            if (s.Length <= maxCharacters)
            {
                result.Add(s);
                continue;
            }

            // Long sentence: Split by Tier 2 (sub-clause punctuation)
            var clauses = SplitByPunctuation(s, SubClauseSet);
            bool allClausesFit = clauses.Count > 1 && clauses.TrueForAll(c => c.Trim().Length <= maxCharacters);

            if (allClausesFit)
            {
                foreach (var c in clauses)
                {
                    string cl = c.Trim();
                    if (!string.IsNullOrEmpty(cl))
                    {
                        result.Add(cl);
                    }
                }
                continue;
            }

            // If clauses still exceed threshold or no sub-clause punctuation, split progressively using Tier 2 -> 3 -> 4
            int start = 0;
            while (start < s.Length)
            {
                int remaining = s.Length - start;
                if (remaining <= maxCharacters)
                {
                    string lastPart = s.Substring(start).Trim();
                    if (!string.IsNullOrEmpty(lastPart))
                    {
                        result.Add(lastPart);
                    }
                    break;
                }

                string slice = s.Substring(start, maxCharacters);
                int cutIndex = FindCutIndexInSlice(slice);

                if (cutIndex <= 0)
                {
                    cutIndex = maxCharacters;
                }

                // Never cut across a surrogate pair! If cut lands on a high surrogate, step back before it
                if (cutIndex > 0 && char.IsHighSurrogate(slice[cutIndex - 1]))
                {
                    cutIndex--;
                }

                if (cutIndex <= 0)
                {
                    cutIndex = Math.Min(2, slice.Length);
                }

                string part = s.Substring(start, cutIndex).Trim();
                if (!string.IsNullOrEmpty(part))
                {
                    result.Add(part);
                }

                start += cutIndex;
                while (start < s.Length && char.IsWhiteSpace(s[start]))
                {
                    start++;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public (IReadOnlyList<string> SourceChunks, IReadOnlyList<string> TargetChunks) AlignChunks(
        string sourceText,
        string targetText,
        int maxCharacters)
    {
        var sourceChunks = ChunkText(sourceText, maxCharacters);
        var targetChunks = ChunkText(targetText, maxCharacters);

        return (sourceChunks, targetChunks);
    }

    /// <inheritdoc/>
    public int GetRecommendedSilenceInterval(string text)
    {
        return HasTerminalPunctuation(text) ? 800 : 1800;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _pendingBuffer.Clear();
        }
    }

    private static bool HasTerminalPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) continue;
            return TerminalSet.Contains(c);
        }
        return false;
    }

    private static int FindFirstTerminalSentenceEnd(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (TerminalSet.Contains(text[i]))
            {
                int end = i + 1;
                while (end < text.Length && (TerminalSet.Contains(text[end]) || SubClauseSet.Contains(text[end])))
                {
                    end++;
                }
                return end;
            }
        }
        return -1;
    }

    private static int FindFirstSubClauseEnd(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (SubClauseSet.Contains(text[i]))
            {
                int end = i + 1;
                while (end < text.Length && (SubClauseSet.Contains(text[end]) || TerminalSet.Contains(text[end])))
                {
                    end++;
                }
                return end;
            }
        }
        return -1;
    }

    private static List<string> SplitByPunctuation(string text, HashSet<char> punctuationSet)
    {
        var list = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (punctuationSet.Contains(text[i]))
            {
                int end = i + 1;
                // Absorb consecutive punctuation
                while (end < text.Length && (punctuationSet.Contains(text[end]) || TerminalSet.Contains(text[end]) || SubClauseSet.Contains(text[end])))
                {
                    end++;
                }

                string piece = text.Substring(start, end - start);
                list.Add(piece);
                start = end;
                i = end - 1;
            }
        }

        if (start < text.Length)
        {
            string remainder = text.Substring(start);
            if (!string.IsNullOrWhiteSpace(remainder))
            {
                list.Add(remainder);
            }
        }

        return list;
    }

    private static int FindCutIndexInSlice(string slice)
    {
        // Tier 2: Look for rightmost sub-clause punctuation
        for (int i = slice.Length - 1; i >= 1; i--)
        {
            if (SubClauseSet.Contains(slice[i]))
            {
                int cut = i + 1;
                while (cut < slice.Length && SubClauseSet.Contains(slice[cut]))
                {
                    cut++;
                }
                return cut;
            }
        }

        // Tier 3: Look for rightmost whitespace boundary
        for (int i = slice.Length - 1; i >= 1; i--)
        {
            if (char.IsWhiteSpace(slice[i]))
            {
                return i;
            }
        }

        // Tier 4: Hard limit cut
        return slice.Length;
    }
}
