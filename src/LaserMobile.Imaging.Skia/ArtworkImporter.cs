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
namespace LaserMobile.Imaging.Skia;

public sealed record ImageEdits(double Left = 0, double Top = 0, double Right = 1, double Bottom = 1, int QuarterTurns = 0);

public static class ArtworkImporter
{
    public sealed record Conversion(ProgramAnalysis Analysis, ImagePreparation Image);
    public static ProgramAnalysis Convert(byte[] bytes, string name, RasterSettings settings, JobLimits limits, bool outline, CancellationToken token, ImageEdits? edits = null, ImageProcessingOptions? options = null, ContourSettings? contours = null)
        => ConvertDetailed(bytes, name, settings, limits, outline, token, edits, options, contours).Analysis;
    public static Conversion ConvertDetailed(byte[] bytes, string name, RasterSettings settings, JobLimits limits, bool outline, CancellationToken token, ImageEdits? edits = null, ImageProcessingOptions? options = null, ContourSettings? contours = null)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length > 16_000_000) throw new InvalidDataException("素材超过 16 MB");
        options ??= ImageProcessingOptions.For(limits); options.Validate();
        if (!double.IsFinite(settings.LineSpacing) || settings.LineSpacing is < .05 or > 2 || !double.IsFinite(settings.Gamma) || settings.Gamma is < .1 or > 5 || !double.IsFinite(settings.Feed) || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || !double.IsFinite(settings.Power) || settings.Power <= 0 || settings.Power > limits.MaxPower) throw new ArgumentException("转换参数超限");
        if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var xml = ValidateSvg(bytes);
            if (outline && contours is null)
            {
                var analysis = Outline(xml, settings, limits, token, options);
                return new(analysis, new(0, 0, analysis.MaxX, analysis.MaxY));
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
            using var document = new SKSvg(); var picture = document.Load(stream) ?? throw new FormatException("SVG 解码失败");
            var bounds = picture.CullRect;
            var physical = ImageSizing.FitPhysical(bounds.Width, bounds.Height, settings.WidthMillimeters, options, limits);
            var sample = Sample(bounds.Width, bounds.Height, physical.Width, physical.Height, settings, options, contours is not null);
            using var bitmap = new SKBitmap(sample.Width, sample.Height); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            canvas.Scale(sample.Width / bounds.Width, sample.Height / bounds.Height); canvas.Translate(-bounds.Left, -bounds.Top); canvas.DrawPicture(picture);
            return Generate(bitmap, physical.Width, physical.Height, settings, limits, contours, token);
        }
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data) ?? throw new FormatException("图片格式不支持");
        edits ??= new ImageEdits();
        if (new[] { edits.Left, edits.Top, edits.Right, edits.Bottom }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || edits.Right <= edits.Left || edits.Bottom <= edits.Top || edits.QuarterTurns is < 0 or > 3) throw new ArgumentException("裁剪范围需为 0–1 且右下角大于左上角");
        bool swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        double imageWidth = swap ? codec.Info.Height : codec.Info.Width, imageHeight = swap ? codec.Info.Width : codec.Info.Height;
        double cropWidth = imageWidth * (edits.Right - edits.Left), cropHeight = imageHeight * (edits.Bottom - edits.Top);
        double aspectWidth = cropWidth, aspectHeight = cropHeight;
        if (edits.QuarterTurns % 2 == 1) (aspectWidth, aspectHeight) = (aspectHeight, aspectWidth);
        var fitted = ImageSizing.FitPhysical(aspectWidth, aspectHeight, settings.WidthMillimeters, options, limits);
        var pixels = Sample(aspectWidth, aspectHeight, fitted.Width, fitted.Height, settings, options, contours is not null);
        double decodeScale = Math.Min(1, Math.Max(pixels.Width / aspectWidth, pixels.Height / aspectHeight));
        using var resized = new SKBitmap(pixels.Width, pixels.Height);
        using (var decoded = ImageSizing.Decode(codec, Math.Max(1, (int)Math.Ceiling(codec.Info.Width * decodeScale)), Math.Max(1, (int)Math.Ceiling(codec.Info.Height * decodeScale))))
        using (var c = new SKCanvas(resized))
        {
            c.Clear(SKColors.White);
            double orientedWidth = swap ? decoded.Height : decoded.Width, orientedHeight = swap ? decoded.Width : decoded.Height;
            var crop = new SKRect((float)(edits.Left * orientedWidth), (float)(edits.Top * orientedHeight), (float)(edits.Right * orientedWidth), (float)(edits.Bottom * orientedHeight));
            float dw = edits.QuarterTurns % 2 == 0 ? pixels.Width : pixels.Height, dh = edits.QuarterTurns % 2 == 0 ? pixels.Height : pixels.Width;
            c.Translate(pixels.Width / 2f, pixels.Height / 2f); c.RotateDegrees(edits.QuarterTurns * 90);
            c.Scale(dw / crop.Width, dh / crop.Height); c.Translate(-crop.MidX, -crop.MidY);
            var matrix = ExifMatrix(decoded.Width, decoded.Height, codec.EncodedOrigin); c.Concat(matrix);
            using var image = SKImage.FromBitmap(decoded); using var paint = new SKPaint { IsAntialias = true };
            c.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        token.ThrowIfCancellationRequested();
        return Generate(resized, fitted.Width, fitted.Height, settings, limits, contours, token);
    }
    private static (int Width, int Height) Sample(double sourceWidth, double sourceHeight, double physicalWidth, double physicalHeight, RasterSettings settings, ImageProcessingOptions options, bool contour)
    {
        return contour
            ? ImageSizing.FitPixels(sourceWidth, sourceHeight, options.MaxWidthPixels, options.MaxHeightPixels)
            : ImageSizing.FitPixels(Math.Max(1, Math.Ceiling(physicalWidth / settings.LineSpacing)), Math.Max(1, Math.Ceiling(physicalHeight / settings.LineSpacing)), options.MaxWidthPixels, options.MaxHeightPixels);
    }
    private static SKMatrix ExifMatrix(float w, float h, SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity
    };
    private static Conversion Generate(SKBitmap bitmap, double physicalWidth, double physicalHeight, RasterSettings settings, JobLimits limits, ContourSettings? contours, CancellationToken token)
    {
        byte[] luminance = new byte[checked(bitmap.Width * bitmap.Height)];
        for (int y = 0; y < bitmap.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, bitmap.Height - y - 1);
                luminance[y * bitmap.Width + x] = LaserGrblImageMath.OpticalGray(p.Red, p.Green, p.Blue, p.Alpha);
            }
        }
        var effective = settings with { WidthMillimeters = physicalWidth, HeightMillimeters = physicalHeight };
        var analysis = contours is null ? RasterCam.Generate(luminance, bitmap.Width, bitmap.Height, effective, limits, token)
            : OpenCvContours.Generate(luminance, bitmap.Width, bitmap.Height, physicalWidth, physicalHeight, effective, contours, limits, token);
        string? method = contours is null ? null : OpenCvContours.Name(OpenCvContours.SelectProfile(LaserGrblImageMath.Tone(luminance, settings.Gamma, settings.Invert, token), contours.Profile));
        return new(analysis, new(bitmap.Width, bitmap.Height, physicalWidth, physicalHeight, method));
    }
    public static XDocument ValidateSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "svg") throw new FormatException("根元素必须为 SVG");
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
    private static ProgramAnalysis Outline(XDocument xml, RasterSettings settings, JobLimits limits, CancellationToken token, ImageProcessingOptions options)
    {
        var paths = new List<SKPath>();
        try
        {
            foreach (var e in xml.Descendants())
            {
                token.ThrowIfCancellationRequested();
                if (e.Attribute("transform") is not null || e.Attribute("style") is not null || e.Attribute("display") is not null || e.Attribute("visibility") is not null || e.Attribute("clip-path") is not null || e.Attribute("mask") is not null || e.Attribute("opacity") is not null || e.Attribute("fill-opacity") is not null || e.Attribute("stroke-opacity") is not null)
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
            double pathWidth = maxX - minX, pathHeight = maxY - minY;
            // A horizontal SVG path has a valid zero-height envelope.
            double fittedWidth = pathHeight == 0 && pathWidth > 0
                ? Math.Min(settings.WidthMillimeters, Math.Min(options.MaxWidthMillimeters, limits.Width))
                : ImageSizing.FitPhysical(pathWidth, pathHeight, settings.WidthMillimeters, options, limits).Width;
            double scale = fittedWidth / pathWidth;
            if (!double.IsFinite(scale) || scale <= 0) throw new FormatException("SVG 宽度无效");
            var code = new StringBuilder("G21\nG90\nM5\n"); int total = 0;
            foreach (var path in paths)
            {
                using var measure = new SKPathMeasure(path, false);
                do
                {
                    double samples = Math.Ceiling(measure.Length * scale / 0.1);
                    if (!double.IsFinite(samples) || samples > 100000) throw new FormatException("轮廓长度或采样数超限");
                    var points = new PathD(); int count = Math.Max(1, (int)samples);
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
