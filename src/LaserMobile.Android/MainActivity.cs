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
    private readonly PreparationRevision draft = new();
    private ToolpathPreview? preview;
    private TextView jobSummary = null!;
    private ProgressBar jobProgress = null!;
    private Button startButton = null!, frameButton = null!, pauseButton = null!, resumeButton = null!, cancelButton = null!;
    private TextView status = null!, details = null!, artwork = null!, history = null!;
    private EditText baud = null!, areaWidth = null!, areaHeight = null!, maxFeed = null!, maxPower = null!, artWidth = null!, spacing = null!, feed = null!, power = null!, gamma = null!, jogStep = null!, cropLeft = null!, cropTop = null!, cropRight = null!, cropBottom = null!;
    private int quarterTurns;
    private CheckBox invert = null!, dither = null!, threshold = null!, outline = null!, confirmed = null!;
    private Spinner ports = null!;
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
        background.AddState([], Surface(primary ? "#1762CC" : Dark ? "#253449" : "#EAF1FB", 8)); button.Background = background;
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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        preferences = GetSharedPreferences("settings", FileCreationMode.Private)!;
        library = new JobLibrary(Path.Combine(FilesDir!.AbsolutePath, "jobs"));
        executions = new ExecutionStore(Path.Combine(FilesDir.AbsolutePath, "executions.sqlite"));
        if (DeviceExecutionService.Current is null && SimulationService.Current is null) executions.RecoverInterrupted();
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var density = Resources!.DisplayMetrics!.Density; root.SetPadding((int)(16 * density), (int)(48 * density), (int)(16 * density), (int)(28 * density)); SetContentView(root);
        root.SetBackgroundColor(Android.Graphics.Color.ParseColor(Dark ? "#101722" : "#F3F6FB"));
        status = Label(root, "GRBL Tool Mobile · 连接设备或导入作品"); status.SetMinLines(2);
        status.Background = Surface(Dark ? "#1C2C43" : "#E7F0FF"); status.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        var nav = Row(root);
        string[] titles = ["设备", "作品", "加工", "历史"];
        for (int i = 0; i < titles.Length; i++) { int index = i; navigation.Add(Button(nav, titles[i], () => { ShowPage(index); return Task.CompletedTask; })); }
        for (int i = 0; i < 4; i++)
        {
            var scroll = new ScrollView(this); var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
            page.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12)); page.Background = Surface(Dark ? "#172130" : "#FFFFFF"); scroll.AddView(page); root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1)); pages.Add(page);
        }
        BuildDevice(); BuildArtwork(); BuildJob(); BuildHistory();
        if (Guid.TryParse(preferences.GetString("selectedJob", ""), out var savedId))
        {
            _ = RestoreSaved(savedId);
        }
        ShowPage(0);
    }
    private TextView Label(LinearLayout parent, string text)
    { var v = new TextView(this) { Text = text, TextSize = 15 }; v.SetTextColor(TextColor); v.SetPadding(0, Dp(5), 0, Dp(5)); parent.AddView(v); return v; }
    private LinearLayout Row(LinearLayout parent)
    { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; parent.AddView(row); return row; }
    private EditText Number(LinearLayout page, string title, string value, string key)
    {
        Label(page, title); var input = new EditText(this) { Text = preferences.GetString(key, value), Tag = key, ContentDescription = title, InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal };
        input.TextChanged += (_, _) => { if (key is not ("baud" or "jogStep")) InvalidateDraft(); };
        page.AddView(input); savedInputs.Add(input); return input;
    }
    private CheckBox Check(LinearLayout page, string title, bool value = false)
    { var box = new CheckBox(this) { Text = title, Checked = value }; box.CheckedChange += (_, _) => { if (!ReferenceEquals(box, confirmed)) InvalidateDraft(); }; page.AddView(box); return box; }
    private Button Button(LinearLayout parent, string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text, ContentDescription = text, TextSize = parent.Orientation == Orientation.Horizontal ? 12 : 14 };
        StyleButton(button, text is "体验完整演示" or "开始加工" or "生成并保存任务" or "授权并连接");
        var layout = new LinearLayout.LayoutParams(parent.Orientation == Orientation.Horizontal ? 0 : -1, -2, parent.Orientation == Orientation.Horizontal ? 1 : 0);
        layout.SetMargins(Dp(2), Dp(5), Dp(2), Dp(5)); parent.AddView(button, layout);
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
        for (int i = 0; i < pages.Count; i++) { ((View)pages[i].Parent!).Visibility = i == index ? ViewStates.Visible : ViewStates.Gone; StyleButton(navigation[i], i == index); }
        if (index == 3) RefreshHistory();
    }
    private void BuildDevice()
    {
        var p = pages[0];
        var title = Label(p, "GRBL Tool Mobile 0.3"); title.TextSize = 24;
        Label(p, "作品导入、路径预览、加工控制与任务记录。USB OTG 直接连接原控制板。");
        Button(p, "体验完整演示", StartDemo);
        Label(p, "演示模式使用内置 GRBL 设备，可体验完整加工流程与异常恢复。");
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
        Button(p, "软件复位控制器", async () => { if (await Ask("复位控制器", "确认清除控制器队列并重新读取配置？")) await Session().ResetController(true); });
        Button(p, "确认设备就绪", async () => { if (await Ask("设备就绪", "确认已核对设备配置、工作原点和工作区域？")) await Session().Arm(true); });
        Button(p, "断开连接", async () => { if (await Ask("断开设备", "确认结束设备会话？活动任务将记为中断。")) { if (DeviceExecutionService.Current is { } s) await s.Disconnect(); SimulationService.Current?.Disconnect(); } });
        details = Label(p, "设备尚未连接");
        var advanced = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        Button(p, "设备参数与维护", () => { advanced.Visibility = advanced.Visibility == ViewStates.Gone ? ViewStates.Visible : ViewStates.Gone; return Task.CompletedTask; });
        p.AddView(advanced); p = advanced;
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
        Button(p, "选择 G54 / G17 / G94 工作模式", () => Session().SelectStandardFrame());
        Button(p, "取消点动", () => Session().CancelJog());
        Button(p, "将当前位置设为工作零点", async () => { if (await Ask("设置原点", "确认当前位置作为 G54 工件 X/Y 零点？")) await Session().SetOrigin(); });
        Button(p, "查看协议日志", () => { ShowMessage("最近协议记录", string.Join('\n', Session().Log)); return Task.CompletedTask; });
        Label(p, "演示故障测试");
        Button(p, "演示：设备报警", () => { Demo().InjectAlarm(); return Task.CompletedTask; });
        Button(p, "演示：控制器复位", () => { Demo().InjectReset(); return Task.CompletedTask; });
        Button(p, "演示：连接中断", () => { Demo().InjectDisconnect(); return Task.CompletedTask; });
        Button(p, "扩展与许可证", () =>
        {
            var extension = new LaserMobile.Extensions.Xingguang.XingguangExtension();
            ShowMessage("GRBL Tool Mobile 0.3.0", $"通用 GRBL 1.1 控制\n{extension.Profiles[0].DisplayName}\n\nGPL-3.0-or-later\nUSB: anotherlab MIT\nSkiaSharp / Svg.Skia MIT\nClipper2 Boost-1.0\n抖动：Cyotek MIT\n源码：github.com/DGHH-Skywalker/GRBL_Tool_mobile"); return Task.CompletedTask;
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
        artwork = Label(p, "导入文件或从设备页体验完整演示");
        canvas = new SKCanvasView(this);
        p.AddView(canvas, new LinearLayout.LayoutParams(-1, (int)(260 * Resources!.DisplayMetrics!.Density)));
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
        Label(p, "位图 / SVG 参数；G-code 的 F/S 沿用文件内容。修改输入后请重新生成任务。");
        artWidth = Number(p, "图像/矢量加工宽度 mm", "30", "artWidth");
        spacing = Number(p, "光栅行距 mm", "0.15", "spacing");
        feed = Number(p, "加工进给 mm/min", "600", "feed");
        power = Number(p, "加工最大功率 S", "100", "power");
        gamma = Number(p, "Gamma", "1", "gamma");
        invert = Check(p, "反色"); threshold = Check(p, "黑白阈值"); dither = Check(p, "Floyd–Steinberg 抖动"); outline = Check(p, "SVG 轮廓加工（展开后的基础图形与路径）");
        Label(p, "位图裁剪：比例坐标 0–1；旋转顺时针 90°");
        cropLeft = Number(p, "裁剪左", "0", "cropLeft"); cropTop = Number(p, "裁剪上", "0", "cropTop");
        cropRight = Number(p, "裁剪右", "1", "cropRight"); cropBottom = Number(p, "裁剪下", "1", "cropBottom");
        Button(p, "旋转 90°", () => { quarterTurns = (quarterTurns + 1) % 4; artwork.Text = $"位图旋转 {quarterTurns * 90}°，重新生成后应用"; InvalidateDraft(); return Task.CompletedTask; });
        Button(p, "生成并保存任务", Prepare);
        Button(p, "取消转换", () => { conversion?.Cancel(); return Task.CompletedTask; });
        Button(p, "适合屏幕", () => { zoom = 1; panX = panY = 0; canvas.Invalidate(); return Task.CompletedTask; });

    }
    private void BuildJob()
    {
        var p = pages[2]; Label(p, "确认项目、工作零点、工作区域、功率及现场条件后启动。");
        jobSummary = Label(p, "请在作品页准备任务");
        jobProgress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000 }; p.AddView(jobProgress);
        confirmed = Check(p, "已检查现场，并核对工作原点与加工参数");
        startButton = Button(p, "开始加工", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请完成加工确认"); await StartSelected(false); });
        frameButton = Button(p, "无光范围预跑", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请先确认范围与现场"); await StartSelected(true); });
        var controls = Row(p);
        pauseButton = Button(controls, "暂停", () => { var s = Session(); return s.Pause(s.Snapshot.Execution?.ExecutionId ?? Guid.Empty); });
        resumeButton = Button(controls, "继续", async () =>
        {
            var s = Session(); var id = s.Snapshot.Execution?.ExecutionId ?? Guid.Empty;
            if (await Ask("继续加工", "确认当前现场状态允许继续输出？")) await s.Resume(id, true);
        });
        cancelButton = Button(p, "取消并复位当前加工", async () =>
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
            await LoadSaved(jobs[index].Id); ShowPage(1);
        });
        Button(p, "删除已选任务", async () =>
        {
            if (selected is null) throw new InvalidOperationException("请先选择任务");
            if (SessionMonitor.Active(CurrentSession()?.Snapshot.Execution?.State))
                throw new InvalidOperationException("活动加工期间请保留任务文件");
            if (await Ask("删除任务", selected.Name))
            {
                library.Delete(selected.Id); selected = null; prepared = null; preview?.Dispose(); preview = null; InvalidateDraft(); artwork.Text = "尚未选择任务"; confirmed.Checked = false; canvas.Invalidate(); RefreshHistory();
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
        if (SimulationService.Current is not null) throw new InvalidOperationException("请先断开演示设备");
        if (ports.SelectedItemPosition < 0 || ports.SelectedItemPosition >= devices.Count) throw new InvalidOperationException("请扫描并选择设备");
        var port = devices[ports.SelectedItemPosition].Port; var manager = (UsbManager)GetSystemService(UsbService)!;
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
        selected = job; prepared = result; jobName = job.Name; draft.Commit(draft.Revision);
        preview?.Dispose(); preview = new ToolpathPreview(result); artwork.Text = Description(); confirmed.Checked = false; canvas.Invalidate();
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
        await Prepare(); ShowPage(1);
    }
    private static double Value(EditText input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new FormatException("请输入有效数字（小数点使用 .）");
    private JobLimits Limits() { var l = new JobLimits(Value(areaWidth), Value(areaHeight), Value(maxFeed), Value(maxPower)); l.Validate(); return l; }
    private async Task Prepare()
    {
        if (source is null || converting) return;
        var data = source; var name = sourceName; var limits = Limits(); var revision = draft.Revision;
        bool image = IsArtwork(name);
        var settings = image ? new RasterSettings(Value(artWidth), Value(spacing), Value(feed), Value(power), Value(gamma), invert.Checked, true, dither.Checked ? RasterMode.FloydSteinberg : threshold.Checked ? RasterMode.Threshold : RasterMode.Grayscale) : new RasterSettings(1, .1, 1, 1);
        bool vector = outline.Checked;
        var edits = image ? new ImageEdits(Value(cropLeft), Value(cropTop), Value(cropRight), Value(cropBottom), quarterTurns) : new ImageEdits();
        converting = true; conversion?.Dispose(); conversion = new CancellationTokenSource();
        try
        {
            artwork.Text = "正在准备任务…";
            var result = await Task.Run(() => IsArtwork(name) ? ArtworkImporter.Convert(data, name, settings, limits, vector, conversion.Token, edits) : GcodeProgram.Prepare(Encoding.ASCII.GetString(data), limits), conversion.Token);
            conversion.Token.ThrowIfCancellationRequested();
            if (revision != draft.Revision) throw new InvalidOperationException("输入已更新，请重新生成任务");
            string parameters = IsArtwork(name) ? $"宽 {settings.WidthMillimeters:0.##} mm · 行距 {settings.LineSpacing:0.###} mm · F{settings.Feed:0.##} · S{settings.Power:0.##} · Gamma {settings.Gamma:0.##} · {settings.Mode} · 反色 {settings.Invert} · 旋转 {quarterTurns * 90}° · 裁剪 {edits}" : "G-code 指令参数以保存文件为准";
            var job = await Task.Run(() => library.Commit(name, result.Plan, limits, parameters));
            if (conversion.IsCancellationRequested || IsDestroyed) { library.Delete(job.Id); return; }
            if (!draft.Commit(revision)) { library.Delete(job.Id); throw new InvalidOperationException("输入已更新，请重新生成任务"); }
            prepared = result; selected = job; jobName = name; preview?.Dispose(); preview = new ToolpathPreview(result); confirmed.Checked = false; artwork.Text = Description(); canvas.Invalidate();
        }
        finally { converting = false; }
    }
    private static bool IsArtwork(string name) => new[] { ".png", ".jpg", ".jpeg", ".svg", ".webp", ".bmp" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    private string Description() => prepared is null ? "尚未生成" : $"{jobName}\n{prepared.Plan.Lines.Count} 行 · {prepared.MaxX:0.##} × {prepared.MaxY:0.##} mm\n预计 {TimeSpan.FromSeconds(prepared.EstimatedSeconds):hh\\:mm\\:ss} · SHA256 {prepared.Plan.Sha256[..12]}\n{(prepared.EmitsLaser ? "含激光输出" : "无光运动")}";
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
        if (!await Ask(frame ? "范围预跑" : "开始加工", $"{job.Name}\n{analysis.Plan.Lines.Count} 行 · 预计 {TimeSpan.FromSeconds(analysis.EstimatedSeconds):hh\\:mm\\:ss}\n起点 WPos {origin.X:0.###}, {origin.Y:0.###}\n范围 X {analysis.MinX:0.##}–{analysis.MaxX:0.##} / Y {analysis.MinY:0.##}–{analysis.MaxY:0.##} mm\n{job.Parameters}\nSHA256 {job.Hash[..12]}\n确认开始？")) return;
        if (revision != draft.Revision || !draft.IsCurrent) throw new InvalidOperationException("输入已变化，请重新生成并确认任务");
        await session.Start(job, limits, Guid.NewGuid(), epoch, confirmed.Checked, origin); confirmed.Checked = false;
    }
    private void DrawPreview(SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas;
        bool dark = ((int)Resources!.Configuration!.UiMode & 0x30) == 0x20;
        c.Clear(dark ? new SKColor(21, 26, 34) : new SKColor(245, 247, 250));
        if (prepared is null) return;
        float w = (float)Math.Max(1, prepared.MaxX - prepared.MinX), h = (float)Math.Max(1, prepared.MaxY - prepared.MinY);
        float scale = Math.Min((e.Info.Width - 40) / w, (e.Info.Height - 40) / h) * zoom;
        c.Translate(20 + panX, e.Info.Height - 20 + panY); c.Scale(scale, -scale); c.Translate((float)-prepared.MinX, (float)-prepared.MinY);
        var viewport = new SKRect((float)prepared.MinX + (-20 - panX) / scale, (float)prepared.MinY + (-20 + panY) / scale,
            (float)prepared.MinX + (e.Info.Width - 20 - panX) / scale, (float)prepared.MinY + (e.Info.Height - 20 + panY) / scale);
        preview?.Draw(c, viewport, scale);
    }
    private void RefreshHistory()
    { history.Text = "准备任务\n" + string.Join('\n', library.List().Select(j => $"{j.Name} · {j.CreatedAt.ToLocalTime():MM-dd HH:mm}")) + "\n\n执行记录\n" + string.Join('\n', executions.History().Select(x => $"{x.JobName ?? x.Id.ToString()[..8]} · {StateName(x.State)}\n{x.AcknowledgedLines}/{x.TotalLines} 行 · {x.StartedAt}\n{x.Parameters} {x.Error}")); }
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
                source = output.ToArray(); selected = null; prepared = null; preview?.Dispose(); preview = null; quarterTurns = 0; InvalidateDraft(); confirmed.Checked = false; artwork.Text = $"已导入 {sourceName} · {source.Length} 字节"; canvas.Invalidate(); ShowPage(1);
            }
            else if (requestCode is 21 or 22)
            {
                using var stream = ContentResolver!.OpenOutputStream(data.Data) ?? throw new IOException("无法写入文件");
                string content = requestCode == 21 ? library.Read(selected?.Id ?? throw new InvalidOperationException("任务未选择")).Content : "GRBL Tool Mobile 0.3.0\n" + (CurrentSession()?.Snapshot.ToString() ?? "Disconnected") + "\n" + string.Join('\n', CurrentSession()?.Log ?? []);
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
        using var edit = preferences.Edit(); foreach (var input in savedInputs) edit!.PutString(input.Tag?.ToString() ?? "", input.Text); edit!.PutString("selectedJob", draft.IsCurrent ? selected?.Id.ToString() ?? "" : ""); edit.Apply(); base.OnPause();
    }
    protected override void OnDestroy() { conversion?.Cancel(); preview?.Dispose(); base.OnDestroy(); }
    private static void SetLabel(TextView view, string text) { if (view.Text != text) view.Text = text; }
    private void Refresh()
    {
        if (!visible) return;
        var snapshot = CurrentSession()?.Snapshot;
        string mode = SimulationService.Current?.Session is not null ? "演示设备" : "USB";
        SetLabel(status, snapshot is null ? DeviceExecutionService.Current?.ConnectionError ?? SimulationService.Current?.ConnectionError ?? "未连接 · 可体验完整演示" : $"{mode} · {(snapshot.Connection == "Connected" ? snapshot.Machine : "连接中断")} · {(snapshot.IsArmed ? "已就绪" : "待确认就绪")}\n{StateName(snapshot.Execution?.State)}");
        var execution = snapshot?.Execution; bool active = SessionMonitor.Active(execution?.State);
        bool ready = selected is not null && draft.IsCurrent && !converting && !active && snapshot is { Connection: "Connected", Machine: "Idle", IsArmed: true };
        startButton.Enabled = ready && confirmed.Checked; frameButton.Enabled = ready && confirmed.Checked;
        if (snapshot?.IsArmed != true) confirmed.Checked = false;
        confirmed.Enabled = ready;
        pauseButton.Enabled = execution?.State is ExecutionState.Running or ExecutionState.Draining;
        resumeButton.Enabled = execution?.State == ExecutionState.Paused;
        cancelButton.Enabled = active && execution?.State != ExecutionState.Cancelling;
        jobProgress.Progress = execution is { TotalLines: > 0 } ? execution.AcknowledgedLines * 1000 / execution.TotalLines : 0;
        SetLabel(jobSummary, (draft.IsCurrent ? Description() : "输入已变化，请在作品页重新生成任务") + "\n" + (selected?.Parameters ?? "")
            + (execution is null ? "" : $"\n{StateName(execution.State)} · 已确认指令 {execution.AcknowledgedLines}/{execution.TotalLines}\n{execution.Error}"));
        if (snapshot is not null) SetLabel(details, $"{snapshot.Firmware}\n{snapshot.Position}\n$30={snapshot.MaxPower?.ToString() ?? "未知"} · 激光模式 {snapshot.LaserMode} · RX {snapshot.ReceiveBufferBytes?.ToString() ?? "逐行"}\n确认行 {snapshot.Execution?.AcknowledgedLines}/{snapshot.Execution?.TotalLines}\n{snapshot.Error ?? snapshot.Execution?.Error}");
        handler.PostDelayed(Refresh, 250);
    }
}
