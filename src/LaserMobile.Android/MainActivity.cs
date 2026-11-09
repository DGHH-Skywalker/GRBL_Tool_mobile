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

[Activity(Label = "GRBL Tool Mobile", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private bool visible, converting;
    private TextView status = null!, details = null!, artwork = null!, history = null!;
    private EditText baud = null!, areaWidth = null!, areaHeight = null!, maxFeed = null!, maxPower = null!, artWidth = null!, spacing = null!, feed = null!, power = null!, gamma = null!, jogStep = null!, cropLeft = null!, cropTop = null!, cropRight = null!, cropBottom = null!;
    private int quarterTurns;
    private CheckBox invert = null!, dither = null!, threshold = null!, outline = null!, confirmed = null!;
    private Spinner ports = null!;
    private readonly List<(UsbSerialPort Port, string Label)> devices = [];
    private readonly List<LinearLayout> pages = [];
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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        preferences = GetSharedPreferences("settings", FileCreationMode.Private)!;
        library = new JobLibrary(Path.Combine(FilesDir!.AbsolutePath, "jobs"));
        executions = new ExecutionStore(Path.Combine(FilesDir.AbsolutePath, "executions.sqlite"));
        if (DeviceExecutionService.Current is null) executions.RecoverInterrupted();
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var density = Resources!.DisplayMetrics!.Density; root.SetPadding((int)(16 * density), (int)(48 * density), (int)(16 * density), (int)(28 * density)); SetContentView(root);
        status = Label(root, "GRBL Tool Mobile · 连接设备或导入作品");
        var nav = Row(root);
        string[] titles = ["设备", "作品", "加工", "历史"];
        for (int i = 0; i < titles.Length; i++) { int index = i; Button(nav, titles[i], () => { ShowPage(index); return Task.CompletedTask; }); }
        for (int i = 0; i < 4; i++)
        {
            var scroll = new ScrollView(this); var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
            page.SetPadding(8, 8, 8, 8); scroll.AddView(page); root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1)); pages.Add(page);
        }
        BuildDevice(); BuildArtwork(); BuildJob(); BuildHistory();
        if (Guid.TryParse(preferences.GetString("selectedJob", ""), out var savedId))
        {
            try { selected = library.Read(savedId); prepared = GcodeProgram.Prepare(selected.Content, Limits()); jobName = selected.Name; artwork.Text = Description(); }
            catch (Exception e) when (e is IOException or FormatException or ArgumentException or System.Text.Json.JsonException) { selected = null; }
        }
        ShowPage(0);
    }
    private TextView Label(LinearLayout parent, string text)
    { var v = new TextView(this) { Text = text, TextSize = 15 }; v.SetPadding(0, 8, 0, 8); parent.AddView(v); return v; }
    private LinearLayout Row(LinearLayout parent)
    { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; parent.AddView(row); return row; }
    private EditText Number(LinearLayout page, string title, string value, string key)
    {
        Label(page, title); var input = new EditText(this) { Text = preferences.GetString(key, value), Tag = key, InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal };
        input.TextChanged += (_, _) => { if (confirmed is not null) confirmed.Checked = false; };
        page.AddView(input); savedInputs.Add(input); return input;
    }
    private CheckBox Check(LinearLayout page, string title, bool value = false)
    { var box = new CheckBox(this) { Text = title, Checked = value }; page.AddView(box); return box; }
    private Button Button(LinearLayout parent, string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text };
        parent.AddView(button, new LinearLayout.LayoutParams(parent.Orientation == Orientation.Horizontal ? 0 : -1, -2, parent.Orientation == Orientation.Horizontal ? 1 : 0));
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); }
            catch (Exception e) { ShowError(e.Message); }
            finally { if (!IsDestroyed) button.Enabled = true; }
        };
        return button;
    }
    private void ShowPage(int index)
    {
        for (int i = 0; i < pages.Count; i++) ((View)pages[i].Parent!).Visibility = i == index ? ViewStates.Visible : ViewStates.Gone;
        if (index == 3) RefreshHistory();
    }
    private void BuildDevice()
    {
        var p = pages[0]; Label(p, "设备配置：自定义工作区域。星光板号和固件由实机资料确认。");
        baud = Number(p, "波特率", "115200", "baud");
        ports = new Spinner(this); p.AddView(ports);
        Button(p, "扫描 USB 设备与端口", Scan);
        Button(p, "授权并连接", Connect);
        Button(p, "导入设备配置 JSON", () =>
        {
            using var intent = new Intent(Intent.ActionOpenDocument); intent.SetType("*/*"); intent.AddCategory(Intent.CategoryOpenable);
#pragma warning disable CA1422
            StartActivityForResult(intent, 23);
#pragma warning restore CA1422
            return Task.CompletedTask;
        });
        Button(p, "读取固件与配置", () => Session().ReadConfiguration());
        Button(p, "断开连接", async () => { if (DeviceExecutionService.Current is { } s && await Ask("断开设备", "活动任务将中断，程序会先发送保持请求。确认断开？")) await s.Disconnect(); });
        details = Label(p, "设备尚未连接");
        areaWidth = Number(p, "工作区域宽度 mm", "40", "areaWidth");
        areaHeight = Number(p, "工作区域高度 mm", "40", "areaHeight");
        maxFeed = Number(p, "进给上限 mm/min", "1200", "maxFeed");
        maxPower = Number(p, "功率上限（应与设备 $30 匹配）", "1000", "maxPower");
        jogStep = Number(p, "点动步长 mm（0.1–10）", "1", "jogStep");
        var row = Row(p);
        Button(row, "X−", () => Session().Jog(-1, 0, Value(jogStep), 300)); Button(row, "X+", () => Session().Jog(1, 0, Value(jogStep), 300));
        row = Row(p);
        Button(row, "Y−", () => Session().Jog(0, -1, Value(jogStep), 300)); Button(row, "Y+", () => Session().Jog(0, 1, Value(jogStep), 300));
        Button(p, "硬件回零", async () => { if (await Ask("回零", "确认已安装回零开关、方向正确且设备 $22=1？")) await Session().Home(true); });
        Button(p, "报警解锁", async () => { if (await Ask("解锁", "确认已处理报警原因并检查位置？")) await Session().Unlock(true); });
        Button(p, "将当前位置设为工作零点", async () => { if (await Ask("设置原点", "确认当前位置作为 G54 工件 X/Y 零点？")) await Session().SetOrigin(); });
        Button(p, "查看协议日志", () => { ShowMessage("最近协议记录", string.Join('\n', Session().Log)); return Task.CompletedTask; });
        Button(p, "运行无光模拟任务", () => { StartForegroundService(new Intent(this, typeof(SimulationService))); return Task.CompletedTask; });
        Button(p, "扩展与许可证", () =>
        {
            var extension = new LaserMobile.Extensions.Xingguang.XingguangExtension();
            ShowMessage("GRBL Tool Mobile 0.2.0", $"通用 GRBL 1.1 控制\n{extension.Profiles[0].DisplayName}\n\nGPL-3.0-or-later\nUSB: anotherlab MIT\nSkiaSharp / Svg.Skia MIT\nClipper2 Boost-1.0\n抖动：Cyotek MIT\n源码：github.com/DGHH-Skywalker/GRBL_Tool_mobile"); return Task.CompletedTask;
        });
    }
    private void BuildArtwork()
    {
        var p = pages[1];
        Button(p, "导入 G-code / PNG / JPG / SVG", () =>
        {
            using var intent = new Intent(Intent.ActionOpenDocument); intent.SetType("*/*"); intent.AddCategory(Intent.CategoryOpenable);
#pragma warning disable CA1422
            StartActivityForResult(intent, 20);
#pragma warning restore CA1422
            return Task.CompletedTask;
        });
        artwork = Label(p, "尚未导入作品");
        artWidth = Number(p, "图像/矢量加工宽度 mm", "30", "artWidth");
        spacing = Number(p, "光栅行距 mm", "0.15", "spacing");
        feed = Number(p, "加工进给 mm/min", "600", "feed");
        power = Number(p, "加工最大功率 S", "100", "power");
        gamma = Number(p, "Gamma", "1", "gamma");
        invert = Check(p, "反色"); threshold = Check(p, "黑白阈值"); dither = Check(p, "Floyd–Steinberg 抖动"); outline = Check(p, "SVG 轮廓加工（展开后的基础图形与路径）");
        Label(p, "位图裁剪：比例坐标 0–1；旋转顺时针 90°");
        cropLeft = Number(p, "裁剪左", "0", "cropLeft"); cropTop = Number(p, "裁剪上", "0", "cropTop");
        cropRight = Number(p, "裁剪右", "1", "cropRight"); cropBottom = Number(p, "裁剪下", "1", "cropBottom");
        Button(p, "旋转 90°", () => { quarterTurns = (quarterTurns + 1) % 4; artwork.Text = $"位图旋转 {quarterTurns * 90}°，重新生成后应用"; confirmed.Checked = false; return Task.CompletedTask; });
        Button(p, "生成并保存任务", Prepare);
        Button(p, "取消转换", () => { conversion?.Cancel(); return Task.CompletedTask; });
        Button(p, "适合屏幕", () => { zoom = 1; panX = panY = 0; canvas.Invalidate(); return Task.CompletedTask; });
        canvas = new SKCanvasView(this);
        p.AddView(canvas, new LinearLayout.LayoutParams(-1, 600));
        canvas.PaintSurface += (_, e) => DrawPreview(e);
        canvas.Touch += (_, e) =>
        {
            var ev = e.Event!;
            if (ev.ActionMasked == MotionEventActions.Down) { lastX = ev.GetX(); lastY = ev.GetY(); lastDistance = 0; }
            if (ev.ActionMasked == MotionEventActions.Move)
            {
                if (ev.PointerCount == 2)
                {
                    float d = MathF.Sqrt(MathF.Pow(ev.GetX(0) - ev.GetX(1), 2) + MathF.Pow(ev.GetY(0) - ev.GetY(1), 2));
                    if (lastDistance > 0) zoom = Math.Clamp(zoom * d / lastDistance, 0.2f, 20); lastDistance = d;
                }
                else { panX += ev.GetX() - lastX; panY += ev.GetY() - lastY; lastX = ev.GetX(); lastY = ev.GetY(); }
                canvas.Invalidate();
            }
            if (ev.ActionMasked == MotionEventActions.Up) lastDistance = 0;
            canvas.Parent?.RequestDisallowInterceptTouchEvent(true); e.Handled = true;
        };
    }
    private void BuildJob()
    {
        var p = pages[2]; Label(p, "确认项目、工作零点、工作区域、功率及现场条件后启动。");
        confirmed = Check(p, "已检查现场、坐标、材料、参数与设备为标准 GRBL 1.1；确认使用其同步屏障和复位取消策略");
        Button(p, "开始加工", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请完成加工确认"); await StartSelected(false); });
        Button(p, "无光范围预跑", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请先确认范围与现场"); await StartSelected(true); });
        Button(p, "暂停", () => { var s = Session(); return s.Pause(s.Snapshot.Execution?.ExecutionId ?? Guid.Empty); });
        Button(p, "继续", async () =>
        {
            var s = Session(); var id = s.Snapshot.Execution?.ExecutionId ?? Guid.Empty;
            if (await Ask("继续加工", "确认当前现场状态允许继续输出？")) await s.Resume(id, true);
        });
        Button(p, "取消并复位当前加工", async () =>
        {
            var s = Session(); var id = s.Snapshot.Execution?.ExecutionId ?? Guid.Empty;
            if (await Ask("结束当前执行", "确认发送实时保持和软件复位？完成后需重新检查工作位置。")) await s.Cancel(id);
        });
    }
    private void BuildHistory()
    {
        var p = pages[3]; Button(p, "刷新历史与准备任务", () => { RefreshHistory(); return Task.CompletedTask; });
        Button(p, "选择已保存任务", async () =>
        {
            var jobs = library.List().ToArray(); if (jobs.Length == 0) return;
            var tcs = new TaskCompletionSource<int>();
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("准备任务");
            builder.SetItems(jobs.Select(j => j.Name + " · " + j.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm")).ToArray(), (_, e) => tcs.TrySetResult(e.Which));
            builder.SetNegativeButton("取消", (_, _) => tcs.TrySetResult(-1));
            var dialog = builder.Create() ?? throw new InvalidOperationException("无法创建对话框");
            dialog.SetOnCancelListener(new DialogCancel(() => tcs.TrySetResult(-1))); dialog.Show();
            int index = await tcs.Task; if (index < 0) return;
            var job = library.Read(jobs[index].Id); prepared = GcodeProgram.Prepare(job.Content, Limits()); selected = job; jobName = job.Name;
            artwork.Text = Description(); confirmed.Checked = false; canvas.Invalidate(); ShowPage(1);
        });
        Button(p, "删除已选任务", async () =>
        {
            if (selected is null) throw new InvalidOperationException("请先选择任务");
            if (DeviceExecutionService.Current?.Session?.Snapshot.Execution?.State is ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Draining or ExecutionState.Cancelling)
                throw new InvalidOperationException("活动加工期间请保留任务文件");
            if (await Ask("删除任务", selected.Name))
            {
                library.Delete(selected.Id); selected = null; prepared = null; artwork.Text = "尚未选择任务"; confirmed.Checked = false; canvas.Invalidate(); RefreshHistory();
            }
        });
        Button(p, "导出任务 G-code", () =>
        {
            if (selected is null) throw new InvalidOperationException("请先选择任务");
            using var intent = new Intent(Intent.ActionCreateDocument); intent.SetType("text/plain"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraTitle, selected.Name + ".nc");
#pragma warning disable CA1422
            StartActivityForResult(intent, 21);
#pragma warning restore CA1422
            return Task.CompletedTask;
        });
        Button(p, "导出诊断日志", () =>
        {
            using var intent = new Intent(Intent.ActionCreateDocument); intent.SetType("text/plain"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraTitle, "grbl-diagnostics.txt");
#pragma warning disable CA1422
            StartActivityForResult(intent, 22);
#pragma warning restore CA1422
            return Task.CompletedTask;
        });
        history = Label(p, "");
    }
    private async Task Scan()
    {
        var manager = (UsbManager)GetSystemService(UsbService)!;
        var drivers = await Task.Run(() => new UsbSerialProber(UsbSerialProber.DefaultProbeTable).FindAllDrivers(manager));
        devices.Clear(); foreach (var d in drivers) foreach (var port in d.Ports) devices.Add((port, $"{d.Device.VendorId:X4}:{d.Device.ProductId:X4} 端口 {port.PortNumber}"));
        ports.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, devices.Select(d => d.Label).DefaultIfEmpty("未发现 USB 串口").ToArray());
    }
    private async Task Connect()
    {
        if (ports.SelectedItemPosition < 0 || ports.SelectedItemPosition >= devices.Count) throw new InvalidOperationException("请扫描并选择设备");
        var port = devices[ports.SelectedItemPosition].Port; var manager = (UsbManager)GetSystemService(UsbService)!;
        if (!await UsbPermissionReceiver.Request(this, manager, port.Driver.Device)) throw new UnauthorizedAccessException("USB 授权被拒绝");
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted) RequestPermissions([Android.Manifest.Permission.PostNotifications], 100);
        using var intent = new Intent(this, typeof(DeviceExecutionService)); intent.PutExtra("device", port.Driver.Device.DeviceId); intent.PutExtra("port", port.PortNumber); intent.PutExtra("baud", checked((int)Value(baud))); StartForegroundService(intent);
    }
    private DeviceSession Session() => DeviceExecutionService.Current?.Session ?? throw new InvalidOperationException("请先连接设备");
    private static double Value(EditText input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new FormatException("请输入有效数字（小数点使用 .）");
    private JobLimits Limits() { var l = new JobLimits(Value(areaWidth), Value(areaHeight), Value(maxFeed), Value(maxPower)); l.Validate(); return l; }
    private async Task Prepare()
    {
        if (source is null || converting) return;
        var data = source; var name = sourceName; var limits = Limits();
        var settings = new RasterSettings(Value(artWidth), Value(spacing), Value(feed), Value(power), Value(gamma), invert.Checked, true, dither.Checked ? RasterMode.FloydSteinberg : threshold.Checked ? RasterMode.Threshold : RasterMode.Grayscale);
        bool vector = outline.Checked;
        var edits = new ImageEdits(Value(cropLeft), Value(cropTop), Value(cropRight), Value(cropBottom), quarterTurns);
        converting = true; conversion?.Dispose(); conversion = new CancellationTokenSource();
        try
        {
            artwork.Text = "正在准备任务…";
            var result = await Task.Run(() => IsArtwork(name) ? ArtworkImporter.Convert(data, name, settings, limits, vector, conversion.Token, edits) : GcodeProgram.Prepare(Encoding.ASCII.GetString(data), limits), conversion.Token);
            conversion.Token.ThrowIfCancellationRequested();
            var job = await Task.Run(() => library.Commit(name, result.Plan));
            prepared = result; selected = job; jobName = name; confirmed.Checked = false; artwork.Text = Description(); canvas.Invalidate();
        }
        finally { converting = false; }
    }
    private static bool IsArtwork(string name) => new[] { ".png", ".jpg", ".jpeg", ".svg", ".webp", ".bmp" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    private string Description() => prepared is null ? "尚未生成" : $"{jobName}\n{prepared.Plan.Lines.Count} 行 · {prepared.MaxX:0.##} × {prepared.MaxY:0.##} mm\n预计 {TimeSpan.FromSeconds(prepared.EstimatedSeconds):hh\\:mm\\:ss} · SHA256 {prepared.Plan.Sha256[..12]}\n{(prepared.EmitsLaser ? "含激光输出" : "无光运动")}";
    private async Task StartSelected(bool frame)
    {
        if (selected is null) throw new InvalidOperationException("请先准备任务");
        var limits = Limits(); var job = library.Read(selected.Id); var analysis = GcodeProgram.Prepare(job.Content, limits);
        if (frame) { var framed = RasterCam.Frame(analysis, Math.Min(300, limits.MaxFeed), limits); job = library.Commit("范围预跑", framed.Plan); }
        var session = Session(); var epoch = session.Snapshot.ControllerEpoch;
        if (!await Ask(frame ? "范围预跑" : "开始加工", $"{job.Name}\n确认区域、工作原点与现场条件？")) return;
        await session.Start(job, limits, Guid.NewGuid(), epoch, confirmed.Checked); confirmed.Checked = false;
    }
    private void DrawPreview(SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas; c.Clear(new SKColor(245, 247, 250));
        if (prepared is null) return;
        float w = (float)Math.Max(1, prepared.MaxX - prepared.MinX), h = (float)Math.Max(1, prepared.MaxY - prepared.MinY);
        float scale = Math.Min((e.Info.Width - 40) / w, (e.Info.Height - 40) / h) * zoom;
        c.Translate(20 + panX, e.Info.Height - 20 + panY); c.Scale(scale, -scale); c.Translate((float)-prepared.MinX, (float)-prepared.MinY);
        using var travel = new SKPaint { Color = new SKColor(150, 160, 170), StrokeWidth = 1 / scale, IsAntialias = true };
        using var burn = new SKPaint { Color = new SKColor(20, 90, 180), StrokeWidth = 1.5f / scale, IsAntialias = true };
        foreach (var s in prepared.Segments) c.DrawLine((float)s.X1, (float)s.Y1, (float)s.X2, (float)s.Y2, s.Power > 0 ? burn : travel);
    }
    private void RefreshHistory()
    { history.Text = "准备任务\n" + string.Join('\n', library.List().Select(j => $"{j.Name} · {j.CreatedAt.ToLocalTime():MM-dd HH:mm}")) + "\n\n执行记录\n" + string.Join('\n', executions.History().Select(x => $"{x.Id.ToString()[..8]} · {x.State} {x.Error}")); }
#pragma warning disable CS0672, CA1422
    protected override async void OnActivityResult(int requestCode, Android.App.Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Android.App.Result.Ok || data?.Data is null) return;
        try
        {
            if (requestCode is 20 or 23)
            {
                using var stream = ContentResolver!.OpenInputStream(data.Data) ?? throw new IOException("无法读取文件");
                using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer)) > 0) { if (output.Length + count > 16_000_000) throw new IOException("文件超过 16 MB"); await output.WriteAsync(buffer.AsMemory(0, count)); }
                if (requestCode == 23)
                {
                    var preset = DevicePreset.Parse(Encoding.UTF8.GetString(output.ToArray()));
                    areaWidth.Text = preset.Width.ToString(CultureInfo.InvariantCulture); areaHeight.Text = preset.Height.ToString(CultureInfo.InvariantCulture);
                    maxFeed.Text = preset.MaxFeed.ToString(CultureInfo.InvariantCulture); maxPower.Text = preset.MaxPower.ToString(CultureInfo.InvariantCulture);
                    confirmed.Checked = false; ShowMessage("设备配置已导入", preset.DisplayName + "\n请连接设备读取固件与参数核对"); return;
                }
                using var cursor = ContentResolver.Query(data.Data, null, null, null, null);
                sourceName = "import.nc";
                if (cursor?.MoveToFirst() == true) { int column = cursor.GetColumnIndex("_display_name"); if (column >= 0) sourceName = cursor.GetString(column) ?? sourceName; }
                source = output.ToArray(); selected = null; prepared = null; confirmed.Checked = false; artwork.Text = $"已导入 {sourceName} · {source.Length} 字节"; canvas.Invalidate(); ShowPage(1);
            }
            else if (requestCode is 21 or 22)
            {
                using var stream = ContentResolver!.OpenOutputStream(data.Data) ?? throw new IOException("无法写入文件");
                string content = requestCode == 21 ? library.Read(selected?.Id ?? throw new InvalidOperationException("任务未选择")).Content : "GRBL Tool Mobile 0.2.0\n" + (DeviceExecutionService.Current?.Session?.Snapshot.ToString() ?? "Disconnected") + "\n" + string.Join('\n', DeviceExecutionService.Current?.Session?.Log ?? []);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
            }
        }
        catch (Exception e) { ShowError(e.Message); }
    }
