using System.Text;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using SkiaSharp;
namespace LaserMobile.Imaging.Skia;

public sealed record ValidatedImport(ImportKind Kind, string Name, byte[] Bytes);
public static class ImportValidator
{
    public static ValidatedImport Validate(byte[] bytes, string name, string? mime, ImportKind expected, WorkPosition? origin = null)
    {
        var format = ImportFormats.CheckMetadata(name, mime, expected);
        if (bytes.Length == 0 || bytes.Length > format.MaximumBytes) throw new FormatException($"文件为空或超过 {format.MaximumBytes / 1000} KB 上限");
        switch (format.Kind)
        {
            case ImportKind.Bitmap:
                using (var data = SKData.CreateCopy(bytes))
                using (var codec = SKCodec.Create(data) ?? throw new FormatException("图片内容无效"))
                {
                    var actual = codec.EncodedFormat;
                    bool match = Path.GetExtension(name).ToLowerInvariant() switch
                    {
                        ".png" => actual == SKEncodedImageFormat.Png,
                        ".jpg" or ".jpeg" => actual == SKEncodedImageFormat.Jpeg,
                        ".webp" => actual == SKEncodedImageFormat.Webp,
                        ".bmp" => actual == SKEncodedImageFormat.Bmp,
                        _ => false
                    };
                    if (!match) throw new FormatException("图片编码与扩展名不一致");
                    if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > ImageSizing.MaximumSourcePixels)
                        throw new FormatException("图片尺寸无效或超过 4800 万像素解码预算");
                    if (codec.FrameCount > 1) throw new FormatException("请将动态图像转换为静态图片后导入");
                    var bounded = ImageSizing.FitPixels(codec.Info.Width, codec.Info.Height, 1000, 1000);
                    using var bitmap = ImageSizing.Decode(codec, bounded.Width, bounded.Height);
                }
                break;
            case ImportKind.Svg: ArtworkImporter.ValidateSvg(bytes); break;
            case ImportKind.Gcode:
                if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bytes = bytes[3..];
                if (bytes.Any(b => b > 127 || (b < 32 && b is not (9 or 10 or 13)))) throw new FormatException("G-code 必须为 ASCII 文本，支持 UTF-8 BOM");
                GcodeProgram.Prepare(Encoding.ASCII.GetString(bytes), JobLimits.Analysis, origin);
                break;
            case ImportKind.DeviceProfile:
                // JSON schema validation stays in the existing device preset adapter.
                var text = new UTF8Encoding(false, true).GetString(bytes);
                using (var document = System.Text.Json.JsonDocument.Parse(text.TrimStart('\ufeff')))
                    if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new FormatException("设备配置必须为 JSON 对象");
                break;
        }
        return new(format.Kind, name, bytes);
    }
}
