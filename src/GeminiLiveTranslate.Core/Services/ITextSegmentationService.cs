using System;
using System.Collections.Generic;

namespace GeminiLiveTranslate.Core.Services;

/// <summary>
/// Result of token processing and progressive text segmentation.
/// </summary>
public class SegmentationResult
{
    public string CommittedText { get; }
    public string PendingText { get; }
    public bool HasCommitted => !string.IsNullOrEmpty(CommittedText);
    public bool IsSentenceEnd { get; }
    public int RecommendedSilenceMs { get; }

    public SegmentationResult(
        string committedText,
        string pendingText,
        bool isSentenceEnd,
        int recommendedSilenceMs = 1800)
    {
        CommittedText = committedText ?? string.Empty;
        PendingText = pendingText ?? string.Empty;
        IsSentenceEnd = isSentenceEnd;
        RecommendedSilenceMs = recommendedSilenceMs;
    }
}

/// <summary>
/// Text segmentation service enforcing paragraph length thresholds and 4-tier punctuation hierarchy.
/// </summary>
public interface ITextSegmentationService
{
    /// <summary>
    /// Maximum character threshold per segment (e.g. 35, 50, 75, 100, or 0 for unlimited).
    /// </summary>
    int MaxCharacters { get; set; }

    /// <summary>
    /// Progressively processes an incoming streamed token.
    /// </summary>
    SegmentationResult ProcessToken(string incomingToken, bool isFinal);

    /// <summary>
    /// Chunks complete text into segments respecting character threshold and 4-tier punctuation hierarchy.
    /// </summary>
    IReadOnlyList<string> ChunkText(string input, int maxCharacters);

    /// <summary>
    /// Aligns source and target bilingual text chunks proportionately.
    /// </summary>
    (IReadOnlyList<string> SourceChunks, IReadOnlyList<string> TargetChunks) AlignChunks(
        string sourceText,
        string targetText,
        int maxCharacters);

    /// <summary>
    /// Determines recommended silence timeout (800ms for terminal sentence, 1800ms for pause).
    /// </summary>
    int GetRecommendedSilenceInterval(string text);

    /// <summary>
    /// Clears any pending accumulated tokens.
    /// </summary>
    void Reset();
}
