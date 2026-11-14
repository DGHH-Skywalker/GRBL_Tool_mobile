using System.Runtime.InteropServices;
using System.Text;
using Clipper2Lib;
using OpenCvSharp;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.Imaging.Skia;

public enum ContourProfile { Automatic, Photo, Graphic, Document }
public sealed record ContourMask(byte[] Pixels, ContourProfile Profile);

/// <summary>Native OpenCV detail extraction, with exact integer-cell polygonization for graphics.</summary>
public static class OpenCvContours
{
    public static ContourProfile SelectProfile(byte[] tones, ContourProfile requested)
    {
        if (requested != ContourProfile.Automatic) return requested;
        Span<int> bins = stackalloc int[16]; foreach (byte p in tones) bins[p >> 4]++;
        double entropy = 0; foreach (int n in bins) if (n > 0) { double p = (double)n / tones.Length; entropy -= p * Math.Log2(p); }
        return entropy < 3.2 ? ContourProfile.Graphic : ContourProfile.Photo;
    }
    public static string Name(ContourProfile profile) => profile switch { ContourProfile.Photo => "照片细节", ContourProfile.Graphic => "图文保真", ContourProfile.Document => "局部阈值", _ => "智能识别" };
    public static ContourMask Extract(byte[] gray, int width, int height, RasterSettings settings, ContourSettings options, CancellationToken token = default)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        if (width < 3 || height < 3 || (long)width * height > ImageSizing.SampleBudget || gray.Length != width * height) throw new ArgumentException("轮廓图像尺寸无效");
        var tones = LaserGrblImageMath.Tone(gray, settings.Gamma, settings.Invert, token);
        var profile = SelectProfile(tones, options.Profile);
        using var input = new Mat(height, width, MatType.CV_8UC1); Marshal.Copy(tones, 0, input.Data, tones.Length);
        using var mask = new Mat();
        if (profile == ContourProfile.Photo)
        {
            using var denoised = new Mat(); using var enhanced = new Mat(); using var blur = new Mat();
            Cv2.BilateralFilter(input, denoised, 5, 25, 25);
            using var clahe = Cv2.CreateCLAHE(1.8, new Size(8, 8)); clahe.Apply(denoised, enhanced);
            Cv2.GaussianBlur(enhanced, blur, new Size(3, 3), .7);
            token.ThrowIfCancellationRequested();
            using var gx = new Mat(); using var gy = new Mat(); using var magnitude = new Mat();
            Cv2.Sobel(blur, gx, MatType.CV_32F, 1, 0); Cv2.Sobel(blur, gy, MatType.CV_32F, 0, 1); Cv2.Magnitude(gx, gy, magnitude);
            magnitude.GetArray(out float[] gradients); var histogram = new int[2048]; int total = 0;
            foreach (float g in gradients) if (g > 1) { histogram[Math.Clamp((int)g, 0, 2047)]++; total++; }
            int percentile = 0, sum = 0; for (; percentile < 2047; percentile++) { sum += histogram[percentile]; if (sum >= total * .75) break; }
            double high = Math.Clamp(percentile * (1.55 - options.Sensitivity * .11), 12, 160);
            Cv2.Canny(blur, mask, high * .35, high, 3, true);
            // Filter complete isolated components; short branches inside a real contour are retained.
            using var labels = new Mat(); using var stats = new Mat(); using var centers = new Mat();
            Cv2.ConnectedComponentsWithStats(mask, labels, stats, centers, PixelConnectivity.Connectivity8, MatType.CV_32S);
            labels.GetArray(out int[] components); stats.GetArray(out int[] statistics);
            mask.GetArray(out byte[] edges);
            for (int i = 0; i < edges.Length; i++) if (edges[i] != 0 && statistics[components[i] * 5 + 4] < options.MinimumLengthPixels) edges[i] = 0;
            token.ThrowIfCancellationRequested(); return new(edges, profile);
        }
        if (!options.AdaptiveThreshold) Cv2.Threshold(input, mask, settings.Threshold - .5, 255, ThresholdTypes.BinaryInv);
        else if (profile == ContourProfile.Document)
        {
            int block = Math.Min(41, Math.Min(width, height) | 1); if (block > Math.Min(width, height)) block -= 2;
            Cv2.AdaptiveThreshold(input, mask, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, Math.Max(3, block), 7);
        }
        else Cv2.Threshold(input, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        mask.GetArray(out byte[] pixels); token.ThrowIfCancellationRequested(); return new(pixels, profile);
    }
    public static ProgramAnalysis Generate(byte[] gray, int width, int height, double physicalWidth, double physicalHeight, RasterSettings settings, ContourSettings options, JobLimits limits, CancellationToken token = default)
    {
        limits.Validate();
        if (!double.IsFinite(physicalWidth) || !double.IsFinite(physicalHeight) || physicalWidth <= 0 || physicalHeight <= 0 || physicalWidth > limits.Width || physicalHeight > limits.Height || !double.IsFinite(settings.Feed) || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || !double.IsFinite(settings.Power) || settings.Power <= 0 || settings.Power > limits.MaxPower) throw new ArgumentException("轮廓参数超限");
        var result = Extract(gray, width, height, settings, options, token);
        if (result.Profile == ContourProfile.Photo)
            return ExperimentalContours.TraceEdges(result.Pixels.Select(p => p != 0).ToArray(), width, height, physicalWidth, physicalHeight, settings, options, limits, token, true);
        return ExportRegions(result.Pixels, width, height, physicalWidth, physicalHeight, settings, options, limits, token);
    }
    public static Paths64 PixelRegions(byte[] mask, int width, int height, CancellationToken token = default)
    {
        if (width < 1 || height < 1 || (long)width * height > ImageSizing.SampleBudget || mask.Length != width * height) throw new ArgumentException("区域尺寸无效");
        var rectangles = new Paths64(); var active = new Dictionary<(int Start, int End), Path64>();
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested(); var next = new Dictionary<(int, int), Path64>();
            for (int x = 0; x < width;)
            {
                if (mask[y * width + x] == 0) { x++; continue; }
                int start = x; while (x < width && mask[y * width + x] != 0) x++;
                var key = (start, x);
                if (active.TryGetValue(key, out var rectangle)) { rectangle[2] = new(x, y + 1); rectangle[3] = new(start, y + 1); }
                else
                {
                    if (rectangles.Count >= 100000) throw new FormatException("图像噪点过多，请降低处理像素或使用照片细节模式");
                    rectangle = new Path64 { new(start, y), new(x, y), new(x, y + 1), new(start, y + 1) }; rectangles.Add(rectangle);
                }
                next.Add(key, rectangle);
            }
            active = next;
        }
        // Clipper2 boolean union removes internal cell boundaries without curve fitting or diagonal approximation.
        var paths = Clipper.Union(rectangles, FillRule.NonZero); token.ThrowIfCancellationRequested(); return paths;
    }
    private static ProgramAnalysis ExportRegions(byte[] mask, int width, int height, double physicalWidth, double physicalHeight, RasterSettings settings, ContourSettings options, JobLimits limits, CancellationToken token)
    {
        var paths = PixelRegions(mask, width, height, token); var code = new StringBuilder("G21 G90 G54 G17 G94\nM5\n");
        int points = 0, count = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            double length = 0; for (int i = 0; i < path.Count; i++) { var a = path[i]; var b = path[(i + 1) % path.Count]; length += Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); }
            if (length < options.MinimumLengthPixels || path.Count < 3) continue;
            if (++count > 12000 || (points += path.Count) > 120000) throw new FormatException("保真轮廓超过任务预算，请裁剪作品或减少噪点");
            code.AppendLine(FormattableString.Invariant($"M5\nG0 X{path[0].X * physicalWidth / width:0.#####} Y{path[0].Y * physicalHeight / height:0.#####}\nM4 S{settings.Power:0.###}"));
            for (int i = 1; i <= path.Count; i++) { var p = path[i % path.Count]; code.AppendLine(FormattableString.Invariant($"G1 X{p.X * physicalWidth / width:0.#####} Y{p.Y * physicalHeight / height:0.#####} F{settings.Feed:0.###}")); }
            code.AppendLine("M5");
        }
        if (count == 0) throw new FormatException("未提取到轮廓，请调整阈值或轮廓策略");
        return GcodeProgram.Prepare(code.ToString(), limits);
    }
}
