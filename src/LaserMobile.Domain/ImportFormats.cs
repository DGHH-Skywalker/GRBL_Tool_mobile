namespace LaserMobile.Domain;

public enum ImportKind { Bitmap, Svg, Gcode, DeviceProfile }
public sealed record ImportFormat(ImportKind Kind, IReadOnlyList<string> Extensions, IReadOnlyList<string> MimeTypes, int MaximumBytes);

/// <summary>One allowlist for picker hints, displayed support and authoritative import validation.</summary>
public static class ImportFormats
{
    private static IReadOnlyList<string> List(params string[] values) => Array.AsReadOnly(values);
    private static readonly ImportFormat[] Formats =
    [
        new(ImportKind.Bitmap, List(".png", ".jpg", ".jpeg", ".webp", ".bmp"), List("image/png", "image/jpeg", "image/webp", "image/bmp", "image/x-ms-bmp"), 16_000_000),
        new(ImportKind.Svg, List(".svg"), List("image/svg+xml", "application/xml", "text/xml"), 2_000_000),
        new(ImportKind.Gcode, List(".nc", ".gcode", ".gc", ".ngc", ".tap", ".cnc"), List("text/plain", "text/x-gcode", "text/x.gcode", "application/x-gcode", "text/x-cnc", "application/x-netcdf", "application/octet-stream"), 4_000_000),
        new(ImportKind.DeviceProfile, List(".json"), List("application/json", "text/json"), 65_536)
    ];
    public static ImportFormat Get(ImportKind kind) => Formats.Single(f => f.Kind == kind);
    public static ImportFormat ForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Any(char.IsControl) || name.Contains('/') || name.Contains('\\') || name != name.Trim())
            throw new FormatException("文件名无效，请选择带有正确扩展名的文件");
        var extension = Path.GetExtension(name);
        return Formats.SingleOrDefault(f => f.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new FormatException("不支持此文件格式。请选择 PNG、JPG、WebP、BMP、SVG 或 G-code 文件");
    }
    public static ImportFormat CheckMetadata(string name, string? mimeType, ImportKind expected)
    {
        var format = ForName(name);
        if (format.Kind != expected) throw new FormatException("文件与所选导入类别不一致");
        var mime = mimeType?.Split(';')[0].Trim().ToLowerInvariant();
        // Some document providers omit MIME or use a generic value. Content remains authoritative.
        if (!string.IsNullOrEmpty(mime) && mime != "application/octet-stream" && !format.MimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase)
            && !(expected is ImportKind.Svg or ImportKind.DeviceProfile && mime == "text/plain"))
            throw new FormatException("文件类型与扩展名不一致");
        return format;
    }
}
