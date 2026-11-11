using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Milestone 3 (M3) Challenger 2 Adversarial Stress Test Suite:
/// Desktop Floating Lyrics Overlay Window.
/// 
/// Adversarially probes:
/// 1. Font Size Scaling Boundary Attacks (extreme inputs, NaN, Infinity, sequence invariant [14.0, 64.0]).
/// 2. Subtitle Text Injection Stress (10,000+ unbroken chars, complex ZWJ emojis, skin tones, flags, CJK Ext B-G, RTL, control codes \0, \r\n, \t, \b).
/// 3. Opacity State Cycle Machine (rapid cycles, 100 concurrent tasks race condition, exact color specs, negative normalization).
/// 4. Streaming Subtitle Burst Simulation (100+ turns/sec high-frequency bursts, thread-safe consumer model, data binding stability).
/// 5. Multi-Monitor Coordinates and Win32 Hit-Test Edge/Corner Precision (signed coordinates, negative secondary monitors, NaN/Inf guards).
/// </summary>
public class M3_Challenger2_StressTests
{
    private const double MinTargetFont = Win32Constants.MinFontSize; // 14.0
    private const double MaxTargetFont = Win32Constants.MaxFontSize; // 64.0

    #region 1. Font Size Scaling Boundary Attacks

    [Theory]
    [InlineData(24.0, 100.0, 64.0)]               // Extreme increment clamps at max
    [InlineData(24.0, -500.0, 14.0)]              // Extreme decrement clamps at min
    [InlineData(24.0, 0.0, 24.0)]                 // Zero delta maintains current size
    [InlineData(14.0, -100.0, 14.0)]              // Clamped min decrement stays min
    [InlineData(64.0, 100.0, 64.0)]               // Clamped max increment stays max
    [InlineData(24.0, double.PositiveInfinity, 64.0)] // Positive infinity clamps to max
    [InlineData(24.0, double.NegativeInfinity, 14.0)] // Negative infinity clamps to min
    public void AdjustFontSize_ExtremeInputs_ClampsStrictlyWithinBounds(double current, double delta, double expected)
    {
        double result = NativeMethods.AdjustFontSize(current, delta, MinTargetFont, MaxTargetFont);
        Assert.Equal(expected, result);
        Assert.InRange(result, MinTargetFont, MaxTargetFont);
    }

    [Fact]
    public void AdjustFontSize_NaNInputs_NeverProducesNaNAndPreservesBounds()
    {
        // 1. Delta is NaN: size should remain at current (clamped)
        double r1 = NativeMethods.AdjustFontSize(24.0, double.NaN, MinTargetFont, MaxTargetFont);
        Assert.False(double.IsNaN(r1));
        Assert.Equal(24.0, r1);

        // 2. Current is NaN: size should fall back to min and apply delta
        double r2 = NativeMethods.AdjustFontSize(double.NaN, 2.0, MinTargetFont, MaxTargetFont);
        Assert.False(double.IsNaN(r2));
        Assert.Equal(16.0, r2); // 14.0 + 2.0

        // 3. Both are NaN: size should safely return min
        double r3 = NativeMethods.AdjustFontSize(double.NaN, double.NaN, MinTargetFont, MaxTargetFont);
        Assert.False(double.IsNaN(r3));
        Assert.Equal(MinTargetFont, r3);
    }

    [Fact]
    public void ClampFontSize_ExtremeInputs_AlwaysReturnsFiniteWithinBounds()
    {
        Assert.Equal(MinTargetFont, NativeMethods.ClampFontSize(double.NaN, MinTargetFont, MaxTargetFont));
        Assert.Equal(MaxTargetFont, NativeMethods.ClampFontSize(double.PositiveInfinity, MinTargetFont, MaxTargetFont));
        Assert.Equal(MinTargetFont, NativeMethods.ClampFontSize(double.NegativeInfinity, MinTargetFont, MaxTargetFont));
        Assert.Equal(MinTargetFont, NativeMethods.ClampFontSize(-1000.0, MinTargetFont, MaxTargetFont));
        Assert.Equal(MaxTargetFont, NativeMethods.ClampFontSize(1000.0, MinTargetFont, MaxTargetFont));
        Assert.Equal(30.0, NativeMethods.ClampFontSize(30.0, MinTargetFont, MaxTargetFont));
    }

