using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Clipper2Lib;
using SkiaSharp;
using Svg.Skia;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.AndroidApp.Imaging;

public static class ArtworkImporter
{
    public static ProgramAnalysis Convert(byte[] bytes, string name, RasterSettings settings, JobLimits limits, bool outline, CancellationToken token)
    {
        if (bytes.Length > 16_000_000) throw new InvalidDataException("素材超过 16 MB");
        bool svg = name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        if (svg)
        {
            var xml = ValidateSvg(bytes);
            if (outline) return Outline(xml, settings, limits, token);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
            using var document = new SKSvg(); var picture = document.Load(stream) ?? throw new FormatException("SVG 解码失败");
            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new FormatException("SVG 尺寸无效");
            int w = Math.Clamp((int)Math.Ceiling(settings.WidthMillimeters / settings.LineSpacing), 1, 1500);
            int h = Math.Max(1, (int)Math.Round(w * bounds.Height / bounds.Width));
            if ((long)w * h > 1_000_000) throw new FormatException("降低分辨率或缩小素材");
            using var bitmap = new SKBitmap(w, h);
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            canvas.Scale(w / bounds.Width, h / bounds.Height); canvas.Translate(-bounds.Left, -bounds.Top); canvas.DrawPicture(picture);
            return Raster(bitmap, settings, limits, token);
        }
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data) ?? throw new FormatException("图片格式不支持");
        if ((long)codec.Info.Width * codec.Info.Height > 40_000_000) throw new FormatException("原图超过 4000 万像素");
        int width = Math.Clamp((int)Math.Ceiling(settings.WidthMillimeters / settings.LineSpacing), 1, 1500);
        int height = Math.Max(1, (int)Math.Round(width * (double)codec.Info.Height / codec.Info.Width));
        if ((long)width * height > 1_000_000) throw new FormatException("采样超过 100 万像素");
        using var decoded = SKBitmap.Decode(bytes) ?? throw new FormatException("图片解码失败");
        using var resized = new SKBitmap(width, height); using var c = new SKCanvas(resized);
        c.Clear(SKColors.White); using var paint = new SKPaint { IsAntialias = true };
        c.DrawBitmap(decoded, new SKRect(0, 0, width, height), paint);
        return Raster(resized, settings, limits, token);
    }
    private static ProgramAnalysis Raster(SKBitmap bitmap, RasterSettings settings, JobLimits limits, CancellationToken token)
    {
        byte[] luminance = new byte[checked(bitmap.Width * bitmap.Height)];
        for (int y = 0; y < bitmap.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, bitmap.Height - y - 1);
                double light = (p.Red * 0.2126 + p.Green * 0.7152 + p.Blue * 0.0722);
                luminance[y * bitmap.Width + x] = (byte)Math.Round((light * p.Alpha + 255 * (255 - p.Alpha)) / 255);
            }
        }
        return RasterCam.Generate(luminance, bitmap.Width, bitmap.Height, settings, limits, token);
    }
    private static XDocument ValidateSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var document = XDocument.Load(reader);
        var nodes = document.Descendants().ToArray();
        if (nodes.Length > 20000 || nodes.Any(n => n.Ancestors().Count() > 32)) throw new FormatException("SVG 结构超限");
        foreach (var node in nodes)
        {
            if (node.Name.LocalName is "script" or "image" or "use" or "foreignObject" or "text" or "style" or "filter" or "animate" or "set") throw new FormatException($"请先将 {node.Name.LocalName} 转换为普通路径");
            foreach (var a in node.Attributes())
                if (a.Name.LocalName.Contains("href", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) || a.Value.Contains("@import", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("SVG 外部资源或脚本属性被拒绝");
        }
        return document;
    }
    private static ProgramAnalysis Outline(XDocument xml, RasterSettings settings, JobLimits limits, CancellationToken token)
    {
        var paths = new List<SKPath>();
        try
        {
            foreach (var e in xml.Descendants())
            {
                token.ThrowIfCancellationRequested();
                if (e.Attribute("transform") is not null || e.Attribute("style") is not null || e.Attribute("display") is not null || e.Attribute("visibility") is not null || e.Attribute("clip-path") is not null || e.Attribute("mask") is not null)
                    throw new FormatException("带样式、变换或裁剪的 SVG 请使用光栅模式或先展开为普通路径");
                double V(string n, double d = 0) => double.TryParse(e.Attribute(n)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : d;
                SKPath? path = null;
                switch (e.Name.LocalName)
                {
                    case "svg": case "g": case "defs": case "title": case "desc": case "metadata": continue;
                    case "path": path = SKPath.ParseSvgPathData(e.Attribute("d")?.Value ?? "") ?? throw new FormatException("SVG path 无效"); break;
                    case "rect": path = new(); path.AddRect(new((float)V("x"), (float)V("y"), (float)(V("x") + V("width")), (float)(V("y") + V("height")))); break;
                    case "circle": path = new(); path.AddCircle((float)V("cx"), (float)V("cy"), (float)V("r")); break;
                    case "ellipse": path = new(); path.AddOval(new((float)(V("cx") - V("rx")), (float)(V("cy") - V("ry")), (float)(V("cx") + V("rx")), (float)(V("cy") + V("ry")))); break;
                    case "line": path = new(); path.MoveTo((float)V("x1"), (float)V("y1")); path.LineTo((float)V("x2"), (float)V("y2")); break;
                    default: throw new FormatException($"轮廓模式尚未支持 {e.Name.LocalName}，可使用光栅模式");
                }
                if (e.Ancestors().Any(a => a.Name.LocalName == "defs")) { path?.Dispose(); continue; }
                if (path is not null) paths.Add(path);
            }
            if (paths.Count == 0) throw new FormatException("SVG 没有路径");
            float minX = paths.Min(p => p.Bounds.Left), minY = paths.Min(p => p.Bounds.Top), maxX = paths.Max(p => p.Bounds.Right), maxY = paths.Max(p => p.Bounds.Bottom);
            double scale = settings.WidthMillimeters / (maxX - minX);
            if (!double.IsFinite(scale) || scale <= 0) throw new FormatException("SVG 宽度无效");
            var code = new StringBuilder("G21\nG90\nM5\n"); int total = 0;
            foreach (var path in paths)
            {
                using var measure = new SKPathMeasure(path, false);
                do
                {
                    var points = new PathD(); int count = Math.Max(1, (int)Math.Ceiling(measure.Length * scale / 0.1));
                    if ((total += count) > 100000) throw new FormatException("轮廓段数超限");
                    for (int i = 0; i <= count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (measure.GetPositionAndTangent(measure.Length * i / count, out var p, out _)) points.Add(new((p.X - minX) * scale, (maxY - p.Y) * scale));
                    }
                    var simple = Clipper.SimplifyPath(points, 0.02, false);
                    if (simple.Count < 2) continue;
                    code.AppendLine(FormattableString.Invariant($"G0 X{simple[0].x:0.###} Y{simple[0].y:0.###}"));
                    code.AppendLine(FormattableString.Invariant($"M4 S{settings.Power:0.###}"));
                    foreach (var p in simple.Skip(1)) code.AppendLine(FormattableString.Invariant($"G1 X{p.x:0.###} Y{p.y:0.###} F{settings.Feed:0.###}"));
                    code.AppendLine("M5");
                } while (measure.NextContour());
            }
            return GcodeProgram.Prepare(code.ToString(), limits);
        }
        finally { foreach (var path in paths) path.Dispose(); }
    }
}
