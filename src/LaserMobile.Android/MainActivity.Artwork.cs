using Android.Views;
using Android.Widget;
using LaserMobile.Imaging.Skia;
namespace LaserMobile.AndroidApp;

public sealed partial class MainActivity
{
    private readonly List<View> artworkFormRows = [];
    private CheckBox showTravel = null!, automaticThreshold = null!;
    private EditText threshold = null!;
    private LinearLayout CompactRow(LinearLayout parent)
    { var row = Row(parent); artworkFormRows.Add(row); return row; }
    private EditText CompactNumber(LinearLayout row, string caption, string title, string value, string key)
    {
        var cell = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var layout = new LinearLayout.LayoutParams(0, -2, 1); layout.SetMargins(Dp(3), 0, Dp(3), 0); row.AddView(cell, layout);
        var input = Number(cell, title, value, key); input.TextSize = 14;
        if (cell.GetChildAt(0) is TextView label) { label.Text = caption; label.TextSize = 10; label.SetPadding(Dp(4), 0, 0, 0); }
        return input;
    }
    private void UpdateArtworkKeyboard(bool keyboard)
    {
        View? focusRow = null;
        foreach (var row in artworkFormRows)
            for (View? focus = CurrentFocus; focus is not null; focus = focus.Parent as View)
                if (ReferenceEquals(row, focus)) { focusRow = row; break; }
        foreach (var row in artworkFormRows) row.Visibility = keyboard && focusRow is not null && row != focusRow ? ViewStates.Gone : ViewStates.Visible;
    }
    private void BuildArtworkForms(LinearLayout parameters, LinearLayout adjustments)
    {
        var row = CompactRow(parameters);
        artWidth = CompactNumber(row, "加工宽 mm", "加工宽度 mm", "30", "artWidth");
        spacing = CompactNumber(row, "光栅行距 mm", "光栅行距 mm", "0.15", "spacing");
        power = CompactNumber(row, "功率 S", "加工最大功率 S", "100", "power");
        row = CompactRow(parameters);
        feed = CompactNumber(row, "速度 mm/min", "加工进给 mm/min", "600", "feed");
        gamma = CompactNumber(row, "Gamma", "Gamma", "1", "gamma");
        threshold = CompactNumber(row, "二值阈值", "二值阈值（0–255）", "128", "threshold");
        row = CompactRow(parameters);
        imageMaxWidth = CompactNumber(row, "最大宽 mm", "最大图像宽度 mm", "40", "imageMaxWidth");
        imageMaxHeight = CompactNumber(row, "最大高 mm", "最大图像高度 mm", "40", "imageMaxHeight");
        contourSensitivity = CompactNumber(row, "轮廓细节", "轮廓细节（1–10）", "5", "contourSensitivity");
        row = CompactRow(parameters);
        pixelMaxWidth = CompactNumber(row, "像素宽 px", "最大处理宽度 px", "1000", "pixelMaxWidth");
        pixelMaxHeight = CompactNumber(row, "像素高 px", "最大处理高度 px", "1000", "pixelMaxHeight");
        contourMinimum = CompactNumber(row, "照片去噪 px", "照片去噪长度 px", "8", "contourMinimum");
        row = CompactRow(parameters);
        algorithmButton = Button(row, AlgorithmNames[(int)generationMode], () => { Choices("选择生成方式", AlgorithmNames, index => { generationMode = (GenerationMode)index; algorithmButton.Text = AlgorithmNames[index]; InvalidateDraft(); return Task.CompletedTask; }, (int)generationMode); return Task.CompletedTask; });
        algorithmButton.ContentDescription = "选择生成方式";
        Button? profile = null;
        profile = Button(row, OpenCvContours.Name(contourProfile), () => { Choices("轮廓策略", ["智能识别", "照片细节", "图文保真", "局部阈值"], index => { contourProfile = (ContourProfile)index; profile!.Text = OpenCvContours.Name(contourProfile); InvalidateDraft(); return Task.CompletedTask; }, (int)contourProfile); return Task.CompletedTask; }); profile.ContentDescription = "选择轮廓策略";
        row = CompactRow(parameters);
        invert = Check(row, "反色", preferences.GetBoolean("invert", false)); invert.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1);
        automaticThreshold = Check(row, "自动阈值", preferences.GetBoolean("automaticThreshold", true)); automaticThreshold.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1);
        var caption = Label(parameters, "上限等比例缩小 · 自动阈值用于轮廓", 10, 1); artworkFormRows.Add(caption);

        row = CompactRow(adjustments);
        cropLeft = CompactNumber(row, "裁剪左 · 0–1", "裁剪左", "0", "cropLeft");
        cropTop = CompactNumber(row, "裁剪上 · 0–1", "裁剪上", "0", "cropTop");
        row = CompactRow(adjustments);
        cropRight = CompactNumber(row, "裁剪右 · 0–1", "裁剪右", "1", "cropRight");
        cropBottom = CompactNumber(row, "裁剪下 · 0–1", "裁剪下", "1", "cropBottom");
        var rotation = Label(adjustments, "顺时针旋转 0°", 13, 1); artworkFormRows.Add(rotation);
        row = CompactRow(adjustments);
        Button(row, "旋转 90°", () => { quarterTurns = (quarterTurns + 1) % 4; rotation.Text = $"顺时针旋转 {quarterTurns * 90}°"; InvalidateDraft(); return Task.CompletedTask; });
        Button(row, "还原调整", () => { cropLeft.Text = cropTop.Text = "0"; cropRight.Text = cropBottom.Text = "1"; quarterTurns = 0; rotation.Text = "顺时针旋转 0°"; InvalidateDraft(); return Task.CompletedTask; });
        var hint = Label(adjustments, "裁剪比例与旋转集中设置，返回预览后生成任务。", 12, 2); artworkFormRows.Add(hint);
    }
    private byte ThresholdValue()
    {
        double value = Value(threshold);
        if (value != Math.Truncate(value) || value is < 0 or > 255) throw new ArgumentException("二值阈值需为 0–255 的整数");
        return (byte)value;
    }
}
