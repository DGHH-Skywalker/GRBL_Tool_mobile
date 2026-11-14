using LaserMobile.Domain;
using SkiaSharp;
namespace LaserMobile.Imaging.Skia;

public sealed record ImageProcessingOptions(int MaxWidthPixels = 1000, int MaxHeightPixels = 1000, double MaxWidthMillimeters = 40, double MaxHeightMillimeters = 40)
{
    public static ImageProcessingOptions For(JobLimits limits) => new(MaxWidthMillimeters: limits.Width, MaxHeightMillimeters: limits.Height);
    public void Validate()
    {
        if (MaxWidthPixels is < 16 or > 4096 || MaxHeightPixels is < 16 or > 4096 || !double.IsFinite(MaxWidthMillimeters) || !double.IsFinite(MaxHeightMillimeters) || MaxWidthMillimeters <= 0 || MaxHeightMillimeters <= 0)
            throw new ArgumentException("最大处理像素需为 16–4096，加工尺寸上限需为正数");
    }
}
public sealed record ContourSettings(double Sensitivity = 5, double MinimumLengthPixels = 8, double SimplificationMillimeters = .005, bool AdaptiveThreshold = true, ContourProfile Profile = ContourProfile.Automatic)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Profile) || !double.IsFinite(Sensitivity) || Sensitivity is < 1 or > 10 || !double.IsFinite(MinimumLengthPixels) || MinimumLengthPixels is < 2 or > 1000 || !double.IsFinite(SimplificationMillimeters) || SimplificationMillimeters is < .001 or > 1)
            throw new ArgumentException("轮廓灵敏度需为 1–10，最短长度需为 2–1000 像素");
    }
}
public sealed record ImagePreparation(int WidthPixels, int HeightPixels, double WidthMillimeters, double HeightMillimeters);
public static class ImageSizing
{
    public const long MaximumSourcePixels = 48_000_000;
    public const int SampleBudget = 1_000_000;
    public static (int Width, int Height) FitPixels(double width, double height, int maxWidth, int maxHeight, int budget = SampleBudget)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || maxWidth <= 0 || maxHeight <= 0 || budget <= 0) throw new ArgumentException("图像尺寸无效");
        double scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        scale = Math.Min(scale, Math.Sqrt(budget / (width * height)));
        return (Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }
    public static (double Width, double Height) FitPhysical(double aspectWidth, double aspectHeight, double requestedWidth, ImageProcessingOptions options, JobLimits limits)
    {
        options.Validate(); limits.Validate();
        if (!double.IsFinite(aspectWidth) || !double.IsFinite(aspectHeight) || aspectWidth <= 0 || aspectHeight <= 0 || !double.IsFinite(requestedWidth) || requestedWidth <= 0) throw new ArgumentException("加工尺寸无效");
        double height = requestedWidth * aspectHeight / aspectWidth;
        double ratio = Math.Min(1, Math.Min(Math.Min(options.MaxWidthMillimeters, limits.Width) / requestedWidth, Math.Min(options.MaxHeightMillimeters, limits.Height) / height));
        return (requestedWidth * ratio, height * ratio);
    }
    public static SKBitmap Decode(SKCodec codec, int desiredWidth, int desiredHeight)
    {
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumSourcePixels) throw new FormatException("原图解码预算为 4800 万像素，请使用较小的源图");
        float scale = (float)Math.Min(1, Math.Max((double)desiredWidth / info.Width, (double)desiredHeight / info.Height));
        var supported = codec.GetScaledDimensions(scale);
        var bitmap = new SKBitmap(supported.Width, supported.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        { bitmap.Dispose(); throw new FormatException("图片不完整或解码失败"); }
        return bitmap;
    }
}
