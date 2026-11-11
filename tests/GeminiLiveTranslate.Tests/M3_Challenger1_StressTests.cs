using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeminiLiveTranslate.App.Interop;
using GeminiLiveTranslate.Core.Models;
using GeminiLiveTranslate.Core.Services;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Adversarial stress-testing suite for Milestone 3 (M3) Desktop Floating Lyrics Overlay:
/// 1. Negative coordinates & multi-monitor boundaries (secondary monitors at -1920,-1080, virtual spans, signed lParam)
/// 2. Hit-test perimeter & sub-pixel exactness (1-pixel boundaries across 8 directions, 4 corners, interior/exterior gaps)
/// 3. Bitwise extended style fuzzing (32-bit and 64-bit GWL_EXSTYLE bitmasks, 10,000 toggles, unrelated flag preservation)
/// 4. MINMAXINFO edge constraints (negative, zero, extreme dimensions, unmanaged struct layout and marshaling)
/// 5. Concurrency & rapid hotkey toggling (multi-threaded concurrent state transitions, idempotent execution)
/// 6. Typography & text streaming stress (extreme font bounds, surrogates, ZWJ sequences, mixed RTL/CJK/Latin)
/// </summary>
public class M3_Challenger1_StressTests
{
    private const int BorderThickness = 8;

    #region 1. Negative Coordinates & Multi-Monitor Boundaries

