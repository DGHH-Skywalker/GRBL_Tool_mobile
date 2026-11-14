using Android.App;
using Android.OS;
using Android.Content;
using Android.Widget;
using Android.Hardware.Usb;
using Android.Views;
using Android.Text;
using Anotherlab.UsbSerialForAndroid.Driver;
using SkiaSharp;
using SkiaSharp.Views.Android;
using LaserMobile.Application;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Persistence;
using LaserMobile.Extensions.Abstractions;
using LaserMobile.AndroidApp.Services;
using LaserMobile.AndroidApp.Usb;
using LaserMobile.Imaging.Skia;
using System.Globalization;
using System.Text;

namespace LaserMobile.AndroidApp;

[Activity(Label = "GRBL Tool Mobile", MainLauncher = true, Exported = true, ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
public sealed partial class MainActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private bool visible, converting;
    private readonly PreparationRevision draft = new();
    private ToolpathPreview? preview;
    private TextView jobSummary = null!;
    private ProgressBar jobProgress = null!;
    private Button startButton = null!, frameButton = null!, pauseButton = null!, resumeButton = null!, cancelButton = null!;
    private TextView status = null!, details = null!, artwork = null!;
    private EditText baud = null!, areaWidth = null!, areaHeight = null!, maxFeed = null!, maxPower = null!, artWidth = null!, spacing = null!, feed = null!, power = null!, gamma = null!, jogStep = null!, cropLeft = null!, cropTop = null!, cropRight = null!, cropBottom = null!;
    private int quarterTurns;
    private CheckBox invert = null!, confirmed = null!;
    private Button ports = null!, algorithmButton = null!;
    private int selectedPort = -1;
    private enum GenerationMode { Grayscale, ExperimentalContours, Threshold, FloydSteinberg, SvgOutline, Atkinson, Burkes, Jarvis, Stucki, Sierra2, Sierra3, SierraLite }
    private GenerationMode generationMode;
    private ContourProfile contourProfile;
    private static readonly string[] AlgorithmNames = ["灰度 · LaserGRBL", "轮廓 · OpenCV", "黑白阈值", "Floyd–Steinberg", "SVG 路径轮廓", "Atkinson", "Burkes", "Jarvis", "Stucki", "Sierra 2", "Sierra 3", "Sierra Lite"];
    private EditText imageMaxWidth = null!, imageMaxHeight = null!, pixelMaxWidth = null!, pixelMaxHeight = null!, contourSensitivity = null!, contourMinimum = null!;
    private ImagePreparation? imagePreparation;
    private readonly List<(UsbSerialPort Port, string Label)> devices = [];
    private readonly List<LinearLayout> pages = [];
    private readonly List<Button> navigation = [];
    private bool Dark => ((int)Resources!.Configuration!.UiMode & 0x30) == 0x20;
    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density);
    private Android.Graphics.Color TextColor => Android.Graphics.Color.ParseColor(Dark ? "#E7EDF7" : "#172842");
    private Android.Graphics.Drawables.GradientDrawable Surface(string color, int radius = 12)
    {
        var drawable = new Android.Graphics.Drawables.GradientDrawable(); drawable.SetColor(Android.Graphics.Color.ParseColor(color)); drawable.SetCornerRadius(Dp(radius)); return drawable;
    }
    private void StyleButton(Button button, bool primary)
    {
        var background = new Android.Graphics.Drawables.StateListDrawable();
        background.AddState([-Android.Resource.Attribute.StateEnabled], Surface(Dark ? "#263140" : "#E8EDF4", 8));
        background.AddState([], Surface(primary ? "#1762CC" : Dark ? "#253449" : "#EAF1FB", 8)); button.Background = new Android.Graphics.Drawables.RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Argb(36, 100, 150, 220)), background, Surface("#FFFFFF", 8));
        button.SetTextColor(new Android.Content.Res.ColorStateList([[-Android.Resource.Attribute.StateEnabled], []],
            [Android.Graphics.Color.ParseColor(Dark ? "#8090A5" : "#8794A7").ToArgb(), Android.Graphics.Color.ParseColor(primary ? "#FFFFFF" : Dark ? "#D9E8FF" : "#234D82").ToArgb()]));
        button.Elevation = 0; button.SetAllCaps(false); button.SetMinHeight(Dp(46)); button.SetPadding(Dp(10), Dp(6), Dp(10), Dp(6));
    }
    private static string StateName(ExecutionState? state) => state switch
    {
        ExecutionState.Ready => "已准备", ExecutionState.Running => "加工中", ExecutionState.Pausing => "正在暂停",
        ExecutionState.Paused => "已暂停", ExecutionState.Draining => "等待设备完成", ExecutionState.Cancelling => "正在取消",
        ExecutionState.Completed => "已完成", ExecutionState.Cancelled => "已取消", ExecutionState.Interrupted => "执行中断",
        ExecutionState.Failed => "执行失败", _ => "等待任务"
    };
    private LinearLayout root = null!;
    private SKCanvasView canvas = null!;
    private JobLibrary library = null!;
    private ExecutionStore executions = null!;
    private ProgramAnalysis? prepared;
    private StoredJob? selected;
    private CancellationTokenSource? conversion;
    private string jobName = "未命名";
    private float zoom = 1, panX, panY, lastX, lastY, lastDistance;
    private byte[]? source;
    private string sourceName = "";
    private ISharedPreferences preferences = null!;
    private readonly List<EditText> savedInputs = [];
    private async Task Scan()
    {
        var manager = (UsbManager)GetSystemService(UsbService)!;
        var drivers = await Task.Run(() => new UsbSerialProber(UsbSerialProber.DefaultProbeTable).FindAllDrivers(manager));
        devices.Clear(); foreach (var d in drivers) foreach (var port in d.Ports) devices.Add((port, $"{d.Device.VendorId:X4}:{d.Device.ProductId:X4} 端口 {port.PortNumber}"));
        selectedPort = devices.Count == 0 ? -1 : 0; ports.Text = selectedPort < 0 ? "未发现 USB 串口" : devices[selectedPort].Label;
        if (devices.Count == 0) ShowError("未发现 USB 串口，请检查 OTG 连接后重试"); else ClearNotice();
    }
    private async Task Connect()
    {
        if (SimulationService.Current is not null) throw new InvalidOperationException("请先断开演示设备");
        if (selectedPort < 0 || selectedPort >= devices.Count) throw new InvalidOperationException("请先扫描 USB 并选择串口");
        var port = devices[selectedPort].Port; var manager = (UsbManager)GetSystemService(UsbService)!;
        if (!await UsbPermissionReceiver.Request(this, manager, port.Driver.Device)) throw new UnauthorizedAccessException("USB 授权被拒绝");
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted) RequestPermissions([Android.Manifest.Permission.PostNotifications], 100);
        using var intent = new Intent(this, typeof(DeviceExecutionService)); intent.PutExtra("device", port.Driver.Device.DeviceId); intent.PutExtra("port", port.PortNumber); intent.PutExtra("baud", checked((int)Value(baud))); StartForegroundService(intent);
    }
    private static DeviceSession? CurrentSession() => DeviceExecutionService.Current?.Session ?? SimulationService.Current?.Session;
    private DeviceSession Session() => CurrentSession() ?? throw new InvalidOperationException("请连接 USB 设备或进入演示模式");
    private static DemoTransport Demo() => SimulationService.Current?.Transport ?? throw new InvalidOperationException("请先进入演示模式");
    private void InvalidateDraft()
    {
        draft.Invalidate();
        if (confirmed is not null) confirmed.Checked = false;
    }
    private async Task RestoreSaved(Guid id)
    {
        try { await LoadSaved(id); }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException or System.Text.Json.JsonException) { selected = null; InvalidateDraft(); }
    }
    private async Task LoadSaved(Guid id)
    {
        var job = await Task.Run(() => library.Read(id)); var limits = job.Limits ?? Limits();
        var result = await Task.Run(() => GcodeProgram.Prepare(job.Content, limits));
        areaWidth.Text = limits.Width.ToString(CultureInfo.InvariantCulture); areaHeight.Text = limits.Height.ToString(CultureInfo.InvariantCulture);
        maxFeed.Text = limits.MaxFeed.ToString(CultureInfo.InvariantCulture); maxPower.Text = limits.MaxPower.ToString(CultureInfo.InvariantCulture);
        source = Encoding.ASCII.GetBytes(job.Content); sourceName = Path.ChangeExtension(job.Name, ".nc"); quarterTurns = 0;
        if (IsDestroyed) return;
        selected = job; prepared = result; jobName = job.Name; imagePreparation = null; draft.Commit(draft.Revision);
        preview?.Dispose(); preview = new ToolpathPreview(result); artwork.Text = Description(); confirmed.Checked = false; canvas.Invalidate(); jobCanvas?.Invalidate();
    }
    private async Task StartDemo()
    {
        if (DeviceExecutionService.Current?.Session is not null) throw new InvalidOperationException("请先断开 USB 设备");
        if (SimulationService.Current is not null)
        {
            if (SessionMonitor.Active(CurrentSession()?.Snapshot.Execution?.State)) throw new InvalidOperationException("请先结束当前演示任务");
            SimulationService.Current.Disconnect();
            for (int i = 0; i < 100 && SimulationService.Current is not null; i++) await Task.Delay(30);
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted) RequestPermissions([Android.Manifest.Permission.PostNotifications], 100);
        using var intent = new Intent(this, typeof(SimulationService)); StartForegroundService(intent);
        for (int i = 0; i < 100 && SimulationService.Current?.Session?.Snapshot.Machine != "Idle"; i++) await Task.Delay(50);
        var session = Session(); await session.ReadConfiguration(); await Task.Delay(450); await session.Arm(true);
        areaWidth.Text = "40"; areaHeight.Text = "40"; maxFeed.Text = "1200"; maxPower.Text = "1000";
        var sample = new StringBuilder("G21 G90 G54 G17 G94\nM5\nG0 X5 Y5\nM4 S100\n");
        for (int i = 0; i < 96; i++) sample.Append(FormattableString.Invariant($"G1 X{(i % 2 == 0 ? 25 : 5)} Y{5 + i * .2:0.#} F600\n"));
        sample.Append("M5\nG0 X0 Y0\n"); source = Encoding.ASCII.GetBytes(sample.ToString()); sourceName = "GRBL-demo.nc"; InvalidateDraft();
        await Prepare(); ShowPage(1); artworkTabs(0);
    }
    private static double Value(EditText input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new FormatException("请输入有效数字（小数点使用 .）");
    private JobLimits Limits() { var l = new JobLimits(Value(areaWidth), Value(areaHeight), Value(maxFeed), Value(maxPower)); l.Validate(); return l; }
    private async Task Prepare()
    {
        if (source is null || converting) return;
        var data = source; var name = sourceName; var limits = Limits(); var revision = draft.Revision;
        bool image = IsArtwork(name);
        var rasterMode = generationMode switch
        {
            GenerationMode.Threshold => RasterMode.Threshold, GenerationMode.FloydSteinberg => RasterMode.FloydSteinberg,
            GenerationMode.Atkinson => RasterMode.Atkinson, GenerationMode.Burkes => RasterMode.Burkes,
            GenerationMode.Jarvis => RasterMode.Jarvis, GenerationMode.Stucki => RasterMode.Stucki,
            GenerationMode.Sierra2 => RasterMode.Sierra2, GenerationMode.Sierra3 => RasterMode.Sierra3,
            GenerationMode.SierraLite => RasterMode.SierraLite, _ => RasterMode.Grayscale
        };
        var settings = image ? new RasterSettings(Value(artWidth), Value(spacing), Value(feed), Value(power), Value(gamma), invert.Checked, true, rasterMode, Threshold: ThresholdValue()) : new RasterSettings(1, .1, 1, 1);
        bool vector = generationMode == GenerationMode.SvgOutline;
        var options = image ? ProcessingOptions() : null;
        var contour = image && generationMode == GenerationMode.ExperimentalContours ? new ContourSettings(Value(contourSensitivity), Value(contourMinimum), AdaptiveThreshold: automaticThreshold.Checked, Profile: contourProfile) : null;
        var edits = image ? new ImageEdits(Value(cropLeft), Value(cropTop), Value(cropRight), Value(cropBottom), quarterTurns) : new ImageEdits();
        converting = true; conversion?.Dispose(); conversion = new CancellationTokenSource();
        try
        {
            artwork.Text = "正在准备任务…";
            var converted = await Task.Run(() => image ? ArtworkImporter.ConvertDetailed(data, name, settings, limits, vector, conversion.Token, edits, options, contour) : null, conversion.Token);
            var result = converted?.Analysis ?? await Task.Run(() => GcodeProgram.Prepare(Encoding.ASCII.GetString(data), limits), conversion.Token);
            conversion.Token.ThrowIfCancellationRequested();
            if (revision != draft.Revision) throw new InvalidOperationException("输入已更新，请重新生成任务");
            string parameters = image ? $"宽 {settings.WidthMillimeters:0.##} mm · 行距 {settings.LineSpacing:0.###} mm · F{settings.Feed:0.##} · S{settings.Power:0.##} · Gamma {settings.Gamma:0.##} · {AlgorithmNames[(int)generationMode]} · 反色 {settings.Invert} · 阈值 {settings.Threshold} · 旋转 {quarterTurns * 90}° · 裁剪 左{edits.Left:0.###}/上{edits.Top:0.###}/右{edits.Right:0.###}/下{edits.Bottom:0.###}" : "G-code 指令参数以保存文件为准";
            if (converted is not null) parameters += FormattableString.Invariant($" · 实际 {converted.Image.WidthMillimeters:0.###}×{converted.Image.HeightMillimeters:0.###} mm · {converted.Image.WidthPixels}×{converted.Image.HeightPixels} px · 上限 {options!.MaxWidthMillimeters:0.##}×{options.MaxHeightMillimeters:0.##} mm / {options.MaxWidthPixels}×{options.MaxHeightPixels} px");
            var job = await Task.Run(() => library.Commit(name, result.Plan, limits, parameters));
            if (conversion.IsCancellationRequested || IsDestroyed) { library.Delete(job.Id); return; }
            if (!draft.Commit(revision)) { library.Delete(job.Id); throw new InvalidOperationException("输入已更新，请重新生成任务"); }
            prepared = result; selected = job; jobName = name; imagePreparation = converted?.Image; ClearNotice(); preview?.Dispose(); preview = new ToolpathPreview(result); confirmed.Checked = false; artwork.Text = Description(); canvas.Invalidate(); jobCanvas?.Invalidate();
        }
        catch (System.OperationCanceledException) { if (!IsDestroyed) artwork.Text = "已取消生成，可调整参数后重试"; }
        catch { if (!IsDestroyed) artwork.Text = "生成失败，请调整参数后重试"; throw; }
        finally { converting = false; }
    }
    private ImageProcessingOptions ProcessingOptions()
    {
        double w = Value(pixelMaxWidth), h = Value(pixelMaxHeight);
        if (w != Math.Truncate(w) || h != Math.Truncate(h) || w is < 16 or > 4096 || h is < 16 or > 4096) throw new ArgumentException("最大处理像素需为 16–4096 的整数");
        var value = new ImageProcessingOptions((int)w, (int)h, Value(imageMaxWidth), Value(imageMaxHeight)); value.Validate(); return value;
    }
    private static bool IsArtwork(string name) => ImportFormats.ForName(name).Kind is ImportKind.Bitmap or ImportKind.Svg;
    private string Description() => prepared is null ? "尚未生成" : $"{jobName}\n{prepared.Plan.Lines.Count} 行 · {(imagePreparation?.WidthMillimeters ?? prepared.MaxX):0.##} × {(imagePreparation?.HeightMillimeters ?? prepared.MaxY):0.##} mm · {TimeSpan.FromSeconds(prepared.EstimatedSeconds):hh\\:mm\\:ss}" + (imagePreparation is null ? "" : (imagePreparation.WidthPixels > 0 ? $"\n处理 {imagePreparation.WidthPixels}×{imagePreparation.HeightPixels} px · {AlgorithmNames[(int)generationMode]}" : "\nSVG 矢量路径"));
    private async Task StartSelected(bool frame)
    {
        if (selected is null || !draft.IsCurrent) throw new InvalidOperationException("请按当前参数重新生成任务");
        var limits = Limits(); var selectedId = selected.Id; var revision = draft.Revision;
        var job = await Task.Run(() => library.Read(selectedId));
        var session = Session(); var snapshot = session.Snapshot;
        var origin = snapshot.WorkX is { } x && snapshot.WorkY is { } y ? new WorkPosition(x, y) : throw new InvalidOperationException("请先读取设备工作坐标");
        var analysis = await Task.Run(() => GcodeProgram.Prepare(job.Content, limits, origin));
        if (frame) { var framed = RasterCam.Frame(analysis, Math.Min(300, limits.MaxFeed), limits); job = await Task.Run(() => library.Commit("范围预跑", framed.Plan, limits, "M5 无光边界预跑")); }
        var epoch = snapshot.ControllerEpoch;
        if (!await Ask(frame ? "范围预跑" : "开始加工", $"{(job.Name.Length > 36 ? job.Name[..36] + "…" : job.Name)}\n{analysis.Plan.Lines.Count} 行 · 预计 {TimeSpan.FromSeconds(analysis.EstimatedSeconds):hh\\:mm\\:ss}\n起点 WPos {origin.X:0.###}, {origin.Y:0.###}\n范围 X {analysis.MinX:0.##}–{analysis.MaxX:0.##} / Y {analysis.MinY:0.##}–{analysis.MaxY:0.##} mm\n工作区 {limits.Width:0.##} × {limits.Height:0.##} mm · S 上限 {limits.MaxPower:0.##}\n校验 {job.Hash[..12]}\n确认开始？")) return;
        if (revision != draft.Revision || !draft.IsCurrent) throw new InvalidOperationException("输入已变化，请重新生成并确认任务");
        await session.Start(job, limits, Guid.NewGuid(), epoch, confirmed.Checked, origin); confirmed.Checked = false;
    }
    private void DrawPreview(SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas;
        bool dark = ((int)Resources!.Configuration!.UiMode & 0x30) == 0x20;
        c.Clear(dark ? new SKColor(21, 26, 34) : new SKColor(245, 247, 250));
        if (prepared is null || e.Info.Width < 42 || e.Info.Height < 42) return;
        float w = (float)Math.Max(1, prepared.MaxX - prepared.MinX), h = (float)Math.Max(1, prepared.MaxY - prepared.MinY);
        float scale = Math.Min((e.Info.Width - 40) / w, (e.Info.Height - 40) / h) * zoom;
        c.Translate(20 + panX, e.Info.Height - 20 + panY); c.Scale(scale, -scale); c.Translate((float)-prepared.MinX, (float)-prepared.MinY);
        var viewport = new SKRect((float)prepared.MinX + (-20 - panX) / scale, (float)prepared.MinY + (-20 + panY) / scale,
            (float)prepared.MinX + (e.Info.Width - 20 - panX) / scale, (float)prepared.MinY + (e.Info.Height - 20 + panY) / scale);
        preview?.Draw(c, viewport, scale, showTravel.Checked);
    }
    protected override void OnResume() { base.OnResume(); visible = true; Refresh(); }
    protected override void OnPause()
    {
        visible = false; handler.RemoveCallbacksAndMessages(null);
        using var edit = preferences.Edit(); foreach (var input in savedInputs) edit!.PutString(input.Tag?.ToString() ?? "", input.Text); edit!.PutInt("generationMode", (int)generationMode); edit.PutInt("contourProfile", (int)contourProfile); edit.PutBoolean("showTravel", showTravel.Checked); edit.PutBoolean("automaticThreshold", automaticThreshold.Checked); edit.PutBoolean("invert", invert.Checked); edit.PutString("selectedJob", draft.IsCurrent ? selected?.Id.ToString() ?? "" : ""); edit.Apply(); base.OnPause();
    }
    protected override void OnDestroy() { ClosePanel(); conversion?.Cancel(); preview?.Dispose(); foreach (var icon in navigationIcons) icon?.Dispose(); base.OnDestroy(); }
    private static void SetLabel(TextView view, string text) { if (view.Text != text) view.Text = text; }
    private void Refresh()
    {
        if (!visible) return;
        var snapshot = CurrentSession()?.Snapshot; RefreshStatus();
        var execution = snapshot?.Execution; bool active = SessionMonitor.Active(execution?.State);
        bool ready = selected is not null && draft.IsCurrent && !converting && !active && snapshot is { Connection: "Connected", Machine: "Idle", IsArmed: true };
        startButton.Enabled = ready && confirmed.Checked; frameButton.Enabled = ready && confirmed.Checked;
        if (snapshot?.IsArmed != true) confirmed.Checked = false;
        confirmed.Enabled = ready;
        pauseButton.Enabled = execution?.State is ExecutionState.Running or ExecutionState.Draining;
        resumeButton.Enabled = execution?.State == ExecutionState.Paused;
        cancelButton.Enabled = active && execution?.State != ExecutionState.Cancelling;
        jobProgress.Progress = execution is { TotalLines: > 0 } ? execution.AcknowledgedLines * 1000 / execution.TotalLines : 0;
        SetLabel(jobSummary, selected is null ? "先导入作品并生成任务" : draft.IsCurrent ? Description() : "输入已变化，请重新生成任务");
        SetLabel(executionText, execution is null ? "等待开始" : $"{StateName(execution.State)} · 确认 {execution.AcknowledgedLines}/{execution.TotalLines} 行" + (execution.Error is null ? "" : "\n" + execution.Error));
        if (snapshot is not null) SetLabel(details, $"{snapshot.Firmware}\n{snapshot.Position}\n$30={snapshot.MaxPower?.ToString() ?? "未知"} · {(snapshot.LaserMode ? "激光模式" : "普通模式")}");
        SetLabel(positionText, snapshot?.Position ?? "连接设备后显示工作坐标");
        generateButton.Enabled = source is not null && !importing;
        generateButton.Text = converting ? "取消生成" : "生成任务"; generateButton.ContentDescription = generateButton.Text;
        if (previewHint is not null) previewHint.Visibility = prepared is null ? ViewStates.Visible : ViewStates.Gone;
        handler.PostDelayed(Refresh, 250);
    }
}