    [Fact]
    public void AdjustFontSize_1000StepRandomWalk_InvariantBoundsNeverViolated()
    {
        // Adversarially subject font size to a 1000-step random walk with extreme, boundary, and pathological deltas
        var random = new Random(42);
        double[] pathologicalDeltas =
        [
            100.0, -500.0, 0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            2.0, -2.0, 10.0, -10.0, 0.0001, -0.0001, 1e10, -1e10
        ];

        double currentSize = 24.0;
        for (int i = 0; i < 1000; i++)
        {
            double delta = pathologicalDeltas[random.Next(pathologicalDeltas.Length)];
            currentSize = NativeMethods.AdjustFontSize(currentSize, delta, MinTargetFont, MaxTargetFont);

            // Invariant: MUST NEVER BE NaN, MUST NEVER BE INFINITY, MUST BE IN [14.0, 64.0]
            Assert.False(double.IsNaN(currentSize), $"Step {i}: size became NaN!");
            Assert.False(double.IsInfinity(currentSize), $"Step {i}: size became Infinity!");
            Assert.True(currentSize >= MinTargetFont && currentSize <= MaxTargetFont,
                $"Step {i}: size {currentSize} violated bounds [{MinTargetFont}, {MaxTargetFont}]");
        }
    }

    [Fact]
    public void SecondarySubtitleScaling_Ratio075_MaintainsIndependentBounds()
    {
        // FloatingOverlayWindow scales secondary source subtitle by delta * 0.75 clamped to [10.0, 48.0]
        double delta = 4.0;
        double secondaryDelta = delta * 0.75; // 3.0
        double sourceFont = 16.0;

        double newSourceFont = NativeMethods.AdjustFontSize(sourceFont, secondaryDelta, 10.0, 48.0);
        Assert.Equal(19.0, newSourceFont);

        // Extreme decrement on source subtitle
        double minClamped = NativeMethods.AdjustFontSize(sourceFont, -100.0 * 0.75, 10.0, 48.0);
        Assert.Equal(10.0, minClamped);

        // Extreme increment on source subtitle
        double maxClamped = NativeMethods.AdjustFontSize(sourceFont, 100.0 * 0.75, 10.0, 48.0);
        Assert.Equal(48.0, maxClamped);
    }

    [Fact]
    public void DirectWriteTextModel_ExtremeFontOperations_DoesNotThrow()
    {
        var model = new DirectWriteTextModel("測試字幕", 24.0);

        // Extreme increments
        model.IncreaseFontSize(100.0);
        Assert.Equal(DirectWriteTextModel.MaxFontSize, model.FontSize);

        // Extreme decrements
        model.DecreaseFontSize(500.0);
        Assert.Equal(DirectWriteTextModel.MinFontSize, model.FontSize);

        // Re-measure after extreme sizing
        var (width, height, lines) = model.MeasureTextGeometry();
        Assert.True(width > 0);
        Assert.True(height > 0);
        Assert.True(lines >= 1);
    }

    #endregion

    #region 2. Subtitle Text Injection Stress

    [Fact]
    public void Subtitle_10000CharactersUnbroken_MeasuresAndChunksSafely()
    {
        // 10,000 ASCII chars without spaces
        string longAscii = new string('A', 10000);
        var modelAscii = new DirectWriteTextModel(longAscii, 24.0);

        var (w1, h1, l1) = modelAscii.MeasureTextGeometry();
        Assert.True(w1 > 0);
        Assert.True(h1 > 0);
        Assert.True(l1 > 100);

        // 10,000 CJK characters without spaces
        string longCjk = new string('繁', 10000);
        var modelCjk = new DirectWriteTextModel(longCjk, 24.0);

        var (w2, h2, l2) = modelCjk.MeasureTextGeometry();
        Assert.True(w2 > 0);
        Assert.True(h2 > 0);
        Assert.True(l2 > 100);

        // Text segmentation must split 10,000 chars without infinite loop
        var segmenter = new TextSegmentationService { MaxCharacters = 50 };
        var chunks = segmenter.ChunkText(longCjk, 50);

        Assert.Equal(200, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(50, c.Length));
        Assert.Equal(longCjk, string.Concat(chunks));
    }

