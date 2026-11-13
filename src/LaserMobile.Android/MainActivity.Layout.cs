using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using SkiaSharp.Views.Android;
using LaserMobile.Domain;
using LaserMobile.Persistence;
using LaserMobile.AndroidApp.Services;
namespace LaserMobile.AndroidApp;

public sealed partial class MainActivity
{
    private TextView executionText = null!, positionText = null!, previewHint = null!;
    private Button generateButton = null!;
    private LinearLayout bottomNavigation = null!, libraryCards = null!, historyCards = null!;
    private TextView libraryPage = null!, historyPage = null!;
    private SKCanvasView? jobCanvas;
    private int libraryIndex, historyIndex;
    private Action<int> artworkTabs = _ => { };
    private readonly List<Android.Graphics.Drawables.Drawable?> navigationIcons = [];
    private readonly List<LinearLayout> secondaryBars = [];
    private bool importing;
    private ImportKind? pendingImport;
    private int CardsPerPage => Resources!.Configuration!.ScreenHeightDp >= 700 && Resources.Configuration.FontScale <= 1.15f ? 3 : 2;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        preferences = GetSharedPreferences("settings", FileCreationMode.Private)!;
        library = new JobLibrary(Path.Combine(FilesDir!.AbsolutePath, "jobs"));
        executions = new ExecutionStore(Path.Combine(FilesDir.AbsolutePath, "executions.sqlite"));
        if (DeviceExecutionService.Current is null && SimulationService.Current is null) executions.RecoverInterrupted();
        if (savedInstanceState?.ContainsKey("pendingImport") == true) pendingImport = (ImportKind)savedInstanceState.GetInt("pendingImport");
        root = new LinearLayout(this) { Orientation = Orientation.Vertical, FocusableInTouchMode = true };
        root.SetPadding(Dp(8), Dp(28), Dp(8), Dp(8));
        root.SetBackgroundColor(Android.Graphics.Color.ParseColor(Dark ? "#101722" : "#F3F6FB"));
        SetContentView(root);
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
#pragma warning disable CA1422
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) Window.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
        status = Label(root, "未连接 · 可体验完整演示", 12, 2);
        status.Background = Surface(Dark ? "#1C2C43" : "#E7F0FF"); status.SetPadding(Dp(12), Dp(4), Dp(12), Dp(4));
        root.AddView(new Space(this), new LinearLayout.LayoutParams(1, Dp(6)));
        var body = new FrameLayout(this); root.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1));
        for (int i = 0; i < 4; i++)
        {
            var page = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            page.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6)); page.Background = Surface(Dark ? "#172130" : "#FFFFFF");
            body.AddView(page, new FrameLayout.LayoutParams(-1, -1)); pages.Add(page);
        }
        BuildDevice(); BuildArtwork(); BuildJob(); BuildHistory();
        bottomNavigation = Row(root); bottomNavigation.ContentDescription = "底部导航";
        string[] titles = ["设备", "作品", "加工", "历史"];
        int[] icons = [Android.Resource.Drawable.IcMenuManage, Android.Resource.Drawable.IcMenuGallery, Android.Resource.Drawable.IcMediaPlay, Android.Resource.Drawable.IcMenuRecentHistory];
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i; var button = Button(bottomNavigation, titles[i], () => { ShowPage(index); return Task.CompletedTask; });
            var icon = GetDrawable(icons[i]); icon?.SetBounds(0, 0, Dp(20), Dp(20)); navigationIcons.Add(icon);
            if (button.LayoutParameters is LinearLayout.LayoutParams dimensions) { dimensions.Height = Dp(56); button.LayoutParameters = dimensions; }
            button.SetCompoundDrawables(null, icon, null, null); button.CompoundDrawablePadding = Dp(2); button.TextSize = 11; button.SetMinHeight(Dp(56)); navigation.Add(button);
        }
        root.SetOnApplyWindowInsetsListener(new AppInsets(this)); root.RequestApplyInsets();
        ShowPage(0); root.RequestFocus();
        if (Guid.TryParse(preferences.GetString("selectedJob", ""), out var savedId)) _ = RestoreSaved(savedId);
    }
    private sealed class AppInsets(MainActivity activity) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (insets is null) throw new ArgumentNullException(nameof(insets));
            int top, bottom, left, right; bool keyboard = false;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                var ime = insets.GetInsets(WindowInsets.Type.Ime());
                top = bars.Top; bottom = Math.Max(bars.Bottom, ime.Bottom); left = bars.Left; right = bars.Right; keyboard = insets.IsVisible(WindowInsets.Type.Ime());
            }
            else
            {
#pragma warning disable CA1422
                top = insets.SystemWindowInsetTop; bottom = insets.SystemWindowInsetBottom; left = insets.SystemWindowInsetLeft; right = insets.SystemWindowInsetRight;
#pragma warning restore CA1422
            }
            activity.root.SetPadding(left + activity.Dp(8), top + activity.Dp(4), right + activity.Dp(8), bottom + activity.Dp(4));
            activity.bottomNavigation.Visibility = keyboard ? ViewStates.Gone : ViewStates.Visible;
            foreach (var bar in activity.secondaryBars) bar.Visibility = keyboard ? ViewStates.Gone : ViewStates.Visible;
            activity.status.SetMaxLines(keyboard ? 1 : 2);
            return insets;
        }
    }
    private TextView Label(LinearLayout parent, string text, float size = 14, int maxLines = 2)
    {
        var v = new TextView(this) { Text = text, TextSize = size, Ellipsize = TextUtils.TruncateAt.End };
        v.SetMaxLines(maxLines); v.SetTextColor(TextColor); v.SetPadding(0, Dp(3), 0, Dp(3)); parent.AddView(v); return v;
    }
    private LinearLayout Row(LinearLayout parent)
    { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; row.SetGravity(GravityFlags.CenterVertical); parent.AddView(row); return row; }
    private EditText Number(LinearLayout page, string title, string value, string key)
    {
        Label(page, title, 12, 1);
        var input = new EditText(this) { Text = preferences.GetString(key, value), Tag = key, ContentDescription = title, InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal, ImeOptions = ImeAction.Done, TextSize = 16 };
        input.SetSingleLine(true); input.SetPadding(Dp(8), 0, Dp(8), 0); input.SetMinHeight(Dp(48));
        input.TextChanged += (_, _) => { if (key is not ("baud" or "jogStep")) InvalidateDraft(); };
        input.EditorAction += (_, e) => { HideKeyboard(); e.Handled = true; };
        page.AddView(input, new LinearLayout.LayoutParams(-1, Dp(48))); savedInputs.Add(input); return input;
    }
    private CheckBox Check(LinearLayout parent, string title, bool value = false)
    {
        var box = new CheckBox(this) { Text = title, Checked = value, TextSize = 13, ContentDescription = title }; box.SetMinHeight(Dp(48)); box.SetMaxLines(2);
        box.CheckedChange += (_, _) => { if (!ReferenceEquals(box, confirmed)) InvalidateDraft(); }; parent.AddView(box); return box;
    }
    private Button Button(LinearLayout parent, string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text, ContentDescription = text, TextSize = 13, Ellipsize = TextUtils.TruncateAt.End }; button.SetMaxLines(1);
        StyleButton(button, text is "体验完整演示" or "开始加工" or "生成任务" or "授权并连接"); button.SetMinHeight(Dp(48));
        var layout = new LinearLayout.LayoutParams(parent.Orientation == Orientation.Horizontal ? 0 : -1, Dp(48), parent.Orientation == Orientation.Horizontal ? 1 : 0);
        layout.SetMargins(Dp(2), Dp(3), Dp(2), Dp(3)); parent.AddView(button, layout);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); }
            catch (Exception e) { ShowError(e.Message); }
            finally { if (!IsDestroyed) button.Enabled = true; }
        };
        return button;
    }
    private void HideKeyboard()
    {
        if (CurrentFocus is { } focus) ((InputMethodManager)GetSystemService(InputMethodService)!).HideSoftInputFromWindow(focus.WindowToken, HideSoftInputFlags.None);
        root.RequestFocus();
    }
    private void ShowPage(int index)
    {
        HideKeyboard();
        for (int i = 0; i < pages.Count; i++) { pages[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone; StyleButton(navigation[i], i == index); navigationIcons[i]?.SetTint(i == index ? Android.Graphics.Color.White : TextColor); }
        if (index == 3) RefreshHistory();
    }
    private LinearLayout[] Tabs(LinearLayout parent, string[] titles, out Action<int> select)
    {
        var row = Row(parent); secondaryBars.Add(row); var buttons = new List<Button>(); var frame = new FrameLayout(this);
        parent.AddView(frame, new LinearLayout.LayoutParams(-1, 0, 1)); var panels = new LinearLayout[titles.Length];
        for (int i = 0; i < titles.Length; i++)
        {
            panels[i] = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = i == 0 ? ViewStates.Visible : ViewStates.Gone };
            frame.AddView(panels[i], new FrameLayout.LayoutParams(-1, -1));
        }
        void Pick(int index) { HideKeyboard(); for (int i = 0; i < panels.Length; i++) { panels[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone; StyleButton(buttons[i], i == index); } }
        for (int i = 0; i < titles.Length; i++) { int index = i; buttons.Add(Button(row, titles[i], () => { Pick(index); return Task.CompletedTask; })); }
        select = Pick; Pick(0); return panels;
    }
    private void FormPager(LinearLayout parent, params (string Title, Action<LinearLayout> Build)[] steps)
    {
        int current = 0; var title = Label(parent, "", 13, 1); var frame = new FrameLayout(this);
        parent.AddView(frame, new LinearLayout.LayoutParams(-1, 0, 1)); var forms = new List<LinearLayout>();
        foreach (var step in steps)
        {
            var form = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone }; step.Build(form); frame.AddView(form, new FrameLayout.LayoutParams(-1, -1)); forms.Add(form);
        }
        var footer = Row(parent); Button? previous = null, next = null;
        void Pick(int index)
        {
            HideKeyboard(); current = Math.Clamp(index, 0, steps.Length - 1); title.Text = $"{steps[current].Title} · {current + 1}/{steps.Length}";
            for (int i = 0; i < forms.Count; i++) forms[i].Visibility = i == current ? ViewStates.Visible : ViewStates.Gone;
            if (previous is not null) previous.Visibility = current == 0 ? ViewStates.Invisible : ViewStates.Visible;
            if (next is not null) next.Visibility = current == steps.Length - 1 ? ViewStates.Invisible : ViewStates.Visible;
        }
        previous = Button(footer, "上一步", () => { Pick(current - 1); return Task.CompletedTask; });
        next = Button(footer, "下一步", () => { Pick(current + 1); return Task.CompletedTask; }); Pick(0);
    }
    private void BuildDevice()
    {
        var parts = Tabs(pages[0], ["连接", "定位", "参数", "维护"], out _); var p = parts[0];
        details = Label(p, "USB OTG 直连设备，或体验内置演示", 12, 3);
        ports = new Spinner(this) { ContentDescription = "USB 串口" }; p.AddView(ports, new LinearLayout.LayoutParams(-1, Dp(48)));
        var row = Row(p); Button(row, "扫描 USB", Scan); Button(row, "授权并连接", Connect);
        row = Row(p); Button(row, "读取配置", () => Session().ReadConfiguration()); Button(row, "确认就绪", ArmDevice);
        Button(p, "体验完整演示", StartDemo);
        Button(p, "断开连接", DisconnectDevice);
        p = parts[1]; positionText = Label(p, "连接设备后显示工作坐标", 12, 2);
        jogStep = Number(p, "点动步长 mm（0.1–10）", "1", "jogStep");
        row = Row(p); Button(row, "X−", () => Session().Jog(-1, 0, Value(jogStep), 300)); Button(row, "X+", () => Session().Jog(1, 0, Value(jogStep), 300));
        row = Row(p); Button(row, "Y−", () => Session().Jog(0, -1, Value(jogStep), 300)); Button(row, "Y+", () => Session().Jog(0, 1, Value(jogStep), 300));
        row = Row(p); Button(row, "设为工作零点", async () => { if (await Ask("工作零点", "将当前位置作为工件 X/Y 零点？")) await Session().SetOrigin(); }); Button(row, "取消点动", () => Session().CancelJog());
        Button(p, "确认就绪", ArmDevice);
        FormPager(parts[2],
            ("工作区域", form => { areaWidth = Number(form, "工作区域宽度 mm", "40", "areaWidth"); areaHeight = Number(form, "工作区域高度 mm", "40", "areaHeight"); }),
            ("运行上限", form => { maxFeed = Number(form, "进给上限 mm/min", "1200", "maxFeed"); maxPower = Number(form, "功率上限 S（设备 $30）", "1000", "maxPower"); }),
            ("串口与预设", form => { baud = Number(form, "波特率", "115200", "baud"); Button(form, "导入设备配置 JSON", () => { OpenPicker(ImportKind.DeviceProfile); return Task.CompletedTask; }); }));
        FormPager(parts[3],
            ("设备维护", form =>
            {
                Button(form, "硬件回零", async () => { if (await Ask("回零", "确认回零开关、方向与设备 $22=1 配置？")) await Session().Home(true); });
                Button(form, "报警解锁", async () => { if (await Ask("解锁", "确认已处理报警原因并检查位置？")) await Session().Unlock(true); });
                Button(form, "选择工作模式", () => Session().SelectStandardFrame());
                Button(form, "软件复位", async () => { if (await Ask("软件复位", "清除控制器队列并重新读取配置？")) await Session().ResetController(true); });
            }),
            ("演示与诊断", form =>
            {
                var controls = Row(form); Button(controls, "模拟报警", () => { Demo().InjectAlarm(); return Task.CompletedTask; }); Button(controls, "模拟复位", () => { Demo().InjectReset(); return Task.CompletedTask; });
                Button(form, "模拟断线", () => { Demo().InjectDisconnect(); return Task.CompletedTask; });
                Button(form, "协议日志", () => { ShowPagedText("协议日志", string.Join('\n', Session().Log)); return Task.CompletedTask; });
                var exports = Row(form); Button(exports, "导出日志", () => { ExportDocument(22, "grbl-diagnostics.txt"); return Task.CompletedTask; });
                Button(exports, "关于", () => { ShowPagedText("GRBL Tool Mobile 0.4.0", "GPL-3.0-or-later\nUSBSerialForAndroid / SkiaSharp / Svg.Skia：MIT\nClipper2：Boost-1.0\nCyotek 抖动：MIT\n星光扩展独立维护，硬件资料待确认\n源码 github.com/DGHH-Skywalker/GRBL_Tool_mobile"); return Task.CompletedTask; });
            }));
    }
    private async Task ArmDevice() { if (await Ask("设备就绪", "确认设备配置、工作原点与工作区域？")) await Session().Arm(true); }
    private async Task DisconnectDevice()
    {
        if (await Ask("断开连接", "结束设备会话？活动任务将记为中断。")) { if (DeviceExecutionService.Current is { } service) await service.Disconnect(); SimulationService.Current?.Disconnect(); }
    }
    private void BuildArtwork()
    {
        var parts = Tabs(pages[1], ["预览", "加工参数", "图像调整"], out artworkTabs); var p = parts[0];
        artwork = Label(p, "导入作品，或在设备页体验完整演示", 13, 3);
        var frame = new FrameLayout(this); p.AddView(frame, new LinearLayout.LayoutParams(-1, 0, 1));
        canvas = new SKCanvasView(this) { ContentDescription = "加工路径预览" }; frame.AddView(canvas, new FrameLayout.LayoutParams(-1, -1));
        previewHint = new TextView(this) { Text = "路径预览\n导入后生成任务", TextSize = 16, Gravity = GravityFlags.Center }; previewHint.SetTextColor(TextColor); frame.AddView(previewHint, new FrameLayout.LayoutParams(-1, -1));
        canvas.PaintSurface += (_, e) => DrawPreview(e); WirePreviewTouch();
        var row = Row(p); Button(row, "导入作品", ChooseImport); Button(row, "适合屏幕", () => { zoom = 1; panX = panY = 0; canvas.Invalidate(); return Task.CompletedTask; });
        row = Row(p); generateButton = Button(row, "生成任务", Prepare); Button(row, "去加工", () => { ShowPage(2); return Task.CompletedTask; });
        FormPager(parts[1],
            ("尺寸与精度", form => { artWidth = Number(form, "加工宽度 mm", "30", "artWidth"); spacing = Number(form, "光栅行距 mm", "0.15", "spacing"); }),
            ("速度与功率", form => { feed = Number(form, "加工进给 mm/min", "600", "feed"); power = Number(form, "加工最大功率 S", "100", "power"); }),
            ("图像算法", form =>
            {
                gamma = Number(form, "Gamma", "1", "gamma");
                var mode = new Spinner(this) { ContentDescription = "加工算法" }; mode.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, ["灰度扫描", "黑白阈值", "Floyd–Steinberg 抖动", "SVG 轮廓"]); form.AddView(mode, new LinearLayout.LayoutParams(-1, Dp(48)));
                // Preserve existing CAM contracts; one visible selector maps to mutually exclusive modes.
                var backing = new LinearLayout(this); threshold = Check(backing, "阈值"); dither = Check(backing, "抖动"); outline = Check(backing, "轮廓");
                mode.ItemSelected += (_, e) => { threshold.Checked = e.Position == 1; dither.Checked = e.Position == 2; outline.Checked = e.Position == 3; };
                invert = Check(form, "反色");
            }));
        FormPager(parts[2],
            ("裁剪起点 · 0–1 比例", form => { cropLeft = Number(form, "裁剪左", "0", "cropLeft"); cropTop = Number(form, "裁剪上", "0", "cropTop"); }),
            ("裁剪终点 · 0–1 比例", form => { cropRight = Number(form, "裁剪右", "1", "cropRight"); cropBottom = Number(form, "裁剪下", "1", "cropBottom"); }),
            ("旋转与转换", form =>
            {
                var rotation = Label(form, "顺时针旋转 0°"); Button(form, "旋转 90°", () => { quarterTurns = (quarterTurns + 1) % 4; rotation.Text = $"顺时针旋转 {quarterTurns * 90}°"; InvalidateDraft(); return Task.CompletedTask; });
                Button(form, "取消转换", () => { conversion?.Cancel(); return Task.CompletedTask; });
                Label(form, "图像参数用于位图与 SVG。G-code 沿用文件中的速度与功率。", 12, 3);
            }));
    }
    private void WirePreviewTouch()
    {
        canvas.Touch += (_, e) =>
        {
            var ev = e.Event!;
            if (ev.ActionMasked is MotionEventActions.Down or MotionEventActions.PointerUp) { lastX = ev.GetX(); lastY = ev.GetY(); lastDistance = 0; }
            if (ev.ActionMasked == MotionEventActions.Move)
            {
                if (ev.PointerCount == 2)
                {
                    float distance = MathF.Sqrt(MathF.Pow(ev.GetX(0) - ev.GetX(1), 2) + MathF.Pow(ev.GetY(0) - ev.GetY(1), 2));
                    if (lastDistance > 0) zoom = Math.Clamp(zoom * distance / lastDistance, .2f, 20); lastDistance = distance;
                }
                else { panX += ev.GetX() - lastX; panY += ev.GetY() - lastY; lastX = ev.GetX(); lastY = ev.GetY(); }
                canvas.Invalidate();
            }
            if (ev.ActionMasked == MotionEventActions.Up) lastDistance = 0;
            e.Handled = true;
        };
    }
    private void BuildJob()
    {
        var p = pages[2]; jobSummary = Label(p, "先导入作品并生成任务", 14, 3);
        jobCanvas = new SKCanvasView(this) { ContentDescription = "任务路径" }; p.AddView(jobCanvas, new LinearLayout.LayoutParams(-1, 0, 1)); jobCanvas.PaintSurface += (_, e) => DrawPreview(e);
        executionText = Label(p, "等待开始", 13, 2); jobProgress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000 }; p.AddView(jobProgress, new LinearLayout.LayoutParams(-1, Dp(6)));
        confirmed = Check(p, "已检查现场、工作原点与加工参数");
        var row = Row(p);
        startButton = Button(row, "开始加工", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请完成加工确认"); await StartSelected(false); });
        frameButton = Button(row, "范围预跑", async () => { if (!confirmed.Checked) throw new InvalidOperationException("请完成范围确认"); await StartSelected(true); });
        row = Row(p); pauseButton = Button(row, "暂停", () => { var s = Session(); return s.Pause(s.Snapshot.Execution?.ExecutionId ?? Guid.Empty); });
        resumeButton = Button(row, "继续", async () => { var s = Session(); var id = s.Snapshot.Execution?.ExecutionId ?? Guid.Empty; if (await Ask("继续加工", "确认当前现场状态允许继续？")) await s.Resume(id, true); });
        cancelButton = Button(p, "取消加工", async () => { var s = Session(); var id = s.Snapshot.Execution?.ExecutionId ?? Guid.Empty; if (await Ask("取消加工", "发送保持并复位控制器，结束当前执行？")) await s.Cancel(id); });
    }
    private void BuildHistory()
    {
        var parts = Tabs(pages[3], ["任务库", "执行记录"], out _);
        libraryCards = new LinearLayout(this) { Orientation = Orientation.Vertical }; parts[0].AddView(libraryCards, new LinearLayout.LayoutParams(-1, 0, 1));
        var row = Row(parts[0]); Button(row, "上一页", () => { libraryIndex = Math.Max(0, libraryIndex - 1); RefreshHistory(); return Task.CompletedTask; }); libraryPage = Label(row, "1/1", 12, 1); Button(row, "下一页", () => { libraryIndex++; RefreshHistory(); return Task.CompletedTask; });
        row = Row(parts[0]); Button(row, "导出任务", () => { if (selected is null) throw new InvalidOperationException("请先选择任务"); ExportDocument(21, selected.Name + ".nc"); return Task.CompletedTask; });
        Button(row, "删除所选", async () =>
        {
            if (selected is null) throw new InvalidOperationException("请先选择任务");
            if (SessionMonitor.Active(CurrentSession()?.Snapshot.Execution?.State)) throw new InvalidOperationException("请先结束当前任务");
            if (await Ask("删除任务", selected.Name)) { library.Delete(selected.Id); selected = null; prepared = null; preview?.Dispose(); preview = null; InvalidateDraft(); artwork.Text = "尚未选择任务"; canvas.Invalidate(); jobCanvas?.Invalidate(); RefreshHistory(); }
        });
        historyCards = new LinearLayout(this) { Orientation = Orientation.Vertical }; parts[1].AddView(historyCards, new LinearLayout.LayoutParams(-1, 0, 1));
        row = Row(parts[1]); Button(row, "上一页", () => { historyIndex = Math.Max(0, historyIndex - 1); RefreshHistory(); return Task.CompletedTask; }); historyPage = Label(row, "1/1", 12, 1); Button(row, "下一页", () => { historyIndex++; RefreshHistory(); return Task.CompletedTask; });
        Button(parts[1], "刷新记录", () => { RefreshHistory(); return Task.CompletedTask; });
    }
    private void RefreshHistory()
    {
        var jobs = library.List(); var entries = executions.History(); int count = CardsPerPage;
        libraryIndex = Math.Clamp(libraryIndex, 0, Math.Max(0, (jobs.Count - 1) / count)); historyIndex = Math.Clamp(historyIndex, 0, Math.Max(0, (entries.Count - 1) / count));
        libraryCards.RemoveAllViews(); historyCards.RemoveAllViews();
        foreach (var job in jobs.Skip(libraryIndex * count).Take(count))
        {
            var card = new LinearLayout(this) { Orientation = Orientation.Vertical }; libraryCards.AddView(card);
            Button(card, job.Name, async () => { await LoadSaved(job.Id); ShowPage(1); artworkTabs(0); }); Label(card, $"{job.CreatedAt.ToLocalTime():MM-dd HH:mm} · {job.Hash[..12]}", 12, 1);
        }
        foreach (var entry in entries.Skip(historyIndex * count).Take(count))
        {
            var card = new LinearLayout(this) { Orientation = Orientation.Vertical }; historyCards.AddView(card);
            Label(card, entry.JobName ?? entry.Id.ToString()[..8], 14, 1); Label(card, $"{StateName(entry.State)} · {entry.AcknowledgedLines}/{entry.TotalLines} 行", 12, 1);
            Button(card, "执行详情", () => { ShowPagedText("执行详情", $"{entry.JobName}\n{StateName(entry.State)} · {entry.AcknowledgedLines}/{entry.TotalLines} 行\n{entry.StartedAt}\n{entry.Parameters}\n{entry.Hash}\n{entry.Error}"); return Task.CompletedTask; });
        }
        if (jobs.Count == 0) Label(libraryCards, "生成任务后显示在这里"); if (entries.Count == 0) Label(historyCards, "开始加工后显示执行记录");
        libraryPage.Text = $"{libraryIndex + 1}/{Math.Max(1, (jobs.Count + count - 1) / count)}"; historyPage.Text = $"{historyIndex + 1}/{Math.Max(1, (entries.Count + count - 1) / count)}";
    }
    private TextView DialogText(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 14 }; view.SetTextColor(TextColor); view.SetPadding(Dp(20), Dp(8), Dp(20), Dp(8)); return view;
    }
    private void ShowPagedText(string title, string message)
    {
        if (IsDestroyed) return;
        // Explicit pages keep long logs/details reachable without a scrolling dialog.
        var lines = message.Replace("\r", "").Split('\n').SelectMany(line => line.Length == 0 ? new[] { "" } : Enumerable.Range(0, (line.Length + 33) / 34).Select(i => line.Substring(i * 34, Math.Min(34, line.Length - i * 34)))).ToArray();
        var chunks = lines.Chunk(5).Select(c => string.Join('\n', c)).ToArray(); if (chunks.Length == 0) chunks = ["暂无内容"];
        int page = 0; var content = new LinearLayout(this) { Orientation = Orientation.Vertical }; var text = DialogText(chunks[0]); content.AddView(text);
        var footer = Row(content); Button(footer, "上一页", () => { page = Math.Max(0, page - 1); text.Text = chunks[page]; return Task.CompletedTask; }); Button(footer, "下一页", () => { page = Math.Min(chunks.Length - 1, page + 1); text.Text = chunks[page]; return Task.CompletedTask; });
        var builder = new AlertDialog.Builder(this); builder.SetTitle(title); builder.SetView(content); builder.SetPositiveButton("关闭", (_, _) => { }); builder.Show();
    }
    protected override void OnSaveInstanceState(Bundle outState)
    { if (pendingImport is { } kind) outState.PutInt("pendingImport", (int)kind); base.OnSaveInstanceState(outState); }
}
