using System.Text;
using Clipper2Lib;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Imaging.Skia.Upstream;
namespace LaserMobile.Imaging.Skia;

/// <summary>Desktop LaserGRBL's CsPotrace, with bounded mobile adapters and linear G-code export.</summary>
public static class LaserGrblContours
{
    public static ProgramAnalysis Generate(byte[] gray, int width, int height, double physicalWidth, double physicalHeight, RasterSettings settings, ContourSettings options, JobLimits limits, CancellationToken token = default)
    {
        options.Validate(); limits.Validate(); token.ThrowIfCancellationRequested();
        if (width < 2 || height < 2 || (long)width * height > ImageSizing.SampleBudget || gray.Length != width * height) throw new ArgumentException("轮廓图像尺寸无效");
        if (!double.IsFinite(physicalWidth) || !double.IsFinite(physicalHeight) || physicalWidth <= 0 || physicalHeight <= 0 || physicalWidth > limits.Width || physicalHeight > limits.Height || !double.IsFinite(settings.Feed) || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || !double.IsFinite(settings.Power) || settings.Power <= 0 || settings.Power > limits.MaxPower) throw new ArgumentException("轮廓参数超限");
        var tones = LaserGrblImageMath.Tone(gray, settings.Gamma, settings.Invert, token);
        int threshold = options.AdaptiveThreshold ? AutomaticThreshold(tones) : settings.Threshold;
        var tracer = new Potrace { Treshold = threshold / 255.0, turdsize = Math.Max(2, (int)(options.MinimumLengthPixels * options.MinimumLengthPixels / 16)), alphamax = Math.Min(1.333, options.Sensitivity / 5), opttolerance = .2 };
        var curves = tracer.PotraceTrace(tones, width, height, token); tracer.Clear();
        double sx = physicalWidth / width, sy = physicalHeight / height;
        var paths = new List<PathD>(); int pointCount = 0;
        foreach (var loop in curves)
        {
            token.ThrowIfCancellationRequested(); if (loop.Count == 0) continue;
            var path = new PathD { Point(loop[0].A) };
            foreach (var curve in loop)
            {
                token.ThrowIfCancellationRequested();
                if (curve.Kind == CurveKind.Line) Add(Point(curve.B));
                else Flatten(Point(curve.A), Point(curve.ControlPointA), Point(curve.ControlPointB), Point(curve.B), 0);
            }
            if (path.Count < 3) continue;
            if (path[^1] != path[0]) Add(path[0]);
            double perimeter = 0;
            for (int i = 1; i < path.Count; i++) perimeter += Math.Sqrt(Square(path[i], path[i - 1]));
            if (perimeter / Math.Sqrt(sx * sy) < options.MinimumLengthPixels) continue;
            var simple = Clipper.SimplifyPath(path, options.SimplificationMillimeters, true);
            if (simple.Count >= 3) paths.Add(simple);
            void Add(PointD p) { if (++pointCount > 150000) throw new FormatException("轮廓段数超限，请降低处理像素"); if (path.Count == 0 || p != path[^1]) path.Add(p); }
            void Flatten(PointD a, PointD b, PointD c, PointD d, int depth)
            {
                token.ThrowIfCancellationRequested();
                double tolerance = options.SimplificationMillimeters / 2;
                if (depth >= 16 || Math.Max(DistanceToLine(b, a, d), DistanceToLine(c, a, d)) <= tolerance) { Add(d); return; }
                var ab = Mid(a, b); var bc = Mid(b, c); var cd = Mid(c, d); var abc = Mid(ab, bc); var bcd = Mid(bc, cd); var mid = Mid(abc, bcd);
                Flatten(a, ab, abc, mid, depth + 1); Flatten(mid, bcd, cd, d, depth + 1);
            }
        }
        if (paths.Count == 0) throw new FormatException("未提取到有效轮廓，请调整阈值或最短轮廓长度");
        var code = new StringBuilder("G21 G90 G54 G17 G94\nM5\n"); PointD current = new(0, 0);
        // LaserGRBL OptimizePaths nearest-neighbour metric, evaluated on demand instead of an N×N matrix.
        while (paths.Count > 0)
        {
            token.ThrowIfCancellationRequested(); int best = 0; double distance = double.MaxValue;
            for (int i = 0; i < paths.Count; i++) { double candidate = Square(current, paths[i][0]); if (candidate < distance) { best = i; distance = candidate; } }
            var path = paths[best]; paths.RemoveAt(best);
            code.AppendLine(FormattableString.Invariant($"M5\nG0 X{path[0].x:0.###} Y{path[0].y:0.###}\nM4 S{settings.Power:0.###}"));
            foreach (var p in path.Skip(1)) code.AppendLine(FormattableString.Invariant($"G1 X{p.x:0.###} Y{p.y:0.###} F{settings.Feed:0.###}"));
            if (path[^1] != path[0]) code.AppendLine(FormattableString.Invariant($"G1 X{path[0].x:0.###} Y{path[0].y:0.###} F{settings.Feed:0.###}"));
            code.AppendLine("M5"); current = path[0];
        }
        return GcodeProgram.Prepare(code.ToString(), limits);
        PointD Point(dPoint p) => new(Math.Clamp(p.X * sx, 0, physicalWidth), Math.Clamp(p.Y * sy, 0, physicalHeight));
    }
    private static PointD Mid(PointD a, PointD b) => new((a.x + b.x) / 2, (a.y + b.y) / 2);
    private static double Square(PointD a, PointD b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
    private static double DistanceToLine(PointD p, PointD a, PointD b)
    { double length = Square(a, b); return length < 1e-20 ? Math.Sqrt(Square(p, a)) : Math.Abs((b.x - a.x) * (a.y - p.y) - (a.x - p.x) * (b.y - a.y)) / Math.Sqrt(length); }
    public static byte AutomaticThreshold(byte[] pixels)
    {
        var histogram = new int[256]; foreach (byte p in pixels) histogram[p]++;
        long totalSum = 0; for (int i = 0; i < 256; i++) totalSum += (long)i * histogram[i];
        long backgroundSum = 0, background = 0; double best = -1; int threshold = 128;
        for (int i = 0; i < 255; i++)
        {
            background += histogram[i]; backgroundSum += (long)i * histogram[i]; long foreground = pixels.Length - background;
            if (background == 0 || foreground == 0) continue;
            double difference = (double)backgroundSum / background - (double)(totalSum - backgroundSum) / foreground;
            double variance = (double)background * foreground * difference * difference;
            if (variance > best) { best = variance; threshold = i + 1; }
        }
        return (byte)threshold;
    }
}
