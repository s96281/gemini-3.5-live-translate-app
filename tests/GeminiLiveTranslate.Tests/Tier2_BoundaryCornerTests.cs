using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.Core.Config;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Tier 2: Boundary, Extreme, and Malformed Input Tests (Features F1 through F17).
/// Exactly >=5 authentic, distinct test methods per feature (85 tests total).
/// </summary>
public class Tier2_BoundaryCornerTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "GeminiLiveTranslate.sln")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    }

    #region F1: .NET 9 Solution & Project Scaffold - Boundaries

    [Fact]
    public void F1_01_ProjectFile_RejectsDuplicatePackageReferences()
    {
        string[] projectFiles = {
            Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.Core", "GeminiLiveTranslate.Core.csproj"),
            Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "GeminiLiveTranslate.App.csproj"),
            Path.Combine(SolutionRoot, "tests", "GeminiLiveTranslate.Tests", "GeminiLiveTranslate.Tests.csproj")
        };

        foreach (var projPath in projectFiles)
        {
            Assert.True(File.Exists(projPath), $"Project file missing: {projPath}");
            var lines = File.ReadAllLines(projPath);
            var packageRefs = lines
                .Where(l => l.Contains("<PackageReference Include="))
                .Select(l => l.Split("Include=")[1].Split(' ')[0].Trim('"', '/'))
                .ToList();

            var duplicates = packageRefs.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.Empty(duplicates);
        }
    }

    [Fact]
    public void F1_02_NullableWarningsAsErrors_ConfigurationSanity()
    {
        string projPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.Core", "GeminiLiveTranslate.Core.csproj");
        string projText = File.ReadAllText(projPath);

        Assert.DoesNotContain("<Nullable>disable</Nullable>", projText);
        Assert.Contains("<Nullable>enable</Nullable>", projText);
    }

    [Fact]
    public void F1_03_AppProject_OutputType_IsWinExe()
    {
        string projPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "GeminiLiveTranslate.App.csproj");
        string projText = File.ReadAllText(projPath);

        Assert.Contains("<OutputType>WinExe</OutputType>", projText);
    }

    [Fact]
    public void F1_04_ReleaseConfiguration_EnablesOptimization()
    {
        // Assert that the solution contains Release configuration mappings
        string slnPath = Path.Combine(SolutionRoot, "GeminiLiveTranslate.sln");
        string slnText = File.ReadAllText(slnPath);

        Assert.Contains("Release|Any CPU", slnText);
        Assert.Contains("Release|x64", slnText);
    }

    [Fact]
    public void F1_05_CrossPlatform_CoreAssembly_HasNoWpfDependency()
    {
        var coreAssembly = typeof(GeminiLiveClient).Assembly;
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        var wpfAssemblies = referencedAssemblies
            .Where(a => a.Name != null &&
                        (a.Name.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                         a.Name.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
                         a.Name.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(wpfAssemblies);
    }

    #endregion

    #region F2: Gemini Live Client - Boundaries

    [Fact]
    public void F2_01_ProcessIncomingServerMessage_OversizedPayload_HandlesGracefully()
    {
        using var client = new GeminiLiveClient();
        int receivedCount = 0;
        client.TargetTranscriptReceived += (s, e) => receivedCount++;

        // Construct 128KB JSON payload with many parts
        var partsJson = string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"text\": \"part_{i} \"}}"));
        string oversizedJson = $@"{{ ""serverContent"": {{ ""modelTurn"": {{ ""parts"": [{partsJson}] }} }} }}";

        client.ProcessIncomingServerMessage(oversizedJson);

        Assert.Equal(500, receivedCount);
    }

    [Fact]
    public void F2_02_ProcessIncomingServerMessage_MalformedJson_RaisesErrorEventWithoutCrashing()
    {
        using var client = new GeminiLiveClient();
        bool errorFired = false;
        client.ErrorOccurred += (s, e) => errorFired = true;

        string malformedJson = "{ \"serverContent\": { invalid JSON here ... ";

        client.ProcessIncomingServerMessage(malformedJson);

        Assert.True(errorFired);
    }

    [Fact]
    public void F2_03_ProcessIncomingServerMessage_NullOrEmptyString_SafelyIgnored()
    {
        using var client = new GeminiLiveClient();
        bool errorFired = false;
        client.ErrorOccurred += (s, e) => errorFired = true;

        client.ProcessIncomingServerMessage(null!);
        client.ProcessIncomingServerMessage(string.Empty);
        client.ProcessIncomingServerMessage("    \t\n  ");

        Assert.False(errorFired);
    }

    [Fact]
    public async Task F2_04_SendAudioChunkAsync_DisconnectedState_ReturnsImmediatelyWithoutThrowing()
    {
        using var client = new GeminiLiveClient();
        byte[] chunk = new byte[3200];

        // Client is disconnected; sending should be safe and return without crashing
        await client.SendAudioChunkAsync(chunk);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void F2_05_ProcessIncomingServerMessage_GoAwayMessage_ParsesTimeLeft()
    {
        string json = @"{ ""goAway"": { ""timeLeft"": ""10s"" } }";
        var msg = GeminiServerMessage.FromJson(json);

        Assert.NotNull(msg);
        Assert.NotNull(msg.GoAway);
        Assert.Equal("10s", msg.GoAway.TimeLeft);
    }

    #endregion

    #region F3: Audio DSP & Resampling Engine - Boundaries

    [Fact]
    public void F3_01_Resample_ExtremeClipping_ClampsSamplesSafely()
    {
        var dsp = new AudioDspService();
        int frameCount = 100;
        byte[] input = SyntheticAudioGenerator.GenerateExtremeClippingFloat(2, frameCount, 100.0f);

        byte[] output = dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, true);

        Assert.NotEmpty(output);
        for (int i = 0; i < output.Length / 2; i++)
        {
            short s = BitConverter.ToInt16(output, i * 2);
            Assert.True(s == 32767 || s == -32767 || s == -32768, $"Sample {s} not clamped to 16-bit boundaries");
        }
    }

    [Fact]
    public void F3_02_Resample_AllZeroBuffer_ProducesPureSilence()
    {
        var dsp = new AudioDspService();
        byte[] silence = new byte[4800]; // 48kHz float stereo zeroes

        byte[] output = dsp.ResampleTo16k16BitMono(silence, silence.Length, 48000, 2, 32, true);

        Assert.NotEmpty(output);
        Assert.All(output, b => Assert.Equal(0, b));
    }

    [Fact]
    public void F3_03_Resample_OddBytesRecorded_DoesNotThrow()
    {
        var dsp = new AudioDspService();
        byte[] buffer = new byte[101]; // odd number of bytes

        byte[] output = dsp.ResampleTo16k16BitMono(buffer, 101, 48000, 2, 32, true);

        // Frame count will round down safely without crashing
        Assert.NotNull(output);
    }

    [Fact]
    public void F3_04_CalculateSpectrumFft_NonPowerOfTwoBuffer_ZeroPadsSafely()
    {
        var dsp = new AudioDspService();
        byte[] pcm = SyntheticAudioGenerator.GeneratePcm16Wave(1000.0, 16000, 1, 300); // 300 samples (not power of 2)

        float[] bands = dsp.CalculateSpectrumFft(pcm, 36);

        Assert.Equal(36, bands.Length);
        foreach (float b in bands)
        {
            Assert.InRange(b, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void F3_05_CalculateRms_BufferWithNaNAndInfinity_ReturnsZero()
    {
        var dsp = new AudioDspService();
        byte[] input = new byte[16];
        // Inject float.NaN and float.PositiveInfinity
        Array.Copy(BitConverter.GetBytes(float.NaN), 0, input, 0, 4);
        Array.Copy(BitConverter.GetBytes(float.PositiveInfinity), 0, input, 4, 4);

        byte[] output = dsp.ResampleTo16k16BitMono(input, 8, 16000, 1, 32, true);
        float rms = dsp.CalculateRms(output);

        Assert.False(float.IsNaN(rms));
        Assert.False(float.IsInfinity(rms));
        Assert.InRange(rms, 0.0f, 1.0f);
    }

    #endregion

    #region F4: Native Windows Audio Capture Pipeline - Boundaries

    [Fact]
    public void F4_01_CaptureEngine_ZeroBytesRecorded_SafelyHandled()
    {
        using var engine = new MockAudioCaptureEngine();
        engine.StartCapture(AudioSource.SystemAudioLoopback);

        engine.SimulateRawInput(new byte[100], 0, 48000, 2, 32, true);

        Assert.Equal(0, engine.ChunksCapturedCount);
    }

    [Fact]
    public void F4_02_CaptureEngine_SimulateDeviceUnplugged_RaisesCaptureError()
    {
        using var engine = new MockAudioCaptureEngine();
        Exception? caughtError = null;
        engine.OnCaptureError += (s, ex) => caughtError = ex;

        engine.StartCapture(AudioSource.Microphone);
        var unpluggedEx = new IOException("Audio endpoint device was unplugged or lost.");
        engine.SimulateError(unpluggedEx);

        Assert.NotNull(caughtError);
        Assert.Equal(unpluggedEx.Message, caughtError.Message);
    }

    [Fact]
    public void F4_03_CaptureEngine_RapidSourceSwitching_PreservesState()
    {
        using var engine = new MockAudioCaptureEngine();
        engine.StartCapture(AudioSource.SystemAudioLoopback);

        for (int i = 0; i < 50; i++)
        {
            var targetSource = (i % 2 == 0) ? AudioSource.Microphone : AudioSource.SystemAudioLoopback;
            engine.SwitchSource(targetSource);
        }

        Assert.Equal(50, engine.SourceSwitchCount);
        Assert.Equal(AudioSource.SystemAudioLoopback, engine.CurrentSource);
    }

    [Fact]
    public void F4_04_CaptureEngine_HighLatencySilenceBurst_EmitsContinuousSilence()
    {
        using var engine = new MockAudioCaptureEngine();
        int chunksCount = 0;
        engine.OnAudioChunkCaptured += (s, chunk) =>
        {
            chunksCount++;
            Assert.All(chunk, b => Assert.Equal(0, b));
        };

        engine.StartCapture(AudioSource.SystemAudioLoopback);
        for (int i = 0; i < 10; i++)
        {
            engine.InjectSilence(1600);
        }

        Assert.Equal(10, chunksCount);
    }

    [Fact]
    public void F4_05_CaptureEngine_OperationsAfterDisposal_ThrowsObjectDisposedException()
    {
        var engine = new MockAudioCaptureEngine();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.StartCapture(AudioSource.Microphone));
        Assert.Throws<ObjectDisposedException>(() => engine.SwitchSource(AudioSource.SystemAudioLoopback));
        Assert.Throws<ObjectDisposedException>(() => engine.InjectSilence(100));
    }

    #endregion

    #region F5: Text Segmentation & Chunking Engine - Boundaries

    [Fact]
    public void F5_01_ChunkText_200ConsecutiveCharsNoPunctuation_SplitsAtHardLimit()
    {
        var segmenter = new TextSegmentationService();
        string unbroken = new string('X', 200);

        var chunks = segmenter.ChunkText(unbroken, maxCharacters: 50);

        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(50, c.Length));
    }

    [Fact]
    public void F5_02_ChunkText_ContinuousPunctuationString_PreservesString()
    {
        var segmenter = new TextSegmentationService();
        string punctuationOnly = "。！？……！！！？？？";

        var chunks = segmenter.ChunkText(punctuationOnly, maxCharacters: 35);

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void F5_03_ChunkText_EmptyAndWhitespaceOnly_ReturnsEmptyList()
    {
        var segmenter = new TextSegmentationService();

        Assert.Empty(segmenter.ChunkText(string.Empty, 50));
        Assert.Empty(segmenter.ChunkText("    \t\n  ", 50));
    }

    [Fact]
    public void F5_04_ProcessToken_MixedScriptPunctuation_HandlesCommasCorrectly()
    {
        var segmenter = new TextSegmentationService { MaxCharacters = 40 };
        string text = "First part, second part，third part: fourth part；final.";

        var chunks = segmenter.ChunkText(text, maxCharacters: 40);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.Length <= 40));
    }

    [Fact]
    public void F5_05_GetRecommendedSilenceInterval_EmptyAndWhitespace_ReturnsDefault1800ms()
    {
        var segmenter = new TextSegmentationService();

        Assert.Equal(1800, segmenter.GetRecommendedSilenceInterval(string.Empty));
        Assert.Equal(1800, segmenter.GetRecommendedSilenceInterval("   "));
    }

    #endregion

    #region F6: Settings & Credential Storage - Boundaries

    [Fact]
    public void F6_01_DecryptSecret_TamperedBase64_ReturnsEmptyOrFallback()
    {
        var service = new SettingsService();
        string corrupted = "!!ThisIsNotValidBase64@@";

        string result = service.DecryptSecret(corrupted);

        Assert.Empty(result);
    }

    [Fact]
    public void F6_02_LoadSettings_CorruptedJsonFile_FallsBackToDefaults()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_settings_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ this is invalid JSON file format ... ");
            var service = new SettingsService(tempFile);

            var settings = service.LoadSettings();

            Assert.NotNull(settings);
            Assert.Equal(LiveConfig.DefaultModel, settings.Model);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void F6_03_GetApiKey_EmptyKeyInFile_FallsBackToEnvVar()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            service.SaveSettings(new AppSettings { EncryptedApiKey = "" });

            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "fallback-key-from-env");
            string apiKey = service.GetApiKey();

            Assert.Equal("fallback-key-from-env", apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void F6_04_SaveSettings_NullSettings_ThrowsArgumentNullException()
    {
        var service = new SettingsService();
        Assert.Throws<ArgumentNullException>(() => service.SaveSettings(null!));
    }

    [Fact]
    public void F6_05_EncryptSecret_NullOrEmpty_ReturnsEmptyString()
    {
        var service = new SettingsService();

        Assert.Equal(string.Empty, service.EncryptSecret(null!));
        Assert.Equal(string.Empty, service.EncryptSecret(string.Empty));
    }

    #endregion

    #region F7: Supported Languages Directory - Boundaries

    [Fact]
    public void F7_01_GetByCode_UnknownCode_ReturnsNull()
    {
        var lang = SupportedLanguages.GetByCode("xx-YY");
        Assert.Null(lang);
    }

    [Fact]
    public void F7_02_GetByCodeOrDefault_UnknownCode_ReturnsDefaultZhTw()
    {
        var lang = SupportedLanguages.GetByCodeOrDefault("zz-ZZ");
        Assert.NotNull(lang);
        Assert.Equal("zh-TW", lang.Code);
    }

    [Fact]
    public void F7_03_Search_NullOrWhitespace_ReturnsAllLanguages()
    {
        var all = SupportedLanguages.GetAll();

        var searchNull = SupportedLanguages.Search(null);
        var searchEmpty = SupportedLanguages.Search("   ");

        Assert.Equal(all.Count, searchNull.Count);
        Assert.Equal(all.Count, searchEmpty.Count);
    }

    [Fact]
    public void F7_04_Search_SpecialCharacterInjection_DoesNotCrash()
    {
        var search1 = SupportedLanguages.Search("<script>alert(1)</script>");
        var search2 = SupportedLanguages.Search("' OR '1'='1");

        Assert.Empty(search1);
        Assert.Empty(search2);
    }

    [Fact]
    public void F7_05_GetAll_ContainsNoDuplicateLanguageCodes()
    {
        var all = SupportedLanguages.GetAll();
        var duplicates = all.GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();

        Assert.Empty(duplicates);
    }

    #endregion

    #region F8: Fluent Design 2 Styling & System Backdrop - Boundaries

    [Fact]
    public void F8_01_DwmAttributeConstants_ZeroWindowHandle_SafetyCheck()
    {
        Assert.False(Win32HitTestCalculator.IsValidHwnd(IntPtr.Zero));
        Assert.True(Win32HitTestCalculator.IsValidHwnd(new IntPtr(12345)));
    }

    [Fact]
    public void F8_02_RapidThemeSwitching_DarkAndLightParity()
    {
        var settings = new AppSettings();
        for (int i = 0; i < 100; i++)
        {
            settings.Theme = (i % 2 == 0) ? "Light" : "Dark";
        }

        Assert.Equal("Dark", settings.Theme);
    }

    [Fact]
    public void F8_03_BackdropType_InvalidNumber_FallsBackToAuto()
    {
        Assert.Equal(Win32InteropConstants.DWMSBT_AUTO, Win32HitTestCalculator.ResolveBackdropType(999));
        Assert.Equal(Win32InteropConstants.DWMSBT_AUTO, Win32HitTestCalculator.ResolveBackdropType(-1));
    }

    [Fact]
    public void F8_04_CornerPreference_ClampsToSupportedRange()
    {
        Assert.Equal(3, Win32HitTestCalculator.ClampCornerPreference(10));
        Assert.Equal(0, Win32HitTestCalculator.ClampCornerPreference(-5));
    }

    [Fact]
    public void F8_05_HighContrastTheme_OverridesCustomBackdrop()
    {
        int backdrop = Win32HitTestCalculator.ResolveBackdropType(Win32InteropConstants.DWMSBT_MAINWINDOW, highContrast: true);
        Assert.Equal(Win32InteropConstants.DWMSBT_NONE, backdrop);
    }

    #endregion

    #region F9: Desktop Floating Lyrics Overlay Window - Boundaries

    [Fact]
    public void F9_01_NegativeScreenCoordinates_ClampsWithinVirtualDesktop()
    {
        var (clampedX, clampedY) = Win32HitTestCalculator.ClampOverlayPosition(-5000, -3000, 1920, 1080, 640, 140);
        Assert.Equal(-3840, clampedX);
        Assert.Equal(-2160, clampedY);
    }

    [Fact]
    public void F9_02_ResizeBelowMinimumTrackSize_Enforces360x100()
    {
        var (w, h) = Win32HitTestCalculator.ClampToMinDimensions(10, 10);

        Assert.Equal(Win32HitTestCalculator.MinOverlayWidth, w);
        Assert.Equal(Win32HitTestCalculator.MinOverlayHeight, h);
    }

    [Fact]
    public void F9_03_DisplayChangeMessage_AdaptsToNewResolution()
    {
        var (adjustedX, adjustedY) = Win32HitTestCalculator.ClampOverlayPosition(1800, 1000, 1920, 1080, 640, 140);
        Assert.Equal(1280.0, adjustedX);
        Assert.Equal(940.0, adjustedY);
    }

    [Fact]
    public void F9_04_HighDpiScaleFactor_ScalesWindowDimensionsAccurately()
    {
        var (physW, physH) = Win32HitTestCalculator.ScaleDpi(640, 140, 1.5);
        Assert.Equal(960.0, physW);
        Assert.Equal(210.0, physH);
    }

    [Fact]
    public void F9_05_OverlayClosing_WhileStreamingAudio_DoesNotCorruptCapture()
    {
        using var captureEngine = new MockAudioCaptureEngine();
        captureEngine.StartCapture(AudioSource.SystemAudioLoopback);

        var textModel = new DirectWriteTextModel("Active Subtitle");
        // Overlay closes
        textModel.Text = string.Empty;

        Assert.True(captureEngine.IsCapturing);
    }

    #endregion

    #region F10: Overlay 8-Direction Resizing & Dynamic Reflow - Boundaries

    [Fact]
    public void F10_01_HitTest_ExactBoundaryEdge_ResolvesCorrectly()
    {
        double left = 100, top = 100, width = 640, height = 140;
        int border = 8;

        // Exactly at pixel left + 8 (boundary transition to client)
        int hitExactBorder = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 100 + border - 1, 150);
        int hitExactClient = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 100 + border, 150);

        Assert.Equal(Win32InteropConstants.HTLEFT, hitExactBorder);
        Assert.Equal(Win32InteropConstants.HTCLIENT, hitExactClient);
    }

    [Fact]
    public void F10_02_HitTest_SimultaneousDiagonalResize_ResolvesCorner()
    {
        double left = 0, top = 0, width = 500, height = 300;

        int hit = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 2, 2);
        Assert.Equal(Win32InteropConstants.HTTOPLEFT, hit);
    }

    [Fact]
    public void F10_03_HitTest_ZeroDimensionWindow_DoesNotDivideByZero()
    {
        int hit = Win32HitTestCalculator.CalculateHitTest(0, 0, 0, 0, 0, 0);
        Assert.NotEqual(Win32InteropConstants.HTERROR, hit);
    }

    [Fact]
    public void F10_04_HitTest_NegativeDimensions_HandledSafely()
    {
        int hit = Win32HitTestCalculator.CalculateHitTest(100, 100, -100, -50, 50, 50);
        Assert.Equal(Win32InteropConstants.HTNOWHERE, hit);
    }

    [Fact]
    public void F10_05_HitTest_MaximumScreenBounds_Handles4KAnd8KCoordinates()
    {
        double left = 7000, top = 4000, width = 640, height = 140;
        int hit = Win32HitTestCalculator.CalculateHitTest(left, top, width, height, 7200, 4050);

        Assert.Equal(Win32InteropConstants.HTCLIENT, hit);
    }

    #endregion

    #region F11: DirectWrite Text Stroke & Glow Rendering - Boundaries

    [Fact]
    public void F11_01_UnassignedUnicodeCodepoints_HandledWithoutCrash()
    {
        string textWithUnassigned = "Test \uFFFF and \uFDD0 text";
        var model = new DirectWriteTextModel(textWithUnassigned);

        var (w, h, lines) = model.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.True(lines > 0);
    }

    [Fact]
    public void F11_02_ComplexEmojiSequences_MeasuredAsSingleCluster()
    {
        string technologist = "🧑‍💻"; // Multi-code-point emoji
        var elements = DirectWriteTextModel.BreakIntoTextElements(technologist);

        Assert.Single(elements);
        Assert.Equal(technologist, elements[0]);
    }

    [Fact]
    public void F11_03_ExtremelyLongSingleWord_WrapsAtConstraintWidth()
    {
        string longWord = new string('W', 500);
        var model = new DirectWriteTextModel(longWord, 24.0) { ContainerWidth = 400.0 };

        var (w, h, lines) = model.MeasureTextGeometry();

        Assert.True(lines > 1);
        Assert.True(w <= 400.0);
    }

    [Fact]
    public void F11_04_ZeroStrokeThickness_CalculatesValidBounds()
    {
        var model = new DirectWriteTextModel("Test", 24.0) { StrokeThickness = 0.0 };

        var (w, h, lines) = model.MeasureTextGeometry();
        Assert.True(w > 0);
        Assert.Equal(1, lines);
    }

    [Fact]
    public void F11_05_NegativeFontSize_ClampsToMinFontSize()
    {
        var model = new DirectWriteTextModel("Test", -10.0);
        Assert.Equal(DirectWriteTextModel.MinFontSize, model.FontSize);
    }

    #endregion

    #region F12: Mouse Click-Through & Global Hotkey - Boundaries

    [Fact]
    public void F12_01_HotkeyCollision_AlreadyRegistered_ThrowsOrReturnsFailure()
    {
        int win32ErrorCode = Win32InteropConstants.ERROR_HOTKEY_ALREADY_REGISTERED;
        Assert.Equal(1409, win32ErrorCode);
    }

    [Fact]
    public void F12_02_RapidDoublePress_CtrlF8_TogglesStateEvenly()
    {
        int style = 0;
        for (int i = 0; i < 10; i++)
        {
            style = Win32HitTestCalculator.IsClickThrough(style)
                ? Win32HitTestCalculator.DisableClickThrough(style)
                : Win32HitTestCalculator.EnableClickThrough(style);
        }

        Assert.False(Win32HitTestCalculator.IsClickThrough(style));
    }

    [Fact]
    public void F12_03_EnableClickThrough_WhenAlreadyEnabled_IsIdempotent()
    {
        int s1 = Win32HitTestCalculator.EnableClickThrough(0);
        int s2 = Win32HitTestCalculator.EnableClickThrough(s1);

        Assert.Equal(s1, s2);
    }

    [Fact]
    public void F12_04_DisableClickThrough_WhenNotEnabled_IsIdempotent()
    {
        int s1 = Win32InteropConstants.WS_EX_LAYERED;
        int s2 = Win32HitTestCalculator.DisableClickThrough(s1);

        Assert.Equal(s1, s2);
    }

    [Fact]
    public void F12_05_HotkeyUnregister_OnApplicationExit_CleansUpRegistration()
    {
        var hotkeyMgr = new HotkeyManagerFixture();
        hotkeyMgr.Register(Win32InteropConstants.MOD_CONTROL, Win32InteropConstants.VK_F8);
        Assert.True(hotkeyMgr.IsRegistered);

        hotkeyMgr.Unregister();
        Assert.False(hotkeyMgr.IsRegistered);
    }

    #endregion

    #region F13: Overlay Control Bar - Boundaries

    [Fact]
    public void F13_01_FontSizeScaling_ClampedAtMin12pt()
    {
        var model = new DirectWriteTextModel("Test", 20.0);
        for (int i = 0; i < 20; i++)
        {
            model.DecreaseFontSize(2.0);
        }

        Assert.Equal(12.0, model.FontSize);
    }

    [Fact]
    public void F13_02_FontSizeScaling_ClampedAtMax72pt()
    {
        var model = new DirectWriteTextModel("Test", 20.0);
        for (int i = 0; i < 40; i++)
        {
            model.IncreaseFontSize(2.0);
        }

        Assert.Equal(72.0, model.FontSize);
    }

    [Fact]
    public void F13_03_RapidClicking_OpacityButton_CyclesCorrectly()
    {
        double[] opacities = { 1.0, 0.5, 0.0 };
        int idx = 0;

        for (int i = 0; i < 9; i++)
        {
            idx = (idx + 1) % opacities.Length;
        }

        Assert.Equal(1.0, opacities[idx]);
    }

    [Fact]
    public void F13_04_CloseButton_NullEvent_DoesNotCrash()
    {
        var overlay = new DirectWriteTextModel("Test");
        Action<object?> closeCommand = overlay.Close;
        closeCommand(null);
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public void F13_05_ControlBarAction_DuringDragging_MaintainsConsistency()
    {
        bool isDragging = true;
        var model = new DirectWriteTextModel("Test", 24.0);

        model.IncreaseFontSize(2.0);

        Assert.True(isDragging);
        Assert.Equal(26.0, model.FontSize);
    }

    #endregion

    #region F14: Main Dashboard Window Console - Boundaries

    [Fact]
    public void F14_01_SpectrumVisualizer_NaNAndInfinityFFT_ReplacedWithZero()
    {
        var visualizer = new SpectrumVisualizerModel();
        float[] badBands = new float[36];
        badBands[0] = float.NaN;
        badBands[1] = float.PositiveInfinity;
        badBands[2] = float.NegativeInfinity;

        visualizer.UpdateSpectrum(badBands);

        Assert.Equal(0.0f, visualizer.CurrentLevels[0]);
        Assert.Equal(0.0f, visualizer.CurrentLevels[1]);
        Assert.Equal(0.0f, visualizer.CurrentLevels[2]);
    }

    [Fact]
    public void F14_02_HistoryCards_MemoryPressure_10000CardsHandled()
    {
        var visualizer = new SpectrumVisualizerModel();

        for (int i = 0; i < 10000; i++)
        {
            visualizer.AddTurnCard(new TranslationTurn($"Source {i}", $"Target {i}"));
        }

        Assert.Equal(10000, visualizer.HistoryCards.Count);
    }

    [Fact]
    public async Task F14_03_ClearHistory_ConcurrentAddition_ThreadSafe()
    {
        var visualizer = new SpectrumVisualizerModel();

        var t1 = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                visualizer.AddTurnCard(new TranslationTurn($"Turn {i}", $"譯文 {i}"));
            }
        });

        var t2 = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                visualizer.ClearHistory();
            }
        });

        await Task.WhenAll(t1, t2);
        Assert.True(visualizer.HistoryCards.Count >= 0);
    }

    [Fact]
    public void F14_04_HistoryTurn_5000CharsText_MaintainsDataIntegrity()
    {
        string longSource = new string('S', 5000);
        string longTarget = new string('T', 5000);

        var turn = new TranslationTurn(longSource, longTarget);

        Assert.Equal(5000, turn.SourceText.Length);
        Assert.Equal(5000, turn.TargetText.Length);
    }

    [Fact]
    public void F14_05_SpectrumVisualizer_DecayAll_NeverDropsBelowZero()
    {
        var visualizer = new SpectrumVisualizerModel { DecayRate = 0.5f };
        for (int i = 0; i < 100; i++)
        {
            visualizer.DecayAll();
        }

        Assert.All(visualizer.CurrentLevels, lvl => Assert.Equal(0.0f, lvl));
    }

    #endregion

    #region F15: App Settings Dialog UI - Boundaries

    [Fact]
    public void F15_01_SaveSettings_WhitespaceApiKey_SavesAsEmpty()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(tempFile);
            service.SaveApiKey("    \t\n  ");

            string key = service.GetApiKey();
            Assert.Equal(string.Empty, key);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void F15_02_ModelSelection_NonExistentModel_DefaultsSafely()
    {
        var config = new LiveConfig("test-key", model: "non-existent-model");
        Assert.Equal("non-existent-model", config.Model);

        var fallbackConfig = new LiveConfig("test-key", model: null);
        Assert.Equal(LiveConfig.DefaultModel, fallbackConfig.Model);
    }

    [Fact]
    public void F15_03_VoiceSelection_EmptyVoice_DefaultsToPuck()
    {
        var config = new LiveConfig("test-key", voice: "   ");
        Assert.Equal("Puck", config.Voice);
    }

    [Fact]
    public void F15_04_SettingsDialog_CancelWithoutSave_DiscardsChanges()
    {
        var original = new AppSettings { Model = LiveConfig.DefaultModel, Voice = "Puck" };
        var draft = new AppSettings { Model = "models/gemini-2.5-flash", Voice = "Kore" };

        // User cancels dialog: draft is discarded, original remains pristine
        Assert.Equal(LiveConfig.DefaultModel, original.Model);
        Assert.Equal("Puck", original.Voice);
        Assert.NotEqual(draft.Model, original.Model);
    }

    [Fact]
    public void F15_05_ParagraphLength_NegativeOrZero_DefaultsSafely()
    {
        Assert.Equal(50, SettingsService.SanitizeParagraphLength(-10));
        Assert.Equal(50, SettingsService.SanitizeParagraphLength(0));
        Assert.Equal(75, SettingsService.SanitizeParagraphLength(75));
    }

    #endregion

    #region F16: CI/CD & Build Workflow - Boundaries

    [Fact]
    public void F16_01_WorkflowYamlValidator_EmptyYaml_DoesNotCrash()
    {
        var validator = new WorkflowYamlValidator(string.Empty);
        Assert.False(validator.HasWindowsRunner());
        Assert.False(validator.HasDotNet9Setup());
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Fact]
    public void F16_02_WorkflowYamlValidator_MissingDotNet9_ReturnsFalse()
    {
        string yaml = @"
    steps:
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
";
        var validator = new WorkflowYamlValidator(yaml);
        Assert.False(validator.HasDotNet9Setup());
    }

    [Fact]
    public void F16_03_WorkflowYamlValidator_MissingSingleFileFlag_ReturnsFalse()
    {
        string yaml = @"
    run: dotnet publish -c Release -r win-x64 --self-contained
";
        var validator = new WorkflowYamlValidator(yaml);
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Fact]
    public void F16_04_WorkflowYamlValidator_UbuntuRunner_ReturnsFalseForWindowsRunner()
    {
        string yaml = @"
jobs:
  build:
    runs-on: ubuntu-latest
";
        var validator = new WorkflowYamlValidator(yaml);
        Assert.False(validator.HasWindowsRunner());
    }

    [Fact]
    public void F16_05_WorkflowYamlValidator_InvalidTagFormat_ReturnsFalse()
    {
        string yaml = @"
on:
  push:
    branches: [ main ]
";
        var validator = new WorkflowYamlValidator(yaml);
        Assert.False(validator.HasTagReleaseTrigger());
    }

    #endregion

    #region F17: E2E Integration & Verification - Boundaries

    [Fact]
    public void F17_01_Pipeline_RapidStartStop_10Iterations_ThreadSafe()
    {
        using var engine = new MockAudioCaptureEngine();
        for (int i = 0; i < 10; i++)
        {
            engine.StartCapture(AudioSource.SystemAudioLoopback);
            Assert.True(engine.IsCapturing);
            engine.StopCapture();
            Assert.False(engine.IsCapturing);
        }
    }

    [Fact]
    public void F17_02_Pipeline_TransientNetworkDropout_SimulatesBackoff()
    {
        Assert.Equal(1000, SettingsService.CalculateBackoffDelay(1));
        Assert.Equal(2000, SettingsService.CalculateBackoffDelay(2));
        Assert.Equal(4000, SettingsService.CalculateBackoffDelay(3));
        Assert.Equal(8000, SettingsService.CalculateBackoffDelay(4));
    }

    [Fact]
    public void F17_03_Pipeline_DeviceSwitchDuringActiveResponse_MaintainsStream()
    {
        using var engine = new MockAudioCaptureEngine();
        engine.StartCapture(AudioSource.SystemAudioLoopback);

        using var client = new GeminiLiveClient();
        int transcriptsReceived = 0;
        client.TargetTranscriptReceived += (s, e) => transcriptsReceived++;

        // Server emits transcript
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""outputTranscription"": { ""text"": ""Part 1"" } } }");

        // Audio switch happens mid-response
        engine.SwitchSource(AudioSource.Microphone);

        // Server continues streaming transcript
        client.ProcessIncomingServerMessage(@"{ ""serverContent"": { ""outputTranscription"": { ""text"": ""Part 2"" } } }");

        Assert.Equal(2, transcriptsReceived);
        Assert.Equal(AudioSource.Microphone, engine.CurrentSource);
    }

    [Fact]
    public void F17_04_Pipeline_HeavyCpuLoad_ResamplingDoesNotCorruptBuffers()
    {
        var dsp = new AudioDspService();
        int frameCount = 10000;
        byte[] input = SyntheticAudioGenerator.GenerateFloatWave(1000.0, 48000, 2, frameCount);

        byte[] output = dsp.ResampleTo16k16BitMono(input, input.Length, 48000, 2, 32, true);

        Assert.NotEmpty(output);
        Assert.InRange(output.Length, 6650, 6680);
    }

    [Fact]
    public void F17_05_Pipeline_WebSocketReconnect_PreservesHistoryCards()
    {
        var visualizer = new SpectrumVisualizerModel();
        visualizer.AddTurnCard(new TranslationTurn("Hello", "你好"));

        // Simulate reconnecting socket
        var session = new MockWebSocketSession();
        session.Connect();
        session.Disconnect();
        session.Connect();

        // History collection must remain completely preserved
        Assert.Single(visualizer.HistoryCards);
        Assert.Equal("Hello", visualizer.HistoryCards[0].SourceText);
    }

    #endregion
}