    [Fact]
    public void Subtitle_ComplexUnicode_ZeroWidthJoinerFamilyEmojis_PreservedIntact()
    {
        // ZWJ Family emojis:
        // 👨‍👩‍👧‍👦 (Family: Man, Woman, Girl, Boy) - 7 codepoints
        // 👩‍👩‍👦‍👦 (Family: Woman, Woman, Boy, Boy)
        // 🧑‍🧑‍🧒‍🧒 (Family: Adult, Adult, Child, Child)
        string zwjText = "👨‍👩‍👧‍👦 👩‍👩‍👦‍👦 🧑‍🧑‍🧒‍🧒 👨‍👩‍👧";
        var elements = DirectWriteTextModel.BreakIntoTextElements(zwjText);

        // 4 emojis + 3 spaces = 7 grapheme clusters
        Assert.Equal(7, elements.Count);
        Assert.Equal("👨‍👩‍👧‍👦", elements[0]);
        Assert.Equal(" ", elements[1]);
        Assert.Equal("👩‍👩‍👦‍👦", elements[2]);
        Assert.Equal(" ", elements[3]);
        Assert.Equal("🧑‍🧑‍🧒‍🧒", elements[4]);

        var model = new DirectWriteTextModel(zwjText);
        var (w, h, lines) = model.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.True(h > 0);
    }

    [Fact]
    public void Subtitle_SkinToneModifiersAndMultiCodepointFlags_HandledAsSingleClusters()
    {
        // Skin tones: 👋🏽 (Waving Hand: Medium Skin Tone), 👍🏿, 🧑🏻‍💻, 🧚🏾‍♂️
        // Flags: 🇺🇳 (United Nations - 2 regional indicators), 🇹🇼, 🏴󠁧󠁢󠁳󠁣󠁴󠁿 (Scotland subdiv flag - 7 codepoints)
        string complexText = "👋🏽 👍🏿 🧑🏻‍💻 🧚🏾‍♂️ 🇺🇳 🇹🇼 🏴󠁧󠁢󠁳󠁣󠁴󠁿";
        var elements = DirectWriteTextModel.BreakIntoTextElements(complexText);

        Assert.Contains("👋🏽", elements);
        Assert.Contains("👍🏿", elements);
        Assert.Contains("🇺🇳", elements);
        Assert.Contains("🇹🇼", elements);
        Assert.Contains("🏴󠁧󠁢󠁳󠁣󠁴󠁿", elements);

        var model = new DirectWriteTextModel(complexText);
        var (w, h, l) = model.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.True(h > 0);
    }

    [Fact]
    public void Subtitle_CJKUnifiedIdeographsExtensionBThroughG_PreservedWithoutSurrogateSplits()
    {
        // Extension B: U+2000B (\uD840\uDC0B)
        // Extension C: U+2A700 (\uD869\uDF00)
        // Extension D: U+2B740 (\uD86D\uDF40)
        // Extension E: U+2B820 (\uD86E\uDC20)
        // Extension F: U+2CEB0 (\uD873\uDEB0)
        // Extension G: U+30000 (\uD880\uDC00)
        string cjkRare = "\uD840\uDC0B\uD869\uDF00\uD86D\uDF40\uD86E\uDC20\uD873\uDEB0\uD880\uDC00";

        var elements = DirectWriteTextModel.BreakIntoTextElements(cjkRare);
        Assert.Equal(6, elements.Count);
        Assert.All(elements, elem => Assert.Equal(2, elem.Length)); // Each is a surrogate pair

        // Segmentation across rare CJK characters
        var segmenter = new TextSegmentationService { MaxCharacters = 2 };
        var chunks = segmenter.ChunkText(cjkRare, 2);

        // Must chunk without splitting surrogate pairs in half
        Assert.Equal(6, chunks.Count);
        foreach (var chunk in chunks)
        {
            Assert.Equal(2, chunk.Length);
            Assert.True(char.IsHighSurrogate(chunk[0]));
            Assert.True(char.IsLowSurrogate(chunk[1]));
        }
    }

