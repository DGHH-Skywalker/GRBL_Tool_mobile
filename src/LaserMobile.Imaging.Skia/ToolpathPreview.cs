using LaserMobile.Domain;
using SkiaSharp;
namespace LaserMobile.Imaging.Skia;

/// <summary>Chunked immutable Skia paths avoid rebuilding each segment on every touch frame.</summary>
public sealed class ToolpathPreview : IDisposable
{
    private readonly List<(SKRect Bounds, SKPath Travel, SKPath Burn)> chunks = [];
    public ToolpathPreview(ProgramAnalysis analysis)
    {
        foreach (var block in analysis.Segments.Chunk(1024))
        {
            var travel = new SKPath(); var burn = new SKPath();
            float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            foreach (var s in block)
            {
                float x1 = (float)s.X1, y1 = (float)s.Y1, x2 = (float)s.X2, y2 = (float)s.Y2;
                var path = !s.Rapid && s.Power > 0 ? burn : travel; path.MoveTo(x1, y1); path.LineTo(x2, y2);
                left = Math.Min(left, Math.Min(x1, x2)); right = Math.Max(right, Math.Max(x1, x2));
                top = Math.Min(top, Math.Min(y1, y2)); bottom = Math.Max(bottom, Math.Max(y1, y2));
            }
            chunks.Add((new(left - .001f, top - .001f, right + .001f, bottom + .001f), travel, burn));
        }
    }
    public void Draw(SKCanvas canvas, SKRect viewport, float scale, bool showTravel = false)
    {
        using var travel = new SKPaint { Color = new SKColor(150, 160, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 1 / scale, IsAntialias = true };
        using var burn = new SKPaint { Color = new SKColor(33, 150, 243), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f / scale, IsAntialias = true };
        using var dashes = SKPathEffect.CreateDash([4 / scale, 4 / scale], 0); travel.PathEffect = dashes;
        foreach (var chunk in chunks)
        {
            if (!chunk.Bounds.IntersectsWith(viewport)) continue;
            if (showTravel) canvas.DrawPath(chunk.Travel, travel); canvas.DrawPath(chunk.Burn, burn);
        }
    }
    public void Dispose() { foreach (var chunk in chunks) { chunk.Travel.Dispose(); chunk.Burn.Dispose(); } chunks.Clear(); }
}
