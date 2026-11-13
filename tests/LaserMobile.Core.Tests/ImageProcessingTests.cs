using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Imaging.Skia;
using SkiaSharp;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class ImageProcessingTests
{
    private static readonly JobLimits Limits = new(40, 40, 1200, 1000);
    private static readonly RasterSettings Settings = new(30, .1, 600, 100);
    private static byte[] Picture(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height); using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 90); return encoded.ToArray();
    }
    [Theory]
    [InlineData(4000, 2000, 1000, 1000, 1000, 500)]
    [InlineData(2000, 4000, 1000, 1000, 500, 1000)]
    [InlineData(400, 200, 800, 800, 400, 200)]
    [InlineData(4096, 4096, 4096, 4096, 1000, 1000)]
    public void PixelFitPreservesAspectAndHonorsBothCaps(int width, int height, int capWidth, int capHeight, int expectedWidth, int expectedHeight)
    {
        var fitted = ImageSizing.FitPixels(width, height, capWidth, capHeight);
        Assert.Equal((expectedWidth, expectedHeight), fitted); Assert.True((long)fitted.Width * fitted.Height <= ImageSizing.SampleBudget);
    }
    [Fact] public void PhysicalFitHonorsImageAndMachineLimitsWithOneScale()
    {
        var fit = ImageSizing.FitPhysical(100, 200, 30, new(MaxWidthMillimeters: 30, MaxHeightMillimeters: 20), Limits);
        Assert.Equal(10, fit.Width, 5); Assert.Equal(20, fit.Height, 5);
        var machine = ImageSizing.FitPhysical(200, 100, 100, new(MaxWidthMillimeters: 1000, MaxHeightMillimeters: 1000), Limits);
        Assert.Equal(40, machine.Width); Assert.Equal(20, machine.Height);
    }
    [Fact] public void RasterAutomaticallyFitsPixelAndPhysicalLimits()
    {
        var result = ArtworkImporter.ConvertDetailed(Picture(400, 200), "wide.png", Settings with { WidthMillimeters = 80, LineSpacing = .05 }, Limits, false, default, options: new(120, 80, 20, 20));
        Assert.Equal(120, result.Image.WidthPixels); Assert.Equal(60, result.Image.HeightPixels);
        Assert.Equal(20, result.Image.WidthMillimeters, 5); Assert.Equal(10, result.Image.HeightMillimeters, 5);
        Assert.Equal(20, result.Analysis.MaxX, 3); Assert.Equal(10, result.Analysis.MaxY, 3); Assert.True(result.Analysis.EmitsLaser);
    }
    [Fact] public void CropAndRotationDetermineAspectBeforeUniformFit()
    {
        var result = ArtworkImporter.ConvertDetailed(Picture(500, 200), "cropped.png", Settings, Limits, false, default, new(0, 0, .5, 1, 1), new(100, 80, 40, 20));
        Assert.Equal(16, result.Image.WidthMillimeters, 5); Assert.Equal(20, result.Image.HeightMillimeters, 5);
        Assert.Equal(64, result.Image.WidthPixels); Assert.Equal(80, result.Image.HeightPixels);
    }
    [Fact] public void LargePhoneJpegIsAcceptedAndDownsampledInsteadOfRejectedAt12Megapixels()
    {
        var jpeg = Picture(2000, 6500, SKEncodedImageFormat.Jpeg);
        Assert.Equal(ImportKind.Bitmap, ImportValidator.Validate(jpeg, "photo.jpg", "image/jpeg", ImportKind.Bitmap).Kind);
        var result = ArtworkImporter.ConvertDetailed(jpeg, "photo.jpg", Settings, Limits, false, default, options: new(128, 128, 20, 20));
        Assert.True(result.Image.WidthPixels <= 128 && result.Image.HeightPixels <= 128);
        Assert.Equal(20, result.Image.HeightMillimeters, 4);
        Assert.Equal(2000.0 / 6500, result.Image.WidthMillimeters / result.Image.HeightMillimeters, 5);
    }
    [Fact] public void SvgRasterAndVectorRespectConfiguredPhysicalCaps()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg width='100' height='200' xmlns='http://www.w3.org/2000/svg'><rect width='100' height='200'/></svg>");
        foreach (bool outline in new[] { false, true })
        {
            var result = ArtworkImporter.ConvertDetailed(svg, "a.svg", Settings, Limits, outline, default, options: new(100, 100, 20, 20));
            Assert.Equal(10, result.Analysis.MaxX, 2); Assert.Equal(20, result.Analysis.MaxY, 2);
        }
    }
    [Fact] public void InvalidLimitsAndCancellationAreExplicit()
    {
        Assert.Throws<ArgumentException>(() => new ImageProcessingOptions(0, 100).Validate());
        Assert.Throws<ArgumentException>(() => new ImageProcessingOptions(MaxWidthMillimeters: double.NaN).Validate());
        Assert.Throws<ArgumentException>(() => new ContourSettings(0).Validate());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ArtworkImporter.Convert(Picture(10, 10), "a.png", Settings, Limits, false, cancelled.Token));
    }
    private static byte[] LowContrast(int width = 80, int height = 80)
    {
        var pixels = Enumerable.Repeat((byte)235, width * height).ToArray();
        for (int y = 15; y < height - 15; y++) for (int x = 15; x < width - 15; x++) pixels[y * width + x] = 190;
        return pixels;
    }
    [Fact] public void ExperimentalContoursDetectStructureThatDirectThresholdWouldDiscard()
    {
        var pixels = LowContrast(); var settings = Settings with { Mode = RasterMode.Threshold, WidthMillimeters = 20 };
        var raster = RasterCam.Generate(pixels, 80, 80, settings, Limits);
        Assert.False(raster.EmitsLaser);
        var contours = ExperimentalContours.Generate(pixels, 80, 80, 20, 20, settings, new(), Limits);
        Assert.True(contours.EmitsLaser); Assert.Contains(contours.Segments, s => s.Power > 0 && Math.Abs(s.Y2 - s.Y1) > 1);
        Assert.Equal("M5", contours.Plan.Lines[^1]);
        for (int i = 1; i < contours.Plan.Lines.Count; i++) if (contours.Plan.Lines[i].StartsWith("G0", StringComparison.Ordinal)) Assert.Equal("M5", contours.Plan.Lines[i - 1]);
        Assert.All(contours.Segments, s => { Assert.InRange(s.X2, 0, 20); Assert.InRange(s.Y2, 0, 20); if (s.Rapid) Assert.Equal(0, s.Power); });
    }
    [Fact] public void SensitivityIsMonotonicAndUniformImageHasNoContours()
    {
        var pixels = LowContrast();
        var low = ExperimentalContours.DetectEdges(pixels, 80, 80, 1, false, 2);
        var high = ExperimentalContours.DetectEdges(pixels, 80, 80, 1, false, 9);
        Assert.True(high.Count(x => x) >= low.Count(x => x)); Assert.Contains(true, high);
        byte[] flat = Enumerable.Repeat((byte)255, 80 * 80).ToArray();
        Assert.DoesNotContain(true, ExperimentalContours.DetectEdges(flat, 80, 80, 1, false, 5));
        Assert.Throws<FormatException>(() => ExperimentalContours.Generate(flat, 80, 80, 20, 20, Settings, new(), Limits));
    }
    [Fact] public void ContourModeUsesSameAspectFitAndCanSwitchBackToRaster()
    {
        using var bitmap = new SKBitmap(160, 80); using (var canvas = new SKCanvas(bitmap))
        { canvas.Clear(new SKColor(235, 235, 235)); using var paint = new SKPaint { Color = new SKColor(190, 190, 190) }; canvas.DrawRect(20, 15, 120, 50, paint); }
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); var bytes = encoded.ToArray();
        var contour = ArtworkImporter.ConvertDetailed(bytes, "drawing.png", Settings, Limits, false, default, options: new(100, 80, 20, 20), contours: new());
        Assert.Equal(20, contour.Image.WidthMillimeters); Assert.Equal(10, contour.Image.HeightMillimeters); Assert.Equal(100, contour.Image.WidthPixels); Assert.Equal(50, contour.Image.HeightPixels);
        Assert.True(contour.Analysis.EmitsLaser);
        var raster = ArtworkImporter.ConvertDetailed(bytes, "drawing.png", Settings with { Mode = RasterMode.Threshold }, Limits, false, default, options: new(100, 80, 20, 20));
        Assert.False(raster.Analysis.EmitsLaser); Assert.NotEqual(raster.Analysis.Plan.Sha256, contour.Analysis.Plan.Sha256);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ExperimentalContours.Generate(LowContrast(), 80, 80, 20, 20, Settings, new(), Limits, cancelled.Token));
    }
}
