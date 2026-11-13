using System.Text;
using LaserMobile.Domain;
using LaserMobile.Imaging.Skia;
using LaserMobile.Application;
using SkiaSharp;
using Xunit;
namespace LaserMobile.Core.Tests;
public sealed class ImportTests
{
    [Theory]
    [InlineData("a.pdf")][InlineData("a.exe")][InlineData("a.zip")][InlineData("a.gif")][InlineData("a.txt")][InlineData("a.png.exe")][InlineData("no-extension")]
    public void UnsupportedExtensionsNeverBecomeGcode(string name) => Assert.Throws<FormatException>(() => ImportFormats.ForName(name));
    [Theory]
    [InlineData("../a.png")][InlineData("x\\a.png")][InlineData("a.png ")][InlineData("a\n.png")]
    public void InvalidDocumentNamesRejected(string name) => Assert.Throws<FormatException>(() => ImportFormats.ForName(name));
    [Fact] public void PickerCategoriesHaveExplicitMimeAllowlist()
    {
        foreach (var kind in Enum.GetValues<ImportKind>())
        {
            var spec = ImportFormats.Get(kind); Assert.DoesNotContain("*/*", spec.MimeTypes); Assert.DoesNotContain("image/*", spec.MimeTypes);
        }
        Assert.Equal(ImportKind.Bitmap, ImportFormats.ForName("PHOTO.JPEG").Kind);
        Assert.Equal(ImportKind.Gcode, ImportFormats.ForName("laser.NGC").Kind);
        Assert.Throws<FormatException>(() => ImportFormats.CheckMetadata("a.png", "application/pdf", ImportKind.Bitmap));
        Assert.Throws<FormatException>(() => ImportFormats.CheckMetadata("a.json", "application/json", ImportKind.Gcode));
    }
    [Fact] public void EncodedImageMustMatchNameAndDecodeCompletely()
    {
        using var bitmap = new SKBitmap(8, 8); using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        Assert.Equal(ImportKind.Bitmap, ImportValidator.Validate(png, "image.png", "image/png", ImportKind.Bitmap).Kind);
        Assert.Throws<FormatException>(() => ImportValidator.Validate(png, "image.jpg", "image/jpeg", ImportKind.Bitmap));
        Assert.Throws<FormatException>(() => ImportValidator.Validate(png[..24], "image.png", "image/png", ImportKind.Bitmap));
        Assert.Throws<FormatException>(() => ImportValidator.Validate("%PDF-1.7"u8.ToArray(), "fake.png", "image/png", ImportKind.Bitmap));
    }
    [Fact] public void GcodeContentAndBudgetAreValidatedBeforeAcceptance()
    {
        var bytes = Encoding.ASCII.GetBytes(SimulationSession.Sample);
        Assert.Equal(bytes, ImportValidator.Validate(bytes, "test.nc", "application/octet-stream", ImportKind.Gcode).Bytes);
        var bom = new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray();
        Assert.Equal(bytes, ImportValidator.Validate(bom, "test.gcode", "text/plain", ImportKind.Gcode).Bytes);
        Assert.Throws<FormatException>(() => ImportValidator.Validate("%PDF-1.7"u8.ToArray(), "fake.nc", null, ImportKind.Gcode));
        Assert.Throws<FormatException>(() => ImportValidator.Validate([0xff, 0, 1], "fake.nc", null, ImportKind.Gcode));
        Assert.Throws<FormatException>(() => ImportValidator.Validate(new byte[4_000_001], "big.nc", null, ImportKind.Gcode));
        Assert.Throws<FormatException>(() => ImportValidator.Validate("G21 G90\nM5\nG2 X1 Y1 I1\nM5"u8.ToArray(), "arc.nc", null, ImportKind.Gcode));
    }
    [Fact] public void SvgAndProfileMustContainExpectedDocuments()
    {
        Assert.Equal(ImportKind.Svg, ImportValidator.Validate("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "a.svg", "image/svg+xml", ImportKind.Svg).Kind);
        Assert.Throws<FormatException>(() => ImportValidator.Validate("<html/>"u8.ToArray(), "a.svg", "image/svg+xml", ImportKind.Svg));
        Assert.Throws<FormatException>(() => ImportValidator.Validate("<svg><script/></svg>"u8.ToArray(), "a.svg", null, ImportKind.Svg));
        Assert.Throws<FormatException>(() => ImportValidator.Validate("[]"u8.ToArray(), "profile.json", "application/json", ImportKind.DeviceProfile));
    }
}
