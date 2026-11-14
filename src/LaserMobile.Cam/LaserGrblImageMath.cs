// Extracted from arkypita/LaserGRBL ImageTransform.GrayScale/Threshold and GrblFile.GetColor.
// Copyright (c) 2016-2021 Diego Settimi, GPL-3.0-or-later.
// Pinned upstream and modifications: third-party/manifest.json.
namespace LaserMobile.Cam;
public static class LaserGrblImageMath
{
    public static byte OpticalGray(byte red, byte green, byte blue, byte alpha = 255)
    {
        double gray = .299 * red + .587 * green + .114 * blue;
        return (byte)Math.Clamp(Math.Round((gray * alpha + 255 * (255 - alpha)) / 255), 0, 255);
    }
    public static byte[] Tone(byte[] pixels, double gamma, bool invert, CancellationToken token = default)
    {
        if (!double.IsFinite(gamma) || gamma is < .1 or > 5) throw new ArgumentException("Gamma 需为 0.1–5");
        var table = new byte[256];
        for (int i = 0; i < table.Length; i++) { byte value = (byte)Math.Clamp(Math.Round(Math.Pow(i / 255.0, gamma) * 255), 0, 255); table[i] = invert ? (byte)(255 - value) : value; }
        var result = new byte[pixels.Length];
        for (int i = 0; i < result.Length; i++) { if ((i & 4095) == 0) token.ThrowIfCancellationRequested(); result[i] = table[pixels[i]]; }
        return result;
    }
    public static int Power(byte light, double maximum, RasterMode mode, byte threshold)
    {
        int rv = mode == RasterMode.Threshold ? light < threshold ? 255 : 0 : 255 - light;
        return rv == 0 ? 0 : (int)(rv * maximum / 255);
    }
}
