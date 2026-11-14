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
using LaserMobile.Imaging.Skia;
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
    private IReadOnlyList<StoredJob> cachedJobs = [];
    private IReadOnlyList<HistoryEntry> cachedExecutions = [];
    private bool historyLoading, historyReloadPending;
    private Action<int> artworkTabs = _ => { };
    private readonly List<Android.Graphics.Drawables.Drawable?> navigationIcons = [];
    private readonly List<LinearLayout> secondaryBars = [];
    private bool importing;
    private ImportKind? pendingImport;
    private LinearLayout libraryPager = null!, historyPager = null!;
    private int libraryCardHeight, historyCardHeight;
    private Button libraryPrevious = null!, libraryNext = null!, historyPrevious = null!, historyNext = null!;

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
        contourProfile = (ContourProfile)Math.Clamp(preferences.GetInt("contourProfile", 0), 0, 3);
        generationMode = (GenerationMode)Math.Clamp(preferences.GetInt("generationMode", 0), 0, AlgorithmNames.Length - 1);
        statusBar = Row(root); statusBar.Background = Surface(Dark ? "#1C2C43" : "#E7F0FF");
        status = Label(statusBar, "未连接 · 可体验完整演示", 12, 2); status.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1); status.SetPadding(Dp(12), Dp(4), Dp(8), Dp(4));
        status.Click += (_, _) => { if (notice is not null && notice.Length > 26) ShowPagedText("详细信息", notice); };
        noticeClose = Button(statusBar, "×", () => { ClearNotice(); return Task.CompletedTask; }); noticeClose.ContentDescription = "收起提示"; noticeClose.LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48)); noticeClose.Visibility = ViewStates.Gone;
        root.AddView(new Space(this), new LinearLayout.LayoutParams(1, Dp(6)));
        var body = new FrameLayout(this); root.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1));
        for (int i = 0; i < 4; i++)
        {
            var page = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            page.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6)); page.Background = Surface(Dark ? "#172130" : "#FFFFFF");
            body.AddView(page, new FrameLayout.LayoutParams(-1, -1)); pages.Add(page);
        }
        appPanel = new FrameLayout(this) { Visibility = ViewStates.Gone, Clickable = true }; body.AddView(appPanel, new FrameLayout.LayoutParams(-1, -1));
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
            activity.UpdateArtworkKeyboard(keyboard);
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
        box.CheckedChange += (_, _) => { if (!ReferenceEquals(box, confirmed) && !ReferenceEquals(box, showTravel)) InvalidateDraft(); }; parent.AddView(box); return box;
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
        bool changed = currentPage != index || pages[index].Visibility != ViewStates.Visible;
        ClosePanel(); currentPage = index; HideKeyboard();
        for (int i = 0; i < pages.Count; i++) { ResetMotion(pages[i]); pages[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone; StyleButton(navigation[i], i == index); navigationIcons[i]?.SetTint(i == index ? Android.Graphics.Color.White : TextColor); }
        if (changed) Reveal(pages[index]);
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
        int selectedTab = 0;
        void Pick(int index) { bool changed = selectedTab != index; selectedTab = index; HideKeyboard(); for (int i = 0; i < panels.Length; i++) { ResetMotion(panels[i]); panels[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone; StyleButton(buttons[i], i == index); } if (changed) Reveal(panels[index]); }
        for (int i = 0; i < titles.Length; i++) { int index = i; buttons.Add(Button(row, titles[i], () => { Pick(index); return Task.CompletedTask; })); }
        select = Pick; Pick(0); return panels;
    }
    private void BuildDevice()
    {
        var parts = Tabs(pages[0], ["连接", "定位", "参数", "维护"], out _); var p = parts[0];
        details = Label(p, "USB OTG 直连设备，或体验内置演示", 12, 3);
        ports = Button(p, "选择 USB 串口", () =>
        {
            if (devices.Count == 0) ShowError("请先扫描 USB，再选择串口");
            else Choices("选择串口", devices.Select(d => d.Label).ToArray(), index => { selectedPort = index; ports.Text = devices[index].Label; return Task.CompletedTask; }, selectedPort);
            return Task.CompletedTask;
        });
        var row = Row(p); Button(row, "扫描 USB", Scan); Button(row, "授权并连接", Connect);
        row = Row(p); Button(row, "读取配置", () => Session().ReadConfiguration()); Button(row, "确认就绪", ArmDevice);
        Button(p, "体验完整演示", StartDemo);
        row = Row(p); Button(row, "USB 设备诊断", ShowUsbInventory); Button(row, "断开连接", DisconnectDevice);
        p = parts[1]; positionText = Label(p, "连接设备后显示工作坐标", 12, 2);
        jogStep = Number(p, "点动步长 mm（0.1–10）", "1", "jogStep");
        row = Row(p); Button(row, "X−", () => Session().Jog(-1, 0, Value(jogStep), 300)); Button(row, "X+", () => Session().Jog(1, 0, Value(jogStep), 300));
        row = Row(p); Button(row, "Y−", () => Session().Jog(0, -1, Value(jogStep), 300)); Button(row, "Y+", () => Session().Jog(0, 1, Value(jogStep), 300));
        row = Row(p); Button(row, "设为工作零点", async () => { if (await Ask("工作零点", "将当前位置作为工件 X/Y 零点？")) await Session().SetOrigin(); }); Button(row, "取消点动", () => Session().CancelJog());
        Button(p, "确认就绪", ArmDevice);
        row = CompactRow(parts[2]); areaWidth = CompactNumber(row, "区域宽 mm", "工作区域宽度 mm", "40", "areaWidth"); areaHeight = CompactNumber(row, "区域高 mm", "工作区域高度 mm", "40", "areaHeight");
        row = CompactRow(parts[2]); maxFeed = CompactNumber(row, "进给上限", "进给上限 mm/min", "1200", "maxFeed"); maxPower = CompactNumber(row, "功率上限 S", "功率上限 S（设备 $30）", "1000", "maxPower");
        row = CompactRow(parts[2]); baud = CompactNumber(row, "波特率", "波特率", "115200", "baud");
        row = Row(parts[2]); var usbSettings = ReadUsbSettings();
        dtrButton = Button(row, "DTR · " + LineName(usbSettings.Dtr), () => { ChooseLine(true); return Task.CompletedTask; }); dtrButton.ContentDescription = "设置 DTR";
        rtsButton = Button(row, "RTS · " + LineName(usbSettings.Rts), () => { ChooseLine(false); return Task.CompletedTask; }); rtsButton.ContentDescription = "设置 RTS";
        Button(parts[2], "导入设备配置 JSON", () => { OpenPicker(ImportKind.DeviceProfile); return Task.CompletedTask; });
        p = parts[3]; Label(p, "设备维护", 12, 1);
        row = Row(p); Button(row, "硬件回零", async () => { if (await Ask("回零", "确认回零开关、方向与设备 $22=1 配置？")) await Session().Home(true); }); Button(row, "报警解锁", async () => { if (await Ask("解锁", "确认已处理报警原因并检查位置？")) await Session().Unlock(true); });
        row = Row(p); Button(row, "选择工作模式", () => Session().SelectStandardFrame()); Button(row, "软件复位", async () => { if (await Ask("软件复位", "清除控制器队列并重新读取配置？")) await Session().ResetController(true); });
        Label(p, "演示与诊断", 12, 1);
        row = Row(p); Button(row, "模拟报警", () => { Demo().InjectAlarm(); return Task.CompletedTask; }); Button(row, "模拟复位", () => { Demo().InjectReset(); return Task.CompletedTask; });
        row = Row(p); Button(row, "模拟断线", () => { Demo().InjectDisconnect(); return Task.CompletedTask; }); Button(row, "协议日志", () => { ShowPagedText("协议日志", string.Join('\n', Session().Log)); return Task.CompletedTask; });
        row = Row(p); Button(row, "导出日志", () => { ExportDocument(22, "grbl-diagnostics.txt"); return Task.CompletedTask; });
        Button(row, "关于", () => { ShowPagedText("GRBL Tool Mobile 0.7.0", "GPL-3.0-or-later\nOpenCV / OpenCvSharp / Sdcb：Apache-2.0\nUSBSerialForAndroid / SkiaSharp / Svg.Skia：MIT\nClipper2：Boost-1.0\nCyotek 抖动：MIT\n源码 github.com/DGHH-Skywalker/GRBL_Tool_mobile"); return Task.CompletedTask; });
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
        showTravel = Check(row, "显示空移", preferences.GetBoolean("showTravel", false)); showTravel.TextSize = 11; showTravel.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1);
        showTravel.CheckedChange += (_, _) => { canvas.Invalidate(); jobCanvas?.Invalidate(); };
        row = Row(p); generateButton = Button(row, "生成任务", async () => { if (converting) conversion?.Cancel(); else await Prepare(); }); Button(row, "去加工", () => { ShowPage(2); return Task.CompletedTask; });
        BuildArtworkForms(parts[1], parts[2]);
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
        var row = Row(parts[0]); libraryPager = row; libraryPrevious = Button(row, "上一页", () => { libraryIndex = Math.Max(0, libraryIndex - 1); RenderHistory(); return Task.CompletedTask; }); libraryPage = Label(row, "1/1", 12, 1); libraryNext = Button(row, "下一页", () => { libraryIndex++; RenderHistory(); return Task.CompletedTask; });
        row = Row(parts[0]); Button(row, "导出任务", () => { if (selected is null) throw new InvalidOperationException("请先选择任务"); ExportDocument(21, selected.Name + ".nc"); return Task.CompletedTask; });
        Button(row, "删除所选", async () =>
        {
            if (selected is null) throw new InvalidOperationException("请先选择任务");
            if (SessionMonitor.Active(CurrentSession()?.Snapshot.Execution?.State)) throw new InvalidOperationException("请先结束当前任务");
            if (await Ask("删除任务", selected.Name)) { library.Delete(selected.Id); selected = null; prepared = null; preview?.Dispose(); preview = null; InvalidateDraft(); artwork.Text = "尚未选择任务"; canvas.Invalidate(); jobCanvas?.Invalidate(); RefreshHistory(); }
        });
        historyCards = new LinearLayout(this) { Orientation = Orientation.Vertical }; parts[1].AddView(historyCards, new LinearLayout.LayoutParams(-1, 0, 1));
        row = Row(parts[1]); historyPager = row; historyPrevious = Button(row, "上一页", () => { historyIndex = Math.Max(0, historyIndex - 1); RenderHistory(); return Task.CompletedTask; }); historyPage = Label(row, "1/1", 12, 1); historyNext = Button(row, "下一页", () => { historyIndex++; RenderHistory(); return Task.CompletedTask; });
        Button(parts[1], "刷新记录", () => { RefreshHistory(); return Task.CompletedTask; });
        libraryCards.LayoutChange += (_, _) => { if (currentPage == 3 && !PanelVisible) RenderHistory(); };
        historyCards.LayoutChange += (_, _) => { if (currentPage == 3 && !PanelVisible) RenderHistory(); };
    }
    private void RefreshHistory()
    {
        if (historyLoading) { historyReloadPending = true; return; }
        historyLoading = true; _ = LoadHistory();
    }
    private async Task LoadHistory()
    {
        try
        {
            var data = await Task.Run(() => (Jobs: library.List(), Executions: executions.History()));
            if (IsDestroyed) return;
            cachedJobs = data.Jobs; cachedExecutions = data.Executions;
            if (currentPage == 3 && !PanelVisible) RenderHistory();
        }
        catch (Exception e) { if (!IsDestroyed) ShowError(e.Message); }
        finally { historyLoading = false; if (historyReloadPending && !IsDestroyed) { historyReloadPending = false; RefreshHistory(); } }
    }
    private void RenderHistory()
    {
        var jobs = cachedJobs; var entries = cachedExecutions;
        int libraryCount = HistoryCapacity(libraryCards, libraryPager, jobs.Count, libraryCardHeight, false);
        int historyCount = HistoryCapacity(historyCards, historyPager, entries.Count, historyCardHeight, true);
        libraryIndex = Math.Clamp(libraryIndex, 0, Math.Max(0, (jobs.Count - 1) / libraryCount)); historyIndex = Math.Clamp(historyIndex, 0, Math.Max(0, (entries.Count - 1) / historyCount));
        libraryCards.RemoveAllViews(); historyCards.RemoveAllViews();
        foreach (var job in jobs.Skip(libraryIndex * libraryCount).Take(libraryCount))
        {
            var card = new LinearLayout(this) { Orientation = Orientation.Vertical }; libraryCards.AddView(card);
            Button(card, job.Name, async () => { await LoadSaved(job.Id); ShowPage(1); artworkTabs(0); }); Label(card, $"{job.CreatedAt.ToLocalTime():MM-dd HH:mm} · {job.Hash[..12]}", 12, 1);
        }
        foreach (var entry in entries.Skip(historyIndex * historyCount).Take(historyCount))
        {
            var card = new LinearLayout(this) { Orientation = Orientation.Vertical }; historyCards.AddView(card);
            Label(card, entry.JobName ?? entry.Id.ToString()[..8], 14, 1); Label(card, $"{StateName(entry.State)} · {entry.AcknowledgedLines}/{entry.TotalLines} 行", 12, 1);
            Button(card, "执行详情", () => { ShowPagedText("执行详情", $"{entry.JobName}\n{StateName(entry.State)} · {entry.AcknowledgedLines}/{entry.TotalLines} 行\n{entry.StartedAt}\n{entry.Parameters}\n{entry.Hash}\n{entry.Error}"); return Task.CompletedTask; });
        }
        if (jobs.Count == 0) Label(libraryCards, "生成任务后显示在这里"); if (entries.Count == 0) Label(historyCards, "开始加工后显示执行记录");
        libraryPage.Text = $"{libraryIndex + 1}/{Math.Max(1, (jobs.Count + libraryCount - 1) / libraryCount)}"; historyPage.Text = $"{historyIndex + 1}/{Math.Max(1, (entries.Count + historyCount - 1) / historyCount)}";
        libraryPrevious.Visibility = libraryIndex == 0 ? ViewStates.Invisible : ViewStates.Visible; libraryNext.Visibility = (libraryIndex + 1) * libraryCount >= jobs.Count ? ViewStates.Invisible : ViewStates.Visible;
        historyPrevious.Visibility = historyIndex == 0 ? ViewStates.Invisible : ViewStates.Visible; historyNext.Visibility = (historyIndex + 1) * historyCount >= entries.Count ? ViewStates.Invisible : ViewStates.Visible;
        MeasureHistoryCards();
    }
    private int HistoryCapacity(LinearLayout cards, LinearLayout pager, int total, int measured, bool execution)
    {
        int card = measured > 0 ? measured : Dp(execution ? 108 : 80);
        int height = cards.Height > 0 ? cards.Height : Dp(Math.Max(160, Resources!.Configuration!.ScreenHeightDp - 250));
        height += pager.Visibility == ViewStates.Visible ? pager.Height : 0;
        bool fits = (long)total * card <= height;
        pager.Visibility = fits ? ViewStates.Gone : ViewStates.Visible;
        return Math.Max(1, fits ? total : (height - Dp(54)) / card);
    }
    private void MeasureHistoryCards()
    {
        libraryCards.Post(() =>
        {
            int a = libraryCards.GetChildAt(0) is LinearLayout libraryCard ? libraryCard.Height : 0, b = historyCards.GetChildAt(0) is LinearLayout historyCard ? historyCard.Height : 0;
            bool changed = a > 0 && libraryCardHeight != a || b > 0 && historyCardHeight != b;
            if (a > 0) libraryCardHeight = a; if (b > 0) historyCardHeight = b;
            if (changed && currentPage == 3 && !PanelVisible) RenderHistory();
        });
    }
    protected override void OnSaveInstanceState(Bundle outState)
    { if (pendingImport is { } kind) outState.PutInt("pendingImport", (int)kind); base.OnSaveInstanceState(outState); }
}