    [Fact]
    public void Subtitle_RTLAndBidirectionalMixedScripts_MeasuresAndFormatsSafely()
    {
        // Arabic (RTL) + Hebrew (RTL) + English (LTR) + Numbers + Chinese + Emojis
        string bidiText = "مرحبا بالعالم (Welcome) שלום עולם! 12345 測試翻譯 🚀 mixed with Hebrew & Arabic.";

        var model = new DirectWriteTextModel(bidiText);
        var (width, height, lines) = model.MeasureTextGeometry();

        Assert.True(width > 0);
        Assert.True(height > 0);
        Assert.True(lines >= 1);

        var elements = DirectWriteTextModel.BreakIntoTextElements(bidiText);
        Assert.NotEmpty(elements);
        Assert.Contains("🚀", elements);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t\r\n\r\n  ")]
    [InlineData("\0\0\0")]
    [InlineData("Hello\0World\0With\0Nulls")]
    [InlineData("Line1\r\nLine2\tTabbed\bBackspace\fFormFeed\vVertical\aBell")]
    public void Subtitle_ControlCodesAndWhitespace_SafelyHandledWithoutExceptions(string pathologicalText)
    {
        var model = new DirectWriteTextModel(pathologicalText);
        var exception = Record.Exception(() =>
        {
            var (w, h, l) = model.MeasureTextGeometry();
            var elements = DirectWriteTextModel.BreakIntoTextElements(pathologicalText);
        });

        Assert.Null(exception);

        // Turn creation with pathological text
        var turn = new TranslationTurn(pathologicalText, pathologicalText);
        Assert.NotNull(turn.SourceText);
        Assert.NotNull(turn.TargetText);
    }

    [Fact]
    public void Subtitle_NullSourceAndTarget_SafeDefaultsApplied()
    {
        var turn = new TranslationTurn(null!, null!);
        Assert.Equal(string.Empty, turn.SourceText);
        Assert.Equal(string.Empty, turn.TargetText);

        var model = new DirectWriteTextModel(null!);
        var (w, h, l) = model.MeasureTextGeometry();
        Assert.Equal(0.0, w);
        Assert.Equal(0.0, h);
        Assert.Equal(0, l);
    }

    #endregion

    #region 3. Opacity State Cycle Machine Stress

    [Fact]
    public void OpacityMode_ThreeStateTransitions_CycleStrictlyZeroOneTwoZero()
    {
        int mode = 0;
        mode = NativeMethods.GetNextOpacityMode(mode);
        Assert.Equal(1, mode);

        mode = NativeMethods.GetNextOpacityMode(mode);
        Assert.Equal(2, mode);

        mode = NativeMethods.GetNextOpacityMode(mode);
        Assert.Equal(0, mode);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(-2, 1)]
    [InlineData(-3, 0)]
    [InlineData(-999, 0)]
    [InlineData(int.MinValue, 1)] // int.MinValue % 3 = -2 -> 1
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(100, 1)]
    [InlineData(int.MaxValue, 1)] // int.MaxValue % 3 = 1
    public void NormalizeOpacityMode_ExtremeIntegerModes_AlwaysNormalizesToZeroOneTwo(int rawMode, int expectedMode)
    {
        int normalized = NativeMethods.NormalizeOpacityMode(rawMode);
        Assert.Equal(expectedMode, normalized);
        Assert.InRange(normalized, 0, 2);
    }

