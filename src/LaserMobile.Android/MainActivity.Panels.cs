using Android.OS;
using Android.Views;
using Android.Widget;
using System.Globalization;
using LaserMobile.AndroidApp.Services;
namespace LaserMobile.AndroidApp;

public sealed partial class MainActivity
{
    private FrameLayout appPanel = null!;
    private LinearLayout statusBar = null!;
    private Button noticeClose = null!;
    private string? notice;
    private Action? panelCancelled;
    private Android.Window.IOnBackInvokedCallback? panelBack;
    private int currentPage;
    private bool PanelVisible => appPanel is not null && appPanel.Visibility == ViewStates.Visible;
    private LinearLayout OpenPanel(string title, Action? cancelled = null)
    {
        ClosePanel(); HideKeyboard(); panelCancelled = cancelled;
        foreach (var page in pages) page.Visibility = ViewStates.Gone;
        appPanel.RemoveAllViews(); appPanel.Visibility = ViewStates.Visible;
        var screen = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
        screen.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8)); screen.Background = Surface(Dark ? "#172130" : "#FFFFFF");
        appPanel.AddView(screen, new FrameLayout.LayoutParams(-1, -1));
        Label(screen, title, 20, 2);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            panelBack = new PanelBack(ClosePanel); OnBackInvokedDispatcher!.RegisterOnBackInvokedCallback(0, panelBack);
        }
        return screen;
    }
    private void ClosePanel()
    {
        var cancelled = panelCancelled; panelCancelled = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && panelBack is not null) OnBackInvokedDispatcher!.UnregisterOnBackInvokedCallback(panelBack);
        (panelBack as IDisposable)?.Dispose(); panelBack = null;
        if (PanelVisible) { appPanel.Visibility = ViewStates.Gone; appPanel.RemoveAllViews(); for (int i = 0; i < pages.Count; i++) pages[i].Visibility = i == currentPage ? ViewStates.Visible : ViewStates.Gone; }
        cancelled?.Invoke();
    }
    private sealed class PanelBack(Action action) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback { public void OnBackInvoked() => action(); }
#pragma warning disable CS0672, CA1422
    public override void OnBackPressed() { if (PanelVisible) ClosePanel(); else base.OnBackPressed(); }
#pragma warning restore CS0672, CA1422
    private Task<bool> Ask(string title, string message)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var screen = OpenPanel(title, () => completion.TrySetResult(false));
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical }; screen.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        Label(content, message, 14, 13);
        var footer = Row(screen); Button(footer, "返回", () => { ClosePanel(); return Task.CompletedTask; });
        var confirm = Button(footer, "确认", () => { panelCancelled = null; ClosePanel(); completion.TrySetResult(true); return Task.CompletedTask; }); StyleButton(confirm, true);
        return completion.Task;
    }
    private void Choices(string title, IReadOnlyList<string> options, Func<int, Task> choose, int selected = -1)
    {
        var screen = OpenPanel(title); var list = new LinearLayout(this) { Orientation = Orientation.Vertical }; screen.AddView(list, new LinearLayout.LayoutParams(-1, 0, 1));
        if (options.Count > 16) throw new InvalidOperationException("选择项过多，请缩小设备范围");
        int columns = options.Count > 6 ? 2 : 1;
        LinearLayout row = list;
        for (int i = 0; i < options.Count; i++)
        {
            if (columns > 1 && i % columns == 0) row = Row(list);
            int index = i; var choice = Button(row, (index == selected ? "✓ " : "") + options[i], async () => { ClosePanel(); await choose(index); });
            choice.ContentDescription = options[i]; choice.TextSize = columns > 1 ? 11 : 13; StyleButton(choice, index == selected);
        }
        Button(screen, "返回", () => { ClosePanel(); return Task.CompletedTask; });
    }

    private void ShowPagedText(string title, string message)
    {
        if (IsDestroyed) return;
        int columns = Math.Clamp((int)((Resources!.Configuration!.ScreenWidthDp - 56) / (14 * Math.Max(1, Resources.Configuration.FontScale))), 12, 32);
        var lines = new List<string>();
        foreach (var line in message.Replace("\r", "").Split('\n'))
        {
            int[] positions = StringInfo.ParseCombiningCharacters(line); if (positions.Length == 0) { lines.Add(""); continue; }
            for (int i = 0; i < positions.Length; i += columns) { int end = i + columns < positions.Length ? positions[i + columns] : line.Length; lines.Add(line[positions[i]..end]); }
        }
        var chunks = lines.Chunk(7).Select(chunk => string.Join('\n', chunk)).ToArray(); if (chunks.Length == 0) chunks = ["暂无内容"];
        int page = 0; var screen = OpenPanel(title); var content = new LinearLayout(this) { Orientation = Orientation.Vertical }; screen.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        var text = Label(content, chunks[0], 14, 9); Button? previous = null, next = null; TextView? counter = null;
        void Render() { text.Text = chunks[page]; if (counter is not null) counter.Text = $"{page + 1}/{chunks.Length}"; if (previous is not null) previous.Visibility = page == 0 ? ViewStates.Invisible : ViewStates.Visible; if (next is not null) next.Visibility = page + 1 == chunks.Length ? ViewStates.Invisible : ViewStates.Visible; }
        if (chunks.Length > 1)
        {
            counter = Label(screen, "", 12, 1); var row = Row(screen);
            previous = Button(row, "上一页", () => { page--; Render(); return Task.CompletedTask; }); next = Button(row, "下一页", () => { page++; Render(); return Task.CompletedTask; });
        }
        Button(screen, "返回", () => { ClosePanel(); return Task.CompletedTask; }); Render();
    }
    private void ShowMessage(string title, string message) => ShowPagedText(title, message);
    private void ShowError(string message)
    {
        if (IsDestroyed) return;
        notice = message; noticeClose.Visibility = ViewStates.Visible;
        statusBar.Background = Surface(Dark ? "#3B301E" : "#FFF1D6");
        RefreshStatus();
    }
    private void ClearNotice()
    {
        notice = null; noticeClose.Visibility = ViewStates.Gone;
        statusBar.Background = Surface(Dark ? "#1C2C43" : "#E7F0FF"); RefreshStatus();
    }
    private void RefreshStatus()
    {
        var snapshot = CurrentSession()?.Snapshot; string mode = SimulationService.Current?.Session is not null ? "演示设备" : "USB";
        string connection = snapshot is null ? "未连接 · 可体验完整演示" : $"{mode} · {(snapshot.Connection == "Connected" ? snapshot.Machine : "连接中断")} · {(snapshot.IsArmed ? "已就绪" : "待确认就绪")}";
        string? failure = DeviceExecutionService.Current?.ConnectionError ?? SimulationService.Current?.ConnectionError;
        SetLabel(status, connection + (notice is not null ? "\n" + notice : snapshot is null ? failure is null ? "" : "\n" + failure : "\n" + StateName(snapshot.Execution?.State)));
    }
}
