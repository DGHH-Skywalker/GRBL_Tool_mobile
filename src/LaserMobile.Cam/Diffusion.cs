// Adapted from Cyotek ErrorDiffusionDithering / FloydSteinbergDithering, Copyright © 2015 Cyotek Ltd.
// MIT; see third-party/notices/cyotek-MIT.txt and third-party/manifest.json.
// Upstream snapshot: LaserGRBL 1f9337b3, RasterConverter/Dithering.
// Adaptation: grayscale byte buffer; allow row/column zero; remove System.Drawing.
namespace LaserMobile.Cam;
public static class Diffusion
{
    private static readonly int[,] Matrix = { { 0, 0, 7 }, { 3, 5, 1 } };
    public static void Apply(byte[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height != pixels.Length) throw new ArgumentException("像素尺寸不匹配");
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int original = pixels[y * width + x], quantized = original < 128 ? 0 : 255;
            pixels[y * width + x] = (byte)quantized;
            int error = original - quantized;
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 3; col++)
            {
                int nx = x + col - 1, ny = y + row, weight = Matrix[row, col];
                if (weight != 0 && nx >= 0 && nx < width && ny >= 0 && ny < height)
                    pixels[ny * width + nx] = (byte)Math.Clamp(pixels[ny * width + nx] + ((error * weight) >> 4), 0, 255);
            }
        }
    }
}