    [Fact]
    public void OpacityMode_ColorsMatchExactSpecifications()
    {
        // Mode 0: 100% Dark slate (#D00F172A) + Border (#60475569)
        var (bg0, bd0) = NativeMethods.GetOpacityModeColors(0);
        Assert.Equal("#D00F172A", bg0);
        Assert.Equal("#60475569", bd0);

        // Mode 1: 50% Frosted slate (#600F172A) + Border (#30475569)
        var (bg1, bd1) = NativeMethods.GetOpacityModeColors(1);
        Assert.Equal("#600F172A", bg1);
        Assert.Equal("#30475569", bd1);

        // Mode 2: 0% Pure transparent (#00000000) + Border (#00000000)
        var (bg2, bd2) = NativeMethods.GetOpacityModeColors(2);
        Assert.Equal("#00000000", bg2);
        Assert.Equal("#00000000", bd2);
    }

    [Fact]
    public async Task OpacityMode_100ConcurrentTasksCyclingOpacity_NoRaceConditionOrDeadlock()
    {
        // Multi-threaded simulation: 100 concurrent tasks each cycling 100 times
        int currentMode = 0;
        object lockObj = new();
        var observedModes = new ConcurrentBag<int>();

        var tasks = new Task[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    int mode;
                    lock (lockObj)
                    {
                        currentMode = NativeMethods.GetNextOpacityMode(currentMode);
                        mode = currentMode;
                    }

                    // Mode must always be in [0, 2]
                    observedModes.Add(mode);
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(100 * 100, observedModes.Count);
        Assert.All(observedModes, m => Assert.InRange(m, 0, 2));

        // 10,000 cycles % 3 = 10,000 % 3 = 1
        Assert.Equal(1, currentMode);
    }

    [Fact]
    public void GetNextOpacity_DoubleValues_CyclesCorrectlyThroughTransitions()
    {
        // 1.0 -> 0.5 -> 0.0 -> 1.0
        double op = 1.0;
        op = NativeMethods.GetNextOpacity(op);
        Assert.Equal(0.5, op);

        op = NativeMethods.GetNextOpacity(op);
        Assert.Equal(0.0, op);

        op = NativeMethods.GetNextOpacity(op);
        Assert.Equal(1.0, op);

        // Edge values
        Assert.Equal(1.0, NativeMethods.GetNextOpacity(-0.5));
        Assert.Equal(1.0, NativeMethods.GetNextOpacity(double.NaN));
        Assert.Equal(0.5, NativeMethods.GetNextOpacity(0.95));
        Assert.Equal(0.0, NativeMethods.GetNextOpacity(0.45));
    }

    #endregion

    #region 4. Streaming Subtitle Burst Simulation

    [Fact]
    public async Task SubtitleBurst_500RapidTranslationTurns_ProcessedWithoutCorruptionOrLoss()
    {
        // Simulate high-frequency streaming turns arriving at 100+ turns/sec
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };
        var receivedTurns = new ConcurrentQueue<TranslationTurn>();
        var activeSubtitles = new ConcurrentDictionary<string, string>();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int turnCount = 500;

        // Producer tasks simulating Gemini Live streaming callbacks
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            int workerId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < turnCount / 5; j++)
                {
                    int index = workerId * 100 + j;
                    string source = $"Speaker {workerId} turn {j}: Live translated sentence with detail.";
                    string target = $"講者 {workerId} 第 {j} 句：包含細節的即時口譯句子。";

                    var turn = new TranslationTurn(source, target, isFinal: (j % 5 == 0));
                    receivedTurns.Enqueue(turn);

                    // Simulated overlay subtitle state update
                    activeSubtitles["target"] = turn.TargetText;
                    activeSubtitles["source"] = turn.SourceText;

                    // Simulated progressive token chunking
                    var chunkResult = segmenter.ProcessToken(turn.TargetText, turn.IsFinal);
                    Assert.NotNull(chunkResult);
                }
            }, cts.Token));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(turnCount, receivedTurns.Count);
        Assert.NotNull(activeSubtitles["target"]);
        Assert.NotNull(activeSubtitles["source"]);
    }

    [Fact]
    public void SubtitleBurst_AsynchronousSpeechAndTranslationAlignment_AlignsAccurately()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        // Test burst of varied-length source vs target speeches
        string[] sourcePhrases =
        [
            "Hello everyone.",
            "Welcome to the Gemini 3.5 live translation desktop application showcase.",
            "Today we are demonstrating the desktop floating lyrics overlay window.",
            "Thank you for joining us."
        ];

        string[] targetPhrases =
        [
            "大家早安。",
            "歡迎來到 Gemini 3.5 即時口譯桌面應用程式展示會。",
            "今天我們要展示的是桌面懸浮歌詞字幕視窗，支援滑鼠穿透與平滑拉伸。",
            "感謝大家的熱情參與。"
        ];

        for (int i = 0; i < sourcePhrases.Length; i++)
        {
            var (sourceChunks, targetChunks) = segmenter.AlignChunks(sourcePhrases[i], targetPhrases[i], maxCharacters: 35);
            Assert.NotEmpty(sourceChunks);
            Assert.NotEmpty(targetChunks);

            foreach (var tChunk in targetChunks)
            {
                Assert.True(tChunk.Length <= 35, $"Target chunk '{tChunk}' length {tChunk.Length} exceeds 35");
            }
        }
    }

    [Fact]
    public void SubtitleBurst_InterruptedTurnBargeIn_CleansUpStateImmediately()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 35 };

        // 1. Partial speech arriving
        var r1 = segmenter.ProcessToken("正在進行口譯的長句子，突然使用者打斷了發言……", isFinal: false);
        Assert.False(string.IsNullOrEmpty(r1.PendingText));

        // 2. User barges in -> Reset overlay state
        segmenter.Reset();

        // 3. New speech immediately arrives
        var r2 = segmenter.ProcessToken("新的對話開始了。", isFinal: true);
        Assert.True(r2.HasCommitted);
        Assert.Equal("新的對話開始了。", r2.CommittedText);
        Assert.Empty(r2.PendingText);
    }

    #endregion

    #region 5. Multi-Monitor and Hit-Test Boundary Stress

    [Theory]
    // Secondary monitor to the LEFT (X: -1920..0, Y: 0..1080)
    [InlineData(-1500, 200, 600, 150, -1498, 250, Win32Constants.HTLEFT)]
    [InlineData(-1500, 200, 600, 150, -902, 250, Win32Constants.HTRIGHT)]
    [InlineData(-1500, 200, 600, 150, -1200, 202, Win32Constants.HTTOP)]
    [InlineData(-1500, 200, 600, 150, -1200, 348, Win32Constants.HTBOTTOM)]
    [InlineData(-1500, 200, 600, 150, -1498, 202, Win32Constants.HTTOPLEFT)]
    [InlineData(-1500, 200, 600, 150, -902, 202, Win32Constants.HTTOPRIGHT)]
    [InlineData(-1500, 200, 600, 150, -1498, 348, Win32Constants.HTBOTTOMLEFT)]
    [InlineData(-1500, 200, 600, 150, -902, 348, Win32Constants.HTBOTTOMRIGHT)]
    [InlineData(-1500, 200, 600, 150, -1200, 275, Win32Constants.HTCLIENT)]
    [InlineData(-1500, 200, 600, 150, -1501, 250, Win32Constants.HTNOWHERE)]
    // Secondary monitor ABOVE (X: 0..1920, Y: -1080..0)
    [InlineData(100, -800, 600, 150, 102, -750, Win32Constants.HTLEFT)]
    [InlineData(100, -800, 600, 150, 300, -798, Win32Constants.HTTOP)]
    [InlineData(100, -800, 600, 150, 102, -798, Win32Constants.HTTOPLEFT)]
    [InlineData(100, -800, 600, 150, 300, -725, Win32Constants.HTCLIENT)]
    [InlineData(100, -800, 600, 150, 300, -801, Win32Constants.HTNOWHERE)]
    // Secondary 8K monitor (X: 7680, Y: 4320)
    [InlineData(7680, 4320, 960, 170, 7682, 4400, Win32Constants.HTLEFT)]
    [InlineData(7680, 4320, 960, 170, 8638, 4488, Win32Constants.HTBOTTOMRIGHT)]
    [InlineData(7680, 4320, 960, 170, 8160, 4405, Win32Constants.HTCLIENT)]
    public void CalculateHitTest_MultiMonitorCoordinates_ResolvesAccurately(
        double winLeft, double winTop, double winW, double winH,
        double cursorX, double cursorY, int expectedHit)
    {
        int hit = NativeMethods.CalculateHitTest(winLeft, winTop, winW, winH, cursorX, cursorY, 8);
        Assert.Equal(expectedHit, hit);
    }

    [Theory]
    [InlineData(double.NaN, 100.0)]
    [InlineData(100.0, double.NaN)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, 100.0)]
    [InlineData(100.0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity)]
    public void CalculateHitTest_NaNOrInfinityCoordinates_SafelyReturnsHTNOWHERE(double cursorX, double cursorY)
    {
        int hit = NativeMethods.CalculateHitTest(100.0, 100.0, 600.0, 150.0, cursorX, cursorY, 8);
        Assert.Equal(Win32Constants.HTNOWHERE, hit);
    }

    [Fact]
    public void CalculateHitTest_OnePixelBoundaryPrecision_DistinguishesBorderAndClient()
    {
        double left = 100.0, top = 100.0, width = 640.0, height = 140.0;
        int border = 8;

        // Left boundary:
        // left + 7 is border
        Assert.Equal(Win32Constants.HTLEFT, NativeMethods.CalculateHitTest(left, top, width, height, left + 7, top + 50, border));
        // left + 8 is client
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(left, top, width, height, left + 8, top + 50, border));

        // Right boundary:
        // left + width - 8 is border
        Assert.Equal(Win32Constants.HTRIGHT, NativeMethods.CalculateHitTest(left, top, width, height, left + width - 8, top + 50, border));
        // left + width - 9 is client
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(left, top, width, height, left + width - 9, top + 50, border));

        // Top boundary:
        // top + 7 is border
        Assert.Equal(Win32Constants.HTTOP, NativeMethods.CalculateHitTest(left, top, width, height, left + 200, top + 7, border));
        // top + 8 is client
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(left, top, width, height, left + 200, top + 8, border));

        // Bottom boundary:
        // top + height - 8 is border
        Assert.Equal(Win32Constants.HTBOTTOM, NativeMethods.CalculateHitTest(left, top, width, height, left + 200, top + height - 8, border));
        // top + height - 9 is client
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(left, top, width, height, left + 200, top + height - 9, border));
    }

    [Fact]
    public void CalculateHitTest_DegenerateGeometry_NeverThrowsOrCrashes()
    {
        // 0x0 size
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(0, 0, 0, 0, 0, 0, 8));
        Assert.Equal(Win32Constants.HTNOWHERE, NativeMethods.CalculateHitTest(0, 0, 0, 0, 5, 5, 8));

        // Negative dimensions
        Assert.Equal(Win32Constants.HTNOWHERE, NativeMethods.CalculateHitTest(100, 100, -100, -50, 100, 100, 8));

        // Zero border thickness
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(100, 100, 400, 100, 101, 101, 0));

        // Negative border thickness
        Assert.Equal(Win32Constants.HTCLIENT, NativeMethods.CalculateHitTest(100, 100, 400, 100, 101, 101, -5));
    }

    [Fact]
    public void ClickThrough_HighConcurrencyToggle_RemainsBitwiseConsistent()
    {
        // Rapid concurrent toggling of click-through bitwise styles
        long style = Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_TOPMOST;

        Parallel.For(0, 500, _ =>
        {
            long enabled = NativeMethods.EnableClickThroughLong(style);
            Assert.True(NativeMethods.IsClickThroughLong(enabled));
            Assert.True((enabled & Win32Constants.WS_EX_TOOLWINDOW) != 0);
            Assert.True((enabled & Win32Constants.WS_EX_TOPMOST) != 0);

            long disabled = NativeMethods.DisableClickThroughLong(enabled);
            Assert.False(NativeMethods.IsClickThroughLong(disabled));
            Assert.True((disabled & Win32Constants.WS_EX_TOOLWINDOW) != 0);
            Assert.True((disabled & Win32Constants.WS_EX_TOPMOST) != 0);
        });
    }

    #endregion
}