#pragma warning restore CS0672, CA1422
    private Task<bool> Ask(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var builder = new AlertDialog.Builder(this); builder.SetTitle(title); builder.SetMessage(message);
        builder.SetPositiveButton("确认", (_, _) => tcs.TrySetResult(true)); builder.SetNegativeButton("取消", (_, _) => tcs.TrySetResult(false));
        var dialog = builder.Create() ?? throw new InvalidOperationException("无法创建对话框");
        dialog.SetOnCancelListener(new DialogCancel(() => tcs.TrySetResult(false))); dialog.Show(); return tcs.Task;
    }
    private sealed class DialogCancel(Action action) : Java.Lang.Object, IDialogInterfaceOnCancelListener { public void OnCancel(IDialogInterface? dialog) => action(); }
    private void ShowMessage(string title, string message)
    {
        if (IsDestroyed) return;
        var builder = new AlertDialog.Builder(this); builder.SetTitle(title); builder.SetMessage(message);
        builder.SetPositiveButton("关闭", (_, _) => { }); builder.Show();
    }
    private void ShowError(string message) => ShowMessage("操作提示", message);
    protected override void OnResume() { base.OnResume(); visible = true; Refresh(); }
    protected override void OnPause()
    {
        visible = false; handler.RemoveCallbacksAndMessages(null);
        using var edit = preferences.Edit(); foreach (var input in savedInputs) edit!.PutString(input.Tag?.ToString() ?? "", input.Text); edit!.PutString("selectedJob", selected?.Id.ToString() ?? ""); edit.Apply(); base.OnPause();
    }
    protected override void OnDestroy() { conversion?.Cancel(); conversion?.Dispose(); base.OnDestroy(); }
    private void Refresh()
    {
        if (!visible) return;
        var snapshot = DeviceExecutionService.Current?.Session?.Snapshot;
        status.Text = snapshot is null ? DeviceExecutionService.Current?.ConnectionError ?? "未连接 · " + SimulationService.Latest : $"{snapshot.Connection} · {snapshot.Machine} · {snapshot.Execution?.State.ToString() ?? "无任务"}";
        if (snapshot is not null) details.Text = $"{snapshot.Firmware}\n{snapshot.Position}\n$30={snapshot.MaxPower?.ToString() ?? "未知"} · 激光模式 {snapshot.LaserMode}\n确认行 {snapshot.Execution?.AcknowledgedLines}/{snapshot.Execution?.TotalLines}\n{snapshot.Error ?? snapshot.Execution?.Error}";
        handler.PostDelayed(Refresh, 250);
    }
}
