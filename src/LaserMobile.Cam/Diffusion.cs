// Adapted from LaserGRBL ImageTransform.DitherImage, pinned in third-party/manifest.json.
// Copyright (c) 2016-2021 Diego Settimi, GPL-3.0-or-later; Cyotek kernels are MIT.
using Cyotek.Drawing;
using Cyotek.Drawing.Imaging.ColorReduction;
namespace LaserMobile.Cam;
public static class Diffusion
{
    public static void Apply(byte[] pixels, int width, int height, RasterMode mode = RasterMode.FloydSteinberg, CancellationToken token = default)
    {
        if (width < 1 || height < 1 || (long)width * height != pixels.Length) throw new ArgumentException("像素尺寸不匹配");
        IErrorDiffusion dither = mode switch
        {
            RasterMode.Atkinson => new AtkinsonDithering(), RasterMode.Burkes => new BurksDithering(),
            RasterMode.Jarvis => new JarvisJudiceNinkeDithering(), RasterMode.Stucki => new StuckiDithering(),
            RasterMode.Sierra2 => new Sierra2Dithering(), RasterMode.Sierra3 => new Sierra3Dithering(),
            RasterMode.SierraLite => new SierraLiteDithering(), _ => new FloydSteinbergDithering()
        };
        var data = new ArgbColor[pixels.Length];
        for (int i = 0; i < data.Length; i++) data[i] = new ArgbColor(255, pixels[i], pixels[i], pixels[i]);
        for (int row = 0; row < height; row++)
        {
            token.ThrowIfCancellationRequested();
            for (int col = 0; col < width; col++)
            {
                int index = row * width + col; var current = data[index];
                byte gray = (byte)(.299 * current.R + .587 * current.G + .114 * current.B);
                var transformed = gray < 128 ? new ArgbColor(current.A, 0, 0, 0) : new ArgbColor(current.A, 255, 255, 255);
                data[index] = transformed; dither.Diffuse(data, current, transformed, col, row, width, height);
                pixels[index] = transformed.R;
            }
        }
    }
}
