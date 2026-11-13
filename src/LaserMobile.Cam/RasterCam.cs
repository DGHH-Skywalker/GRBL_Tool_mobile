using System.Globalization;
using System.Text;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.Cam;

public enum RasterMode { Grayscale, Threshold, FloydSteinberg }
public sealed record RasterSettings(double WidthMillimeters, double LineSpacing, double Feed, double Power, double Gamma = 1, bool Invert = false, bool Bidirectional = true, RasterMode Mode = RasterMode.Grayscale, double? HeightMillimeters = null);
public static class RasterCam
{
    public static ProgramAnalysis Generate(byte[] luminance, int width, int height, RasterSettings settings, JobLimits limits, CancellationToken token = default)
    {
        limits.Validate();
        if (width < 1 || height < 1 || (long)width * height > 1_000_000 || luminance.Length != width * height) throw new ArgumentException("图像采样预算超限");
        if (!double.IsFinite(settings.WidthMillimeters) || settings.WidthMillimeters <= 0 || settings.WidthMillimeters > limits.Width || !double.IsFinite(settings.LineSpacing) || settings.LineSpacing is < 0.05 or > 2 || !double.IsFinite(settings.Gamma) || settings.Gamma is < 0.1 or > 5 || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || settings.Power <= 0 || settings.Power > limits.MaxPower) throw new ArgumentException("转换参数超限");
        if (settings.HeightMillimeters is { } requestedHeight && (!double.IsFinite(requestedHeight) || requestedHeight <= 0 || requestedHeight > limits.Height)) throw new ArgumentException("图像高度超出工作区域");
        var values = luminance.Select(v => (byte)Math.Clamp(Math.Round(Math.Pow(v / 255.0, settings.Gamma) * 255), 0, 255)).ToArray();
        if (settings.Invert) for (int i = 0; i < values.Length; i++) values[i] = (byte)(255 - values[i]);
        if (settings.Mode == RasterMode.FloydSteinberg) Diffusion.Apply(values, width, height);
        var result = new StringBuilder("G21\nG90\nM5\nS0\nM4\n");
        double pitch = settings.WidthMillimeters / width;
        for (int row = 0; row < height; row++)
        {
            token.ThrowIfCancellationRequested();
            bool reverse = settings.Bidirectional && row % 2 == 1;
            double y = settings.HeightMillimeters is { } physicalHeight ? row * physicalHeight / Math.Max(1, height - 1) : row * settings.LineSpacing;
            if (y > limits.Height) throw new ArgumentException("图像高度超出工作区域");
            result.AppendLine(FormattableString.Invariant($"G0 X{(reverse ? settings.WidthMillimeters : 0):0.###} Y{y:0.###}"));

            for (int col = 0; col < width; col++)
            {
                int source = reverse ? width - 1 - col : col;
                var light = values[row * width + source];
                int power = (int)Math.Round(settings.Power * (settings.Mode == RasterMode.Threshold ? (light < 128 ? 1 : 0) : (255 - light) / 255.0));
                bool endRun = col == width - 1;
                if (!endRun)
                {
                    int nextSource = reverse ? source - 1 : source + 1;
                    var nextLight = values[row * width + nextSource];
                    int nextPower = (int)Math.Round(settings.Power * (settings.Mode == RasterMode.Threshold ? (nextLight < 128 ? 1 : 0) : (255 - nextLight) / 255.0));
                    endRun = power != nextPower;
                }
                if (!endRun) continue;
                double x = reverse ? (width - col - 1) * pitch : (col + 1) * pitch;
                result.AppendLine(FormattableString.Invariant($"G1 X{x:0.###} Y{y:0.###} S{power} F{settings.Feed:0.###}"));

            }
            result.AppendLine("S0");
        }
        result.AppendLine("M5");
        return GcodeProgram.Prepare(result.ToString(), limits);
    }
    public static ProgramAnalysis Frame(ProgramAnalysis job, double feed, JobLimits limits)
    {
        var s = FormattableString.Invariant($"G21\nG90\nM5\nG0 X{job.MinX:0.###} Y{job.MinY:0.###}\nG1 X{job.MaxX:0.###} Y{job.MinY:0.###} F{feed:0.###}\nG1 X{job.MaxX:0.###} Y{job.MaxY:0.###}\nG1 X{job.MinX:0.###} Y{job.MaxY:0.###}\nG1 X{job.MinX:0.###} Y{job.MinY:0.###}\nM5\n");
        return GcodeProgram.Prepare(s, limits);
    }
}
