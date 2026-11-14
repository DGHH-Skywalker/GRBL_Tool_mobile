using System.Globalization;
using System.Text;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.Cam;

public enum RasterMode { Grayscale, Threshold, FloydSteinberg, Atkinson, Burkes, Jarvis, Stucki, Sierra2, Sierra3, SierraLite }
public sealed record RasterSettings(double WidthMillimeters, double LineSpacing, double Feed, double Power, double Gamma = 1, bool Invert = false, bool Bidirectional = true, RasterMode Mode = RasterMode.Grayscale, double? HeightMillimeters = null, byte Threshold = 128);
public static class RasterCam
{
    public static ProgramAnalysis Generate(byte[] luminance, int width, int height, RasterSettings settings, JobLimits limits, CancellationToken token = default)
    {
        limits.Validate();
        if (width < 1 || height < 1 || (long)width * height > 1_000_000 || luminance.Length != width * height) throw new ArgumentException("图像采样预算超限");
        if (!double.IsFinite(settings.WidthMillimeters) || settings.WidthMillimeters <= 0 || settings.WidthMillimeters > limits.Width || !double.IsFinite(settings.LineSpacing) || settings.LineSpacing is < 0.05 or > 2 || !double.IsFinite(settings.Gamma) || settings.Gamma is < 0.1 or > 5 || !double.IsFinite(settings.Feed) || settings.Feed <= 0 || settings.Feed > limits.MaxFeed || !double.IsFinite(settings.Power) || settings.Power <= 0 || settings.Power > limits.MaxPower) throw new ArgumentException("转换参数超限");
        if (settings.HeightMillimeters is { } requestedHeight && (!double.IsFinite(requestedHeight) || requestedHeight <= 0 || requestedHeight > limits.Height)) throw new ArgumentException("图像高度超出工作区域");
        var values = LaserGrblImageMath.Tone(luminance, settings.Gamma, settings.Invert, token);
        if (settings.Mode is not (RasterMode.Grayscale or RasterMode.Threshold)) Diffusion.Apply(values, width, height, settings.Mode, token);
        var result = new StringBuilder("G21 G90 G54 G17 G94\nM5\n");
        double pitch = settings.WidthMillimeters / width;
        for (int row = 0; row < height; row++)
        {
            token.ThrowIfCancellationRequested();
            bool reverse = settings.Bidirectional && row % 2 == 1;
            double y = settings.HeightMillimeters is { } physicalHeight ? row * physicalHeight / Math.Max(1, height - 1) : row * settings.LineSpacing;
            if (y > limits.Height) throw new ArgumentException("图像高度超出工作区域");
            int previous = -1, start = 0; bool positioned = false;
            // LaserGRBL GetSegments/ExtractSegment: merge adjacent pixels of the same S value.
            for (int col = 0; col <= width; col++)
            {
                int current = col == width ? -1 : LaserGrblImageMath.Power(values[row * width + (reverse ? width - 1 - col : col)], settings.Power, settings.Mode, settings.Threshold);
                if (col == 0) { previous = current; continue; }
                if (current == previous) continue;
                if (previous > 0)
                {
                    double x0 = (reverse ? width - start : start) * pitch, x1 = (reverse ? width - col : col) * pitch;
                    if (!positioned)
                    {
                        result.AppendLine(FormattableString.Invariant($"M5\nG0 X{x0:0.###} Y{y:0.###}\nM4 S0")); positioned = true;
                    }
                    result.AppendLine(FormattableString.Invariant($"G1 X{x1:0.###} Y{y:0.###} S{previous} F{settings.Feed:0.###}"));
                }
                else positioned = false; // OptimizeLine2Line: collapse all consecutive white runs into the next G0.
                previous = current; start = col;
            }
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
