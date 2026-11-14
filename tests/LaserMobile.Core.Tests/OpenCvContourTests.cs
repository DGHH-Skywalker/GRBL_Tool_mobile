using System.Text.Json;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Imaging.Skia;
using SkiaSharp;
using Xunit;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
namespace LaserMobile.Core.Tests;

public sealed class OpenCvContourTests
{
    private static readonly JobLimits Limits = new(40, 40, 1200, 1000);
    private static readonly RasterSettings Settings = new(30, .1, 600, 100);
    private static string Evidence => Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory(), "artifacts", "tests", "contours");
    private static void Save(SKBitmap bitmap, string name)
    { Directory.CreateDirectory(Evidence); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(Path.Combine(Evidence, name), data.ToArray()); }
    private static byte[] Gray(SKBitmap bitmap)
    {
        var gray = new byte[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) { var p = bitmap.GetPixel(x, bitmap.Height - 1 - y); gray[y * bitmap.Width + x] = LaserGrblImageMath.OpticalGray(p.Red, p.Green, p.Blue); }
        return gray;
    }
    private static SKBitmap Render(ProgramAnalysis program, int width, int height, double physicalWidth, double physicalHeight, bool fill)
    {
        var bitmap = new SKBitmap(width, height); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
        canvas.Translate(0, height); canvas.Scale((float)(width / physicalWidth), (float)(-height / physicalHeight));
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd }; bool moving = true;
        foreach (var s in program.Segments)
        {
            if (s.Rapid || s.Power <= 0) { moving = true; continue; }
            if (moving) { path.MoveTo((float)s.X1, (float)s.Y1); moving = false; }
            path.LineTo((float)s.X2, (float)s.Y2);
        }
        using var paint = new SKPaint { Color = SKColors.Black, Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = (float)(physicalWidth / width), IsAntialias = false };
        canvas.DrawPath(path, paint); return bitmap;
    }
    [Theory]
    [InlineData(2)][InlineData(3)][InlineData(5)]
    public void DenseQrContoursRemainPixelFaithfulAndDecodeToTheSamePayload(int moduleSize)
    {
        string payload = "https://example.invalid/grbl/quality?data=" + string.Concat(Enumerable.Repeat("0123456789AbCdEfGhIjKlMnOpQrStUvWxYz-", 12));
        var bits = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 1, 1, new Dictionary<EncodeHintType, object> { [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.H, [EncodeHintType.MARGIN] = 4 });
        int width = bits.Width * moduleSize;
        using var source = new SKBitmap(width, width);
        for (int y = 0; y < width; y++) for (int x = 0; x < width; x++) source.SetPixel(x, y, bits[x / moduleSize, y / moduleSize] ? SKColors.Black : SKColors.White);
        var gray = Gray(source); var options = new ContourSettings(MinimumLengthPixels: 2, Profile: ContourProfile.Graphic);
        Assert.Equal(ContourProfile.Graphic, OpenCvContours.SelectProfile(gray, ContourProfile.Automatic));
        var program = OpenCvContours.Generate(gray, width, width, 30, 30, Settings, options, Limits);
        Assert.All(program.Segments.Where(s => s.Power > 0), s => Assert.True(s.X1 == s.X2 || s.Y1 == s.Y2, "QR boundary gained a diagonal"));
        using var restored = Render(program, width, width, 30, 30, true);
        var result = Gray(restored); int intersection = 0, union = 0;
        var rgb = new byte[width * width * 3];
        for (int i = 0; i < gray.Length; i++) { bool a = gray[i] < 128, b = result[i] < 128; if (a && b) intersection++; if (a || b) union++; }
        double iou = (double)intersection / union; Assert.True(iou >= .995, $"QR IoU={iou}");
        for (int y = 0; y < width; y++) for (int x = 0; x < width; x++) { byte value = restored.GetPixel(x, y).Red; int i = (y * width + x) * 3; rgb[i] = rgb[i + 1] = rgb[i + 2] = value; }
        var decoded = new BarcodeReaderGeneric().Decode(new RGBLuminanceSource(rgb, width, width, RGBLuminanceSource.BitmapFormat.RGB24));
        Assert.NotNull(decoded); Assert.Equal(payload, decoded.Text);
        Save(source, $"qr-source-{moduleSize}.png"); Save(restored, $"qr-restored-{moduleSize}.png");
        if (moduleSize == 3) { Save(source, "source-qr.png"); using var old = Render(LaserGrblContours.Generate(gray, width, width, 30, 30, Settings, options, Limits), width, width, 30, 30, true); Save(old, "qr-before-potrace.png"); }
    }
    [Fact] public void PhotoKeepsNativeEdgeGeometryAndExportsTheFineDetails()
    {
        using var source = SKBitmap.Decode(Path.Combine(Evidence, "source-photo.png")); Assert.NotNull(source);
        var gray = Gray(source); Assert.Equal(ContourProfile.Photo, OpenCvContours.SelectProfile(gray, ContourProfile.Automatic));
        var options = new ContourSettings(MinimumLengthPixels: 3, Profile: ContourProfile.Photo);
        var edges = OpenCvContours.Extract(gray, source.Width, source.Height, Settings, options);
        var program = OpenCvContours.Generate(gray, source.Width, source.Height, 30, 30, Settings, options, Limits);
        using var rendered = Render(program, source.Width, source.Height, 30, 30, false); var actual = Gray(rendered);
        int total = 0, covered = 0;
        for (int y = 1; y < source.Height - 1; y++) for (int x = 1; x < source.Width - 1; x++)
        {
            if (edges.Pixels[y * source.Width + x] == 0) continue; total++; bool found = false;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) found |= actual[(y + dy) * source.Width + x + dx] < 128;
            if (found) covered++;
        }
        double recall = (double)covered / total; Assert.True(total > 3000); Assert.True(recall > .97, $"Photo edge coverage={recall}");
        using var before = Render(LaserGrblContours.Generate(gray, source.Width, source.Height, 30, 30, Settings, options, Limits), source.Width, source.Height, 30, 30, false);
        Save(before, "photo-before-potrace.png"); Save(rendered, "photo-after-opencv.png");
        using var mask = new SKBitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++) mask.SetPixel(x, y, edges.Pixels[(source.Height - 1 - y) * source.Width + x] != 0 ? SKColors.Black : SKColors.White);
        Save(mask, "photo-reference-edges.png");
        File.WriteAllText(Path.Combine(Evidence, "photo-metrics.json"), JsonSerializer.Serialize(new { total, covered, recall, lines = program.Plan.Lines.Count, engine = "OpenCV 4.13 / native Canny / CLAHE" }));
    }
    [Fact] public void LocalThresholdRetainsThinInkAcrossUnevenLighting()
    {
        const int width = 256, height = 128; var gray = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) gray[y * width + x] = (byte)(100 + x / 2);
        var expected = new List<int>();
        for (int x = 15; x < 240; x += 11) for (int y = 20; y < 108; y++) { int p = y * width + x; gray[p] -= 24; expected.Add(p); }
        var mask = OpenCvContours.Extract(gray, width, height, Settings, new(Profile: ContourProfile.Document));
        Assert.True(expected.Count(p => mask.Pixels[p] != 0) >= expected.Count * .95);
        Assert.True(mask.Pixels.Count(p => p != 0) < expected.Count * 2);
    }
    [Fact] public void ExactRegionUnionKeepsHolesAndSmallRightAngleSteps()
    {
        const int width = 20; var mask = new byte[width * width];
        for (int y = 1; y < 19; y++) for (int x = 1; x < 19; x++) if (x < 3 || x >= 17 || y < 3 || y >= 17 || x == y) mask[y * width + x] = 255;
        var paths = OpenCvContours.PixelRegions(mask, width, width); Assert.True(paths.Count >= 2);
        foreach (var path in paths) for (int i = 0; i < path.Count; i++) { var a = path[i]; var b = path[(i + 1) % path.Count]; Assert.True(a.X == b.X || a.Y == b.Y); }
        using var c = new CancellationTokenSource(); c.Cancel(); Assert.Throws<OperationCanceledException>(() => OpenCvContours.PixelRegions(mask, width, width, c.Token));
    }
}
