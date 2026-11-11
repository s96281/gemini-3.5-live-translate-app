using System.Collections.Generic;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

public class TextSegmentationTests
{
    private readonly TextSegmentationService _service = new();

    [Fact]
    public void ChunkText_TerminalPunctuation_SplitsAtPriority1()
    {
        // Sentence has terminal punctuation before 35 chars
        string text = "這是一句繁體中文的測試句子。接下來是第二句話，內容同樣重要！最後這是第三句。";
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35, $"Chunk exceeded max limit 35: '{chunk}' (Length: {chunk.Length})");
        }
        Assert.Contains("這是一句繁體中文的測試句子。", chunks);
    }

    [Fact]
    public void ChunkText_SubClausePunctuation_SplitsAtPriority2WhenNoTerminal()
    {
        // No terminal punctuation in first 35 chars, but has comma
        string text = "人工智慧正在快速改變世界，為人類帶來前所未有的生產力提升，我們必須謹慎應對。";
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35);
        }
        Assert.Equal("人工智慧正在快速改變世界，", chunks[0]);
    }

    [Fact]
    public void ChunkText_WhitespaceWordBoundary_SplitsAtPriority3InEnglish()
    {
        // English sentence with no punctuation, only word boundaries
        string text = "The quick brown fox jumps over the lazy dog and runs through the dense green forest";
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 35);
        }
        // First chunk should not split words in half
        Assert.Equal("The quick brown fox jumps over the", chunks[0]);
    }

    [Fact]
    public void ChunkText_NoPunctuationOrSpace_EnforcesHardPriority4Cut()
    {
        // 100 consecutive characters without spaces or punctuation
        string text = new string('A', 100);
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(35, chunks[0].Length);
        Assert.Equal(35, chunks[1].Length);
        Assert.Equal(30, chunks[2].Length);
    }

    [Fact]
    public void ChunkText_ConsecutivePunctuation_PreservesPunctuationGrouping()
    {
        string text = "真的假的！？難道這一切都是真的嗎？？這太不可思議了！";
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        Assert.Contains("真的假的！？", chunks);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void ChunkText_VariousThresholds_NeverExceedsSpecifiedLimit(int limit)
    {
        string text = "Google Gemini Live API 提供超低延遲的雙向語音與文字串流，" +
                      "能夠以原生多模態架構即時辨識使用者的語音輸入，並將其翻譯為目標語言。" +
                      "這項技術結合了深度神經網路與先進的訊號處理機制，為跨語言溝通帶來革命性的體驗！" +
                      "使用者可以在 Windows 11 上享受流暢的桌面字幕與語音播報服務。";

        var chunks = _service.ChunkText(text, maxCharacters: limit);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= limit, $"Chunk length {chunk.Length} exceeded limit {limit}: '{chunk}'");
        }
    }

    [Fact]
    public void ChunkText_EmptyOrWhitespace_ReturnsEmptyList()
    {
        Assert.Empty(_service.ChunkText("", 50));
        Assert.Empty(_service.ChunkText("   \t\n  ", 50));
    }

    [Fact]
    public void ProcessToken_StreamingTokens_AccumulatesAndDispatchesCorrectly()
    {
        _service.Reset();
        _service.MaxCharacters = 35;

        var r1 = _service.ProcessToken("早安，", isFinal: false);
        Assert.False(r1.HasCommitted);
        Assert.Equal("早安，", r1.PendingText);

        var r2 = _service.ProcessToken("今天的天氣真好！", isFinal: false);
        // Has terminal punctuation and sentence completed
        Assert.True(r2.HasCommitted || r2.IsSentenceEnd);

        var r3 = _service.ProcessToken("", isFinal: true);
        Assert.True(r3.IsSentenceEnd);
        Assert.Empty(r3.PendingText);
    }

    [Fact]
    public void ProcessToken_Reset_ClearsPendingBuffer()
    {
        _service.Reset();
        _service.ProcessToken("未完成的句子", isFinal: false);
        _service.Reset();

        var r = _service.ProcessToken("", isFinal: false);
        Assert.Empty(r.PendingText);
        Assert.False(r.HasCommitted);
    }

    [Fact]
    public void GetRecommendedSilenceInterval_ReturnsAccurateValues()
    {
        Assert.Equal(800, _service.GetRecommendedSilenceInterval("這句話結束了。"));
        Assert.Equal(800, _service.GetRecommendedSilenceInterval("Is that correct?"));
        Assert.Equal(800, _service.GetRecommendedSilenceInterval("太棒了！"));
        Assert.Equal(1800, _service.GetRecommendedSilenceInterval("這句話還沒結束，中間暫停"));
        Assert.Equal(1800, _service.GetRecommendedSilenceInterval("Waiting for input"));
    }

    [Fact]
    public void ChunkText_200ConsecutiveCharactersWithoutPunctuation_SplitsIntoEvenThresholdChunks()
    {
        string text = new string('X', 200);
        var chunks = _service.ChunkText(text, maxCharacters: 50);

        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(50, c.Length));
    }

    [Fact]
    public void ChunkText_ContinuousPunctuationString_PreservesStringGracefully()
    {
        string text = "。！？……";
        var chunks = _service.ChunkText(text, maxCharacters: 35);
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void ChunkText_MixedScriptPunctuation_HandlesEnglishAndFullwidthChineseCommas()
    {
        string text = "First part, second part，third part: fourth part；final part.";
        var chunks = _service.ChunkText(text, maxCharacters: 50);
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.Length <= 50));
    }

    [Fact]
    public void ChunkText_WithUnicodeEmojis_SplitsSafelyWithoutCorruptingGlyphs()
    {
        string text = "這是一個包含多種表情符號的測試🎉✨，希望即時字幕能夠正常渲染🚀，不要出現任何缺字或亂碼！";
        var chunks = _service.ChunkText(text, maxCharacters: 35);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.Length <= 35));
    }
}
