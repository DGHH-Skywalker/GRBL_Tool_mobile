using System.Globalization;
using System.Text;
using Clipper2Lib;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.Imaging.Skia;

/// <summary>Bounded managed Canny pipeline, pixel graph tracing and existing Clipper2 simplification.</summary>
public static class ExperimentalContours
{
    private static readonly int[] Dx = [1, 1, 0, -1, -1, -1, 0, 1];
    private static readonly int[] Dy = [0, 1, 1, 1, 0, -1, -1, -1];
    public static ProgramAnalysis Generate(byte[] gray, int width, int height, double physicalWidth, double physicalHeight, RasterSettings settings, ContourSettings options, JobLimits limits, CancellationToken token = default)
    {
        options.Validate(); limits.Validate(); token.ThrowIfCancellationRequested();
        if (width < 3 || height < 3 || (long)width * height > ImageSizing.SampleBudget || gray.Length != width * height) throw new ArgumentException("轮廓图像尺寸无效");
        if (!double.IsFinite(physicalWidth) || !double.IsFinite(physicalHeight) || physicalWidth <= 0 || physicalHeight <= 0 || physicalWidth > limits.Width || physicalHeight > limits.Height || !double.IsFinite(settings.Feed) || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || !double.IsFinite(settings.Power) || settings.Power <= 0 || settings.Power > limits.MaxPower || !double.IsFinite(settings.Gamma) || settings.Gamma is < .1 or > 5) throw new ArgumentException("轮廓参数超限");
        bool[] edges = DetectEdges(gray, width, height, settings.Gamma, settings.Invert, options.Sensitivity, token);
        var links = new byte[gray.Length]; var code = new StringBuilder("G21 G90 G54 G17 G94\nM5\n"); int paths = 0, segments = 0;
        int Neighbors(int p, Span<int> neighbors, Span<int> dirOutput)
        {
            int x = p % width, y = p / width, count = 0;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || !edges[ny * width + nx]) continue;
                // Remove diagonal shortcuts when an orthogonal edge already connects the corner.
                if (Dx[d] != 0 && Dy[d] != 0 && (edges[y * width + nx] || edges[ny * width + x])) continue;
                neighbors[count] = ny * width + nx; dirOutput[count++] = d;
            }
            return count;
        }
        void Walk(int start, int first, int initialDirection)
        {
            var points = new PathD(); int previous = start, current = first, direction = initialDirection; double length = 0; bool closed = false;
            points.Add(new((start % width + .5) * physicalWidth / width, (start / width + .5) * physicalHeight / height));
            Span<int> next = stackalloc int[8]; Span<int> dirs = stackalloc int[8];
            for (int budget = 0; budget <= 200000; budget++)
            {
                if ((budget & 255) == 0) token.ThrowIfCancellationRequested();
                if ((links[previous] & (1 << direction)) != 0) break;
                links[previous] |= (byte)(1 << direction); links[current] |= (byte)(1 << ((direction + 4) % 8));
                length += direction % 2 == 0 ? 1 : Math.Sqrt(2);
                points.Add(new((current % width + .5) * physicalWidth / width, (current / width + .5) * physicalHeight / height));
                if (current == start) { closed = true; break; }
                int count = Neighbors(current, next, dirs);
                if (count != 2) break;
                int choice = next[0] == previous ? 1 : 0;
                previous = current; current = next[choice]; direction = dirs[choice];
            }
            if (length < options.MinimumLengthPixels || points.Count < 3) return;
            var simple = Clipper.SimplifyPath(points, options.SimplificationMillimeters, closed);
            if (simple.Count < 2) return;
            if (++paths > 5000 || (segments += simple.Count) > 100000) throw new FormatException("轮廓过多，请降低灵敏度、增加最短轮廓或减小像素上限");
            code.AppendLine(FormattableString.Invariant($"M5\nG0 X{simple[0].x:0.###} Y{simple[0].y:0.###}\nM4 S{settings.Power:0.###}"));
            foreach (var point in simple.Skip(1)) code.AppendLine(FormattableString.Invariant($"G1 X{point.x:0.###} Y{point.y:0.###} F{settings.Feed:0.###}"));
            if (closed && simple[^1] != simple[0]) code.AppendLine(FormattableString.Invariant($"G1 X{simple[0].x:0.###} Y{simple[0].y:0.###} F{settings.Feed:0.###}"));
            code.AppendLine("M5");
        }
        // Trace open paths/junctions first, then remaining closed loops. Every graph link is visited once.
        Span<int> adjacent = stackalloc int[8]; Span<int> directions = stackalloc int[8];
        for (int pass = 0; pass < 2; pass++)
        for (int p = 0; p < edges.Length; p++)
        {
            if ((p & 1023) == 0) token.ThrowIfCancellationRequested();
            if (!edges[p]) continue;
            int count = Neighbors(p, adjacent, directions); if (pass == 0 && count == 2) continue;
            for (int i = 0; i < count; i++) if ((links[p] & (1 << directions[i])) == 0) Walk(p, adjacent[i], directions[i]);
        }
        if (paths == 0) throw new FormatException("未提取到有效轮廓，请调整灵敏度或最短轮廓长度");
        return GcodeProgram.Prepare(code.ToString(), limits);
    }
    public static bool[] DetectEdges(byte[] gray, int width, int height, double gamma, bool invert, double sensitivity, CancellationToken token = default)
    {
        int size = checked(width * height); if (gray.Length != size || width < 3 || height < 3 || size > ImageSizing.SampleBudget) throw new ArgumentException("边缘图像尺寸无效");
        if (!double.IsFinite(gamma) || gamma is < .1 or > 5 || !double.IsFinite(sensitivity) || sensitivity is < 1 or > 10) throw new ArgumentException("边缘参数无效");
        var work = new float[size]; var smooth = new float[size]; int[] kernel = [1, 4, 6, 4, 1];
        var tones = Enumerable.Range(0, 256).Select(i => { float value = (float)(Math.Pow(i / 255.0, gamma) * 255); return invert ? 255 - value : value; }).ToArray();
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                float value = 0;
                for (int k = -2; k <= 2; k++) { float sample = tones[gray[y * width + Math.Clamp(x + k, 0, width - 1)]]; value += sample * kernel[k + 2]; }
                work[y * width + x] = value / 16;
            }
        }
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++) { float value = 0; for (int k = -2; k <= 2; k++) value += work[Math.Clamp(y + k, 0, height - 1) * width + x] * kernel[k + 2]; smooth[y * width + x] = value / 16; }
        }
        var magnitude = new float[size]; var direction = new byte[size];
        for (int y = 1; y < height - 1; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 1; x < width - 1; x++)
            {
                int p = y * width + x;
                float gx = -smooth[p - width - 1] + smooth[p - width + 1] - 2 * smooth[p - 1] + 2 * smooth[p + 1] - smooth[p + width - 1] + smooth[p + width + 1];
                float gy = -smooth[p - width - 1] - 2 * smooth[p - width] - smooth[p - width + 1] + smooth[p + width - 1] + 2 * smooth[p + width] + smooth[p + width + 1];
                magnitude[p] = MathF.Sqrt(gx * gx + gy * gy);
                double angle = Math.Atan2(gy, gx) * 180 / Math.PI; if (angle < 0) angle += 180;
                direction[p] = angle < 22.5 || angle >= 157.5 ? (byte)0 : angle < 67.5 ? (byte)1 : angle < 112.5 ? (byte)2 : (byte)3;
            }
        }
        Array.Clear(work); var histogram = new int[2048]; int candidates = 0;
        int[] offsets = [1, width + 1, width, width - 1];
        for (int y = 1; y < height - 1; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 1; x < width - 1; x++)
            {
                int p = y * width + x, delta = offsets[direction[p]]; float value = magnitude[p];
                if (value > 0 && value >= magnitude[p - delta] && value > magnitude[p + delta]) { work[p] = value; histogram[Math.Min(2047, (int)value)]++; candidates++; }
            }
        }
        int percentile = 0, sum = 0; for (; percentile < histogram.Length - 1; percentile++) { sum += histogram[percentile]; if (sum >= candidates * .85) break; }
        double high = Math.Max(5, Math.Min(magnitude.Max() * .95, percentile * (1.5 - sensitivity * .12))), low = high * .4;
        var edges = new bool[size]; var queue = new Queue<int>();
        for (int p = 0; p < size; p++) if (work[p] >= high) { edges[p] = true; queue.Enqueue(p); }
        int steps = 0;
        while (queue.TryDequeue(out int p))
        {
            if ((steps++ & 1023) == 0) token.ThrowIfCancellationRequested();
            int x = p % width, y = p / width;
            for (int d = 0; d < 8; d++) { int nx = x + Dx[d], ny = y + Dy[d]; if (nx <= 0 || ny <= 0 || nx >= width - 1 || ny >= height - 1) continue; int q = ny * width + nx; if (!edges[q] && work[q] >= low) { edges[q] = true; queue.Enqueue(q); } }
        }
        return edges;
    }
}