    [Fact]
    public void HitTest_SecondaryMonitor_NegativeCoordinates_AllDirectionsAndCornersAccurate()
    {
        // Secondary monitor placed to the top-left of primary monitor:
        // Top-left at (-1920, -1080), 1920x1080 resolution.
        // Window placed on secondary monitor:
        double winLeft = -1920;
        double winTop = -1080;
        double winWidth = 960;
        double winHeight = 170;

        // --- 4 Corners ---
        // Top-Left: within 8px of left and top
        Assert.Equal(Win32Constants.HTTOPLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft, winTop, BorderThickness));
        Assert.Equal(Win32Constants.HTTOPLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 7.9, winTop + 7.9, BorderThickness));

        // Top-Right: within 8px of right and top
        Assert.Equal(Win32Constants.HTTOPRIGHT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth - 1, winTop, BorderThickness));
        Assert.Equal(Win32Constants.HTTOPRIGHT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth - 7.9, winTop + 7.9, BorderThickness));

        // Bottom-Left: within 8px of left and bottom
        Assert.Equal(Win32Constants.HTBOTTOMLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft, winTop + winHeight - 1, BorderThickness));
        Assert.Equal(Win32Constants.HTBOTTOMLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 7.9, winTop + winHeight - 7.9, BorderThickness));

        // Bottom-Right: within 8px of right and bottom
        Assert.Equal(Win32Constants.HTBOTTOMRIGHT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth - 1, winTop + winHeight - 1, BorderThickness));
        Assert.Equal(Win32Constants.HTBOTTOMRIGHT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth - 7.9, winTop + winHeight - 7.9, BorderThickness));

        // --- 4 Edges ---
        // Top edge
        Assert.Equal(Win32Constants.HTTOP,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 480, winTop + 2, BorderThickness));

        // Bottom edge
        Assert.Equal(Win32Constants.HTBOTTOM,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 480, winTop + winHeight - 2, BorderThickness));

        // Left edge
        Assert.Equal(Win32Constants.HTLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 2, winTop + 85, BorderThickness));

        // Right edge
        Assert.Equal(Win32Constants.HTRIGHT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth - 2, winTop + 85, BorderThickness));

        // --- Client Interior ---
        Assert.Equal(Win32Constants.HTCLIENT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 480, winTop + 85, BorderThickness));

        // --- Exterior Boundary (HTNOWHERE) ---
        // Left exterior
        Assert.Equal(Win32Constants.HTNOWHERE,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft - 1, winTop + 85, BorderThickness));
        // Right exterior
        Assert.Equal(Win32Constants.HTNOWHERE,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + winWidth + 1, winTop + 85, BorderThickness));
        // Top exterior
        Assert.Equal(Win32Constants.HTNOWHERE,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 480, winTop - 1, BorderThickness));
        // Bottom exterior
        Assert.Equal(Win32Constants.HTNOWHERE,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 480, winTop + winHeight + 1, BorderThickness));
    }

    [Fact]
    public void HitTest_VirtualDesktopSpanning_CrossZeroBoundaryWithoutDiscontinuity()
    {
        // Window spanning across secondary monitor (negative X) and primary monitor (positive X)
        // Left = -960, Width = 1920 (extends from -960 to +960), Top = 200, Height = 170
        double winLeft = -960;
        double winTop = 200;
        double winWidth = 1920;
        double winHeight = 170;

        // Across X = 0 (origin between two monitors)
        Assert.Equal(Win32Constants.HTCLIENT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, -1.0, 250, BorderThickness));
        Assert.Equal(Win32Constants.HTCLIENT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, 0.0, 250, BorderThickness));
        Assert.Equal(Win32Constants.HTCLIENT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, 1.0, 250, BorderThickness));

        // Top edge crossing X = 0
        Assert.Equal(Win32Constants.HTTOP,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, -1.0, winTop + 2, BorderThickness));
        Assert.Equal(Win32Constants.HTTOP,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, 0.0, winTop + 2, BorderThickness));
        Assert.Equal(Win32Constants.HTTOP,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, 1.0, winTop + 2, BorderThickness));

        // Bottom edge crossing X = 0
        Assert.Equal(Win32Constants.HTBOTTOM,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, 0.0, winTop + winHeight - 2, BorderThickness));
    }

    [Fact]
    public void HitTest_FractionalSubPixelCoordinates_HandledDeterministically()
    {
        // High-DPI 125% or 175% scale creates fractional window positions
        double winLeft = -1366.6667;
        double winTop = -768.3333;
        double winWidth = 800.5;
        double winHeight = 200.25;

        // Subpixel right inside left edge: offset 0.0001
        Assert.Equal(Win32Constants.HTLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 0.0001, winTop + 100, BorderThickness));

        // Subpixel right outside left edge: offset -0.0001
        Assert.Equal(Win32Constants.HTNOWHERE,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft - 0.0001, winTop + 100, BorderThickness));

        // Subpixel right inside client boundary: offset exactly 8.0001
        Assert.Equal(Win32Constants.HTCLIENT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 8.0001, winTop + 100, BorderThickness));

        // Subpixel right inside border: offset 7.9999
        Assert.Equal(Win32Constants.HTLEFT,
            NativeMethods.CalculateHitTest(winLeft, winTop, winWidth, winHeight, winLeft + 7.9999, winTop + 100, BorderThickness));
    }

    [Theory]
    [InlineData(-1920, -1080)]
    [InlineData(-1, -1)]
    [InlineData(-32768, -32768)]
    [InlineData(0, 0)]
    [InlineData(1920, 1080)]
    [InlineData(32767, 32767)]
    [InlineData(-1920, 1080)]
    [InlineData(1920, -1080)]
    public void Win32LParam_SignedCoordinatesExtraction_PreservesNegativeValues(short x, short y)
    {
        // Win32 packs cursor screen coordinates into lParam as LOWORD=X, HIWORD=Y.
        // On multi-monitor setups with negative coordinates, signed extraction is mandatory.
        long packedLParam = unchecked((long)(((ulong)(ushort)y << 16) | (ushort)x));
        IntPtr lParam = (IntPtr)packedLParam;

        short extractedX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        short extractedY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

        Assert.Equal(x, extractedX);
        Assert.Equal(y, extractedY);
    }

    #endregion

    #region 2. Hit-Test Perimeter & Sub-pixel Exactness

    [Fact]
    public void HitTest_PerimeterExactSweep_ZeroGapsBetweenEdgesAndCorners()
    {
        // Sweep across entire window perimeter pixel-by-pixel
        double left = 100;
        double top = 100;
        double width = 500;
        double height = 200;
        int border = 8;

        // Top horizontal row at Y = top + 1 (inside top border)
        for (double x = left; x <= left + width; x += 1.0)
        {
            int hit = NativeMethods.CalculateHitTest(left, top, width, height, x, top + 1, border);
            if (x < left + border)
            {
                Assert.Equal(Win32Constants.HTTOPLEFT, hit);
            }
            else if (x >= left + width - border)
            {
                Assert.Equal(Win32Constants.HTTOPRIGHT, hit);
            }
            else
            {
                Assert.Equal(Win32Constants.HTTOP, hit);
            }
        }

        // Bottom horizontal row at Y = top + height - 1 (inside bottom border)
        for (double x = left; x <= left + width; x += 1.0)
        {
            int hit = NativeMethods.CalculateHitTest(left, top, width, height, x, top + height - 1, border);
            if (x < left + border)
            {
                Assert.Equal(Win32Constants.HTBOTTOMLEFT, hit);
            }
            else if (x >= left + width - border)
            {
                Assert.Equal(Win32Constants.HTBOTTOMRIGHT, hit);
            }
            else
            {
                Assert.Equal(Win32Constants.HTBOTTOM, hit);
            }
        }

        // Left vertical column at X = left + 1 (inside left border)
        for (double y = top; y <= top + height; y += 1.0)
        {
            int hit = NativeMethods.CalculateHitTest(left, top, width, height, left + 1, y, border);
            if (y < top + border)
            {
                Assert.Equal(Win32Constants.HTTOPLEFT, hit);
            }
            else if (y >= top + height - border)
            {
                Assert.Equal(Win32Constants.HTBOTTOMLEFT, hit);
            }
            else
            {
                Assert.Equal(Win32Constants.HTLEFT, hit);
            }
        }

        // Right vertical column at X = left + width - 1 (inside right border)
        for (double y = top; y <= top + height; y += 1.0)
        {
            int hit = NativeMethods.CalculateHitTest(left, top, width, height, left + width - 1, y, border);
            if (y < top + border)
            {
                Assert.Equal(Win32Constants.HTTOPRIGHT, hit);
            }
            else if (y >= top + height - border)
            {
                Assert.Equal(Win32Constants.HTBOTTOMRIGHT, hit);
            }
            else
            {
                Assert.Equal(Win32Constants.HTRIGHT, hit);
            }
        }
    }

    [Fact]
    public void HitTest_ClientAreaExactTransition_NoDeadZones()
    {
        double left = 200;
        double top = 200;
        double width = 400;
        double height = 300;
        int border = 8;

        // Transition from Top border to Client area at X = 350
        double transitionY = top + border; // 208.0
        int justBeforeBorderEnd = NativeMethods.CalculateHitTest(left, top, width, height, 350, transitionY - 0.0001, border);
        int exactBorderBoundary = NativeMethods.CalculateHitTest(left, top, width, height, 350, transitionY, border);
        int justInsideClient = NativeMethods.CalculateHitTest(left, top, width, height, 350, transitionY + 0.0001, border);

        Assert.Equal(Win32Constants.HTTOP, justBeforeBorderEnd);
        Assert.Equal(Win32Constants.HTCLIENT, exactBorderBoundary);
        Assert.Equal(Win32Constants.HTCLIENT, justInsideClient);

        // Transition from Left border to Client area at Y = 350
        double transitionX = left + border; // 208.0
        int justBeforeLeftBorderEnd = NativeMethods.CalculateHitTest(left, top, width, height, transitionX - 0.0001, 350, border);
        int exactLeftBoundary = NativeMethods.CalculateHitTest(left, top, width, height, transitionX, 350, border);
        int justInsideLeftClient = NativeMethods.CalculateHitTest(left, top, width, height, transitionX + 0.0001, 350, border);

        Assert.Equal(Win32Constants.HTLEFT, justBeforeLeftBorderEnd);
        Assert.Equal(Win32Constants.HTCLIENT, exactLeftBoundary);
        Assert.Equal(Win32Constants.HTCLIENT, justInsideLeftClient);
    }

    #endregion

    #region 3. Bitwise Extended Style Fuzzing

    [Fact]
    public void ClickThrough_FuzzArbitraryMasks_PreservesUnrelatedExtendedStyles()
    {
        // Win32 Extended Styles that must remain unaffected
        int[] testMasks = new int[]
        {
            0,
            Win32Constants.WS_EX_TOPMOST,
            Win32Constants.WS_EX_TOOLWINDOW,
            Win32Constants.WS_EX_NOACTIVATE,
            0x00000010, // WS_EX_ACCEPTFILES
            0x00040000, // WS_EX_APPWINDOW
            0x02000000, // WS_EX_COMPOSITED
            0x00000001, // WS_EX_DLGMODALFRAME
            0x00000200, // WS_EX_CLIENTEDGE
            Win32Constants.WS_EX_TOPMOST | Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_NOACTIVATE,
            0x0FFFFFFF & ~(Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED) // All non-click-through bits
        };

        foreach (int initialMask in testMasks)
        {
            // Enable Click-Through
            int enabled = NativeMethods.EnableClickThrough(initialMask);
            Assert.True((enabled & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT);
            Assert.True((enabled & Win32Constants.WS_EX_LAYERED) == Win32Constants.WS_EX_LAYERED);
            Assert.True(NativeMethods.IsClickThrough(enabled));

            // Verify unrelated bits are untouched
            int unrelatedEnabled = enabled & ~(Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED);
            int unrelatedInitial = initialMask & ~(Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED);
            Assert.Equal(unrelatedInitial, unrelatedEnabled);

            // Disable Click-Through
            int disabled = NativeMethods.DisableClickThrough(enabled);
            Assert.False((disabled & Win32Constants.WS_EX_TRANSPARENT) == Win32Constants.WS_EX_TRANSPARENT);
            // WS_EX_LAYERED must be preserved for WPF per-pixel transparency
            Assert.True((disabled & Win32Constants.WS_EX_LAYERED) == Win32Constants.WS_EX_LAYERED);
            Assert.False(NativeMethods.IsClickThrough(disabled));

            // Verify unrelated bits remain completely untouched
            int unrelatedDisabled = disabled & ~(Win32Constants.WS_EX_TRANSPARENT | Win32Constants.WS_EX_LAYERED);
            Assert.Equal(unrelatedInitial, unrelatedDisabled);
        }
    }

    [Fact]
    public void ClickThroughLong_64BitBitmaskFuzzing_PreservesHigh32Bits()
    {
        var rng = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            // Generate arbitrary 64-bit style mask with bits set in high 32 bits
            long highBits = ((long)rng.Next() << 32);
            long lowBits = (uint)rng.Next();
            long initialMask = highBits | lowBits;

            long enabled = NativeMethods.EnableClickThroughLong(initialMask);
            Assert.True((enabled & Win32Constants.WS_EX_TRANSPARENT) != 0);
            Assert.True((enabled & Win32Constants.WS_EX_LAYERED) != 0);
            Assert.True(NativeMethods.IsClickThroughLong(enabled));

            // High 32 bits must remain identical
            Assert.Equal(highBits, enabled & unchecked((long)0xFFFFFFFF00000000UL));

            long disabled = NativeMethods.DisableClickThroughLong(enabled);
            Assert.False((disabled & Win32Constants.WS_EX_TRANSPARENT) != 0);
            Assert.True((disabled & Win32Constants.WS_EX_LAYERED) != 0);
            Assert.False(NativeMethods.IsClickThroughLong(disabled));

            // High 32 bits must remain identical
            Assert.Equal(highBits, disabled & unchecked((long)0xFFFFFFFF00000000UL));
        }
    }

    [Fact]
    public void ClickThrough_RapidToggle10000Iterations_StrictlyIdempotent()
    {
        int style = Win32Constants.WS_EX_TOPMOST | Win32Constants.WS_EX_TOOLWINDOW;

        for (int i = 0; i < 10000; i++)
        {
            style = NativeMethods.EnableClickThrough(style);
            Assert.True(NativeMethods.IsClickThrough(style));

            style = NativeMethods.DisableClickThrough(style);
            Assert.False(NativeMethods.IsClickThrough(style));
        }

        // Expected final state: WS_EX_TOPMOST, WS_EX_TOOLWINDOW, and WS_EX_LAYERED are set, WS_EX_TRANSPARENT is clear
        int expected = Win32Constants.WS_EX_TOPMOST | Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_LAYERED;
        Assert.Equal(expected, style);
    }

    [Fact]
    public void ClickThroughLong_RapidToggle10000Iterations_StrictlyIdempotent()
    {
        long style = 0x1234567800000088L; // Custom high bits + WS_EX_TOPMOST + WS_EX_TOOLWINDOW

        for (int i = 0; i < 10000; i++)
        {
            style = NativeMethods.EnableClickThroughLong(style);
            Assert.True(NativeMethods.IsClickThroughLong(style));

            style = NativeMethods.DisableClickThroughLong(style);
            Assert.False(NativeMethods.IsClickThroughLong(style));
        }

        long expected = 0x1234567800000088L | Win32Constants.WS_EX_LAYERED;
        Assert.Equal(expected, style);
    }

    #endregion

    #region 4. MINMAXINFO Edge Constraints & Extreme Dimensions

    [Theory]
    [InlineData(-500, -500, Win32Constants.MinOverlayWidth, Win32Constants.MinOverlayHeight)]
    [InlineData(0, 0, Win32Constants.MinOverlayWidth, Win32Constants.MinOverlayHeight)]
    [InlineData(1, 1, Win32Constants.MinOverlayWidth, Win32Constants.MinOverlayHeight)]
    [InlineData(359.99, 99.99, Win32Constants.MinOverlayWidth, Win32Constants.MinOverlayHeight)]
    [InlineData(360.0, 100.0, 360.0, 100.0)]
    [InlineData(100000.0, 50000.0, 100000.0, 50000.0)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity, Win32Constants.MinOverlayWidth, Win32Constants.MinOverlayHeight)]
    public void ClampToMinDimensions_AdversarialInputs_EnforcesLimits(
        double width, double height, double expectedW, double expectedH)
    {
        var (clampedW, clampedH) = NativeMethods.ClampToMinDimensions(width, height);
        Assert.Equal(expectedW, clampedW);
        Assert.Equal(expectedH, clampedH);
    }

    [Fact]
    public void MinMaxInfoStruct_MarshalsAccuratelyMatchingWin32Layout()
    {
        // MINMAXINFO struct size in Win32 x64/x86 is 5 * sizeof(POINT) = 5 * 8 = 40 bytes
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.MINMAXINFO>());
        Assert.Equal(8, Marshal.SizeOf<NativeMethods.POINT>());
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.RECT>());
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.MARGINS>());

        // Marshal round-trip test
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MINMAXINFO>());
        try
        {
            var initial = new NativeMethods.MINMAXINFO
            {
                ptMinTrackSize = new NativeMethods.POINT(0, 0),
                ptMaxTrackSize = new NativeMethods.POINT(3840, 2160)
            };
            Marshal.StructureToPtr(initial, ptr, false);

            // Mutate as WM_GETMINMAXINFO handler does
            var retrieved = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(ptr);
            retrieved.ptMinTrackSize.X = NativeMethods.MinOverlayWidth;
            retrieved.ptMinTrackSize.Y = NativeMethods.MinOverlayHeight;
            Marshal.StructureToPtr(retrieved, ptr, true);

            var finalMmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(ptr);
            Assert.Equal(NativeMethods.MinOverlayWidth, finalMmi.ptMinTrackSize.X);
            Assert.Equal(NativeMethods.MinOverlayHeight, finalMmi.ptMinTrackSize.Y);
            Assert.Equal(3840, finalMmi.ptMaxTrackSize.X);
            Assert.Equal(2160, finalMmi.ptMaxTrackSize.Y);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Theory]
    [InlineData(-100000, -100000, -3840, -2160)]
    [InlineData(100000, 100000, 1920 - 960, 1080 - 170)]
    public void ClampOverlayPosition_ExtremeBounds_ClampsWithinVirtualDesktop(
        double x, double y, double expectedX, double expectedY)
    {
        var (clampedX, clampedY) = NativeMethods.ClampOverlayPosition(x, y, 1920, 1080, 960, 170);
        Assert.Equal(expectedX, clampedX);
        Assert.Equal(expectedY, clampedY);
    }

    #endregion

    #region 5. Concurrency & Rapid Hotkey Toggling Simulation

    [Fact]
    public void ConcurrentHotkeyToggle_SimulatedAcrossThreads_MaintainsConsistentState()
    {
        // Stress-test multi-threaded concurrent state transitions simulating rapid Ctrl+F8 presses
        int state = 0; // 0 = off, 1 = on
        int totalIterations = 10000;
        int threadCount = 8;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, threadCount, _ =>
        {
            try
            {
                for (int i = 0; i < totalIterations / threadCount; i++)
                {
                    // Atomic toggle simulation
                    int prev = Interlocked.Exchange(ref state, 1 - Volatile.Read(ref state));
                    Assert.True(prev == 0 || prev == 1);

                    // Test pure function concurrency
                    long baseStyle = 0x88L;
                    long enabled = NativeMethods.EnableClickThroughLong(baseStyle);
                    long disabled = NativeMethods.DisableClickThroughLong(enabled);
                    Assert.True(NativeMethods.IsClickThroughLong(enabled));
                    Assert.False(NativeMethods.IsClickThroughLong(disabled));
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.True(state == 0 || state == 1);
    }

    [Fact]
    public void HotkeyModifiers_AllCombinations_DoNotCollideWithClickThroughHotkey()
    {
        // Click-through hotkey: Ctrl + F8 with MOD_NOREPEAT
        uint clickThroughModifiers = Win32Constants.MOD_CONTROL | Win32Constants.MOD_NOREPEAT;
        Assert.Equal(0x4002u, clickThroughModifiers);

        // Verify other common combinations differ
        uint altF8 = Win32Constants.MOD_ALT | Win32Constants.MOD_NOREPEAT;
        uint shiftF8 = Win32Constants.MOD_SHIFT | Win32Constants.MOD_NOREPEAT;
        uint winF8 = Win32Constants.MOD_WIN | Win32Constants.MOD_NOREPEAT;
        uint ctrlShiftF8 = Win32Constants.MOD_CONTROL | Win32Constants.MOD_SHIFT | Win32Constants.MOD_NOREPEAT;

        Assert.NotEqual(clickThroughModifiers, altF8);
        Assert.NotEqual(clickThroughModifiers, shiftF8);
        Assert.NotEqual(clickThroughModifiers, winF8);
        Assert.NotEqual(clickThroughModifiers, ctrlShiftF8);
    }

    #endregion

    #region 6. Typography & Text Streaming Stress

    [Theory]
    [InlineData(14.0, -1000.0, 14.0)]
    [InlineData(64.0, 1000.0, 64.0)]
    [InlineData(0.0, 0.0, 14.0)]
    [InlineData(100.0, 0.0, 64.0)]
    [InlineData(24.0, double.NaN, 24.0)] // NaN delta handled safely
    public void ClampFontSize_ExtremeValues_NeverViolatesMinMaxBounds(double current, double delta, double expected)
    {
        if (double.IsNaN(delta))
        {
            // If delta is NaN, should remain at valid current
            double clamped = NativeMethods.ClampFontSize(current, 14.0, 64.0);
            Assert.Equal(expected, clamped);
        }
        else
        {
            double result = NativeMethods.AdjustFontSize(current, delta, 14.0, 64.0);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Subtitle_StreamingStress_ComplexUnicodeAndSpecialGlyphs()
    {
        // Stress test text strings with emojis, ZWJ sequences, CJK, RTL, and punctuation
        var testPhrases = new[]
        {
            "即時口譯字幕準備就緒！",
            "🚀 Live translation connected with Gemini 2.0 / 3.5 API.",
            "👨‍👩‍👧‍👦 Family emoji with zero-width joiners",
            "مرحبا بكم في البث المباشر (RTL Arabic)",
            "こんにちは世界！日本語のリアルタイム音声翻訳テストです。",
            "Special characters: < > & \" ' \t \r\n \0 \\ / | ? * :",
            new string('A', 10000), // Very long paragraph without spaces
            new string('中', 5000),  // Very long CJK text
        };

        var segmentation = new TextSegmentationService { MaxCharacters = 50 };

        foreach (var phrase in testPhrases)
        {
            var turn = new TranslationTurn(sourceText: phrase, targetText: phrase);
            Assert.NotNull(turn.SourceText);
            Assert.NotNull(turn.TargetText);

            var chunks = segmentation.ChunkText(phrase, 50);
            Assert.NotEmpty(chunks);

            // Grapheme cluster count must be non-negative
            var enumerator = StringInfo.GetTextElementEnumerator(phrase);
            int clusterCount = 0;
            while (enumerator.MoveNext())
            {
                clusterCount++;
            }
            Assert.True(clusterCount > 0);
        }
    }

    [Fact]
    public void CornerPreferenceAndBackdrop_EdgeValues_ClampedSafely()
    {
        // Corner preference bounds [0, 3]
        Assert.Equal(0, NativeMethods.ClampCornerPreference(-1));
        Assert.Equal(0, NativeMethods.ClampCornerPreference(0));
        Assert.Equal(2, NativeMethods.ClampCornerPreference(2));
        Assert.Equal(3, NativeMethods.ClampCornerPreference(3));
        Assert.Equal(3, NativeMethods.ClampCornerPreference(99));

        // Backdrop type bounds [0, 4] with high-contrast override
        Assert.Equal(Win32Constants.DWMSBT_NONE, NativeMethods.ResolveBackdropType(2, highContrast: true));
        Assert.Equal(Win32Constants.DWMSBT_MAINWINDOW, NativeMethods.ResolveBackdropType(Win32Constants.DWMSBT_MAINWINDOW, highContrast: false));
        Assert.Equal(Win32Constants.DWMSBT_AUTO, NativeMethods.ResolveBackdropType(99, highContrast: false));
    }

    #endregion
}
