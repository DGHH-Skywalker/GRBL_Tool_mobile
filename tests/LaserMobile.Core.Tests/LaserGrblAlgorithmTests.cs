using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Imaging.Skia;
using SkiaSharp;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class LaserGrblAlgorithmTests
{
    private static readonly JobLimits Limits = new(40, 40, 1200, 1000);
    private static readonly RasterSettings Settings = new(20, .1, 600, 100);
    [Fact] public void DesktopOpticalWeightsAlphaAndPowerScalingArePreserved()
    {
        Assert.Equal(76, LaserGrblImageMath.OpticalGray(255, 0, 0));
        Assert.Equal(150, LaserGrblImageMath.OpticalGray(0, 255, 0));
        Assert.Equal(29, LaserGrblImageMath.OpticalGray(0, 0, 255));
        Assert.Equal(255, LaserGrblImageMath.OpticalGray(0, 0, 0, 0));
        Assert.Equal(49, LaserGrblImageMath.Power(128, 100, RasterMode.Grayscale, 128));
        Assert.Equal(0, LaserGrblImageMath.Power(255, 100, RasterMode.Grayscale, 128));
        Assert.Equal(100, LaserGrblImageMath.Power(0, 100, RasterMode.Grayscale, 128));
    }
    [Fact] public void ConfigurableThresholdChangesTheActualProgram()
    {
        byte[] pixels = [150, 150, 150, 150];
        Assert.False(RasterCam.Generate(pixels, 2, 2, Settings with { Mode = RasterMode.Threshold, Threshold = 128 }, Limits).EmitsLaser);
        Assert.True(RasterCam.Generate(pixels, 2, 2, Settings with { Mode = RasterMode.Threshold, Threshold = 180 }, Limits).EmitsLaser);
    }
    [Fact] public void FloydUsesTheDesktopDiffusionKernelIncludingTheFirstRow()
    {
        byte[] pixels = [120, 120, 120, 120]; Diffusion.Apply(pixels, 4, 1);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels);
    }
    [Theory]
    [InlineData(RasterMode.FloydSteinberg)][InlineData(RasterMode.Atkinson)][InlineData(RasterMode.Burkes)]
    [InlineData(RasterMode.Jarvis)][InlineData(RasterMode.Stucki)][InlineData(RasterMode.Sierra2)]
    [InlineData(RasterMode.Sierra3)][InlineData(RasterMode.SierraLite)]
    public void EveryDesktopKernelProducesBinaryTonesAndHonorsCancellation(RasterMode mode)
    {
        byte[] pixels = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); Diffusion.Apply(pixels, 16, 16, mode);
        Assert.All(pixels, value => Assert.True(value is 0 or 255)); Assert.Contains((byte)0, pixels); Assert.Contains((byte)255, pixels);
        var program = RasterCam.Generate(Enumerable.Repeat((byte)120, 256).ToArray(), 16, 16, Settings with { Mode = mode }, Limits);
        Assert.True(program.EmitsLaser); Assert.All(program.Segments.Where(s => s.Power > 0), s => Assert.Equal(100, s.Power));
        using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Throws<OperationCanceledException>(() => Diffusion.Apply(new byte[256], 16, 16, mode, c.Token));
    }
    [Fact] public void WhiteRunsAreSkippedAndEverySkipIsOff()
    {
        var pixels = Enumerable.Repeat((byte)255, 300).ToArray();
        for (int y = 0; y < 3; y++) { pixels[y * 100 + 20] = 0; pixels[y * 100 + 80] = 0; }
        var program = RasterCam.Generate(pixels, 100, 3, Settings, Limits);
        Assert.Equal(6, program.Segments.Count(s => s.Power > 0));
        Assert.All(program.Segments.Where(s => s.Power == 0), s => Assert.True(s.Rapid));
        for (int i = 1; i < program.Plan.Lines.Count; i++) if (program.Plan.Lines[i].StartsWith("G0", StringComparison.Ordinal)) Assert.Equal("M5", program.Plan.Lines[i - 1]);
    }
    [Fact] public void DesktopPotracePreservesAClosedOuterPathAndItsHole()
    {
        var pixels = Enumerable.Repeat((byte)255, 100 * 100).ToArray();
        for (int y = 10; y < 90; y++) for (int x = 10; x < 90; x++) if (x < 25 || x >= 75 || y < 25 || y >= 75) pixels[y * 100 + x] = 0;
        var result = LaserGrblContours.Generate(pixels, 100, 100, 20, 20, Settings, new(MinimumLengthPixels: 2), Limits);
        Assert.Equal(2, AssertClosedIslands(result));
        Assert.True(result.EmitsLaser); Assert.All(result.Segments.Where(s => s.Rapid), s => Assert.Equal(0, s.Power));
        Assert.DoesNotContain(result.Segments, s => s.Power > 0 && s.X1 > 7 && s.X1 < 13 && s.X2 > 7 && s.X2 < 13 && s.Y1 > 7 && s.Y1 < 13 && s.Y2 > 7 && s.Y2 < 13);
    }
    [Fact] public void QrFinderModulesRetainSeparateClosedBoundariesAndLoseSpeckles()
    {
        const int width = 116; var pixels = Enumerable.Repeat((byte)255, width * width).ToArray();
        foreach (var (ox, oy) in new[] { (4, 4), (18, 4), (4, 18) })
        for (int my = 0; my < 7; my++) for (int mx = 0; mx < 7; mx++)
        {
            bool black = mx is 0 or 6 || my is 0 or 6 || mx is >= 2 and <= 4 && my is >= 2 and <= 4;
            if (black) for (int dy = 0; dy < 4; dy++) for (int dx = 0; dx < 4; dx++) pixels[((oy + my) * 4 + dy) * width + (ox + mx) * 4 + dx] = 0;
        }
        pixels[0] = 0;
        var result = LaserGrblContours.Generate(pixels, width, width, 20, 20, Settings, new(), Limits);
        Assert.Equal(9, AssertClosedIslands(result));
        Assert.All(result.Segments.Where(s => s.Power > 0), s => Assert.True(Math.Abs(s.X2 - s.X1) < 5 && Math.Abs(s.Y2 - s.Y1) < 5));
    }
    [Fact] public void AutomaticThresholdRetainsLowContrastContoursAndManualThresholdIsAvailable()
    {
        var pixels = Enumerable.Repeat((byte)235, 60 * 60).ToArray();
        for (int y = 10; y < 50; y++) for (int x = 10; x < 50; x++) pixels[y * 60 + x] = 190;
        Assert.InRange(LaserGrblContours.AutomaticThreshold(pixels), (byte)191, (byte)235);
        Assert.True(LaserGrblContours.Generate(pixels, 60, 60, 20, 20, Settings, new(), Limits).EmitsLaser);
        Assert.Throws<FormatException>(() => LaserGrblContours.Generate(pixels, 60, 60, 20, 20, Settings, new(AdaptiveThreshold: false), Limits));
        using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Throws<OperationCanceledException>(() => LaserGrblContours.Generate(pixels, 60, 60, 20, 20, Settings, new(), Limits, c.Token));
    }
    [Fact] public async Task ConcurrentPotraceJobsKeepIndependentState()
    {
        var pixels = Enumerable.Repeat((byte)255, 64 * 64).ToArray();
        for (int y = 8; y < 50; y++) for (int x = 12; x < 55; x++) pixels[y * 64 + x] = 0;
        var jobs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => LaserGrblContours.Generate(pixels, 64, 64, 20, 20, Settings, new(), Limits))));
        Assert.All(jobs, result => Assert.Equal(jobs[0].Plan.Sha256, result.Plan.Sha256));
    }
    [Fact] public void PreviewHidesTravelByDefaultAndCanDisplayItAsDashedLines()
    {
        var program = GcodeProgram.Prepare("G21 G90\nM5\nG0 X2 Y5\nM4 S100\nG1 X2 Y10 F300\nM5\nG0 X18 Y10\nM4 S100\nG1 X18 Y5 F300\nM5", Limits);
        using var preview = new ToolpathPreview(program);
        using var bitmap = new SKBitmap(220, 220); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White); canvas.Scale(10); preview.Draw(canvas, new(0, 0, 22, 22), 10);
        for (int x = 70; x < 130; x++) Assert.Equal(SKColors.White, bitmap.GetPixel(x, 100));
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(20, 80));
        canvas.Clear(SKColors.White); preview.Draw(canvas, new(0, 0, 22, 22), 10, true);
        Assert.Contains(Enumerable.Range(70, 60), x => bitmap.GetPixel(x, 100) != SKColors.White);
    }
    private static int AssertClosedIslands(ProgramAnalysis program)
    {
        ToolSegment? start = null, last = null; int count = 0;
        foreach (var segment in program.Segments)
        {
            if (segment.Rapid)
            {
                if (start is not null) { Assert.NotNull(last); Assert.Equal(start.X2, last.X2, 3); Assert.Equal(start.Y2, last.Y2, 3); }
                start = segment; last = null; count++;
            }
            else if (segment.Power > 0) last = segment;
        }
        Assert.NotNull(start); Assert.NotNull(last); Assert.Equal(start.X2, last.X2, 3); Assert.Equal(start.Y2, last.Y2, 3); return count;
    }
}
