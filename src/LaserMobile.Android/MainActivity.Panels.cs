using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Text;
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
        foreach (var page in pages) { ResetMotion(page); page.Visibility = ViewStates.Gone; }
        appPanel.RemoveAllViews(); appPanel.Visibility = ViewStates.Visible;
        var screen = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
        screen.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8)); screen.Background = Surface(Dark ? "#172130" : "#FFFFFF");
        appPanel.AddView(screen, new FrameLayout.LayoutParams(-1, -1));
        Label(screen, title, 20, 2);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            panelBack = new PanelBack(ClosePanel); OnBackInvokedDispatcher!.RegisterOnBackInvokedCallback(0, panelBack);
        }
        Reveal(screen);
        return screen;
    }
    private void ClosePanel()
    {
        var cancelled = panelCancelled; panelCancelled = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && panelBack is not null) OnBackInvokedDispatcher!.UnregisterOnBackInvokedCallback(panelBack);
        (panelBack as IDisposable)?.Dispose(); panelBack = null;
        if (PanelVisible) { appPanel.Visibility = ViewStates.Gone; appPanel.RemoveAllViews(); for (int i = 0; i < pages.Count; i++) { ResetMotion(pages[i]); pages[i].Visibility = i == currentPage ? ViewStates.Visible : ViewStates.Gone; } Reveal(pages[currentPage]); }
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
        int page = 0, pageSize = 12; int columns = options.Count > 6 ? 2 : 1;
        Button previous = null!, next = null!;
        var navigation = Row(screen); navigation.Visibility = ViewStates.Gone;
        previous = Button(navigation, "上一页", () => { page--; Render(); return Task.CompletedTask; });
        next = Button(navigation, "下一页", () => { page++; Render(); return Task.CompletedTask; });
        Button(screen, "返回", () => { ClosePanel(); return Task.CompletedTask; });
        void Render()
        {
            page = Math.Clamp(page, 0, Math.Max(0, (options.Count - 1) / pageSize));
            list.RemoveAllViews(); LinearLayout row = list;
            for (int i = page * pageSize; i < Math.Min(options.Count, (page + 1) * pageSize); i++)
            {
                if (columns > 1 && i % columns == 0) row = Row(list);
                int index = i; var choice = Button(row, (index == selected ? "✓ " : "") + options[i], async () => { ClosePanel(); await choose(index); });
                choice.ContentDescription = options[i]; choice.TextSize = columns > 1 ? 11 : 13; StyleButton(choice, index == selected);
            }
            previous.Visibility = page == 0 ? ViewStates.Invisible : ViewStates.Visible;
            next.Visibility = (page + 1) * pageSize >= options.Count ? ViewStates.Invisible : ViewStates.Visible;
        }
        void Reflow()
        {
            if (list.Height <= 0) return;
            var first = list.GetChildAt(0); int itemHeight = first?.Height ?? 0;
            if (first?.LayoutParameters is ViewGroup.MarginLayoutParams margin) itemHeight += margin.TopMargin + margin.BottomMargin;
            if (itemHeight <= 0) itemHeight = Dp(54);
            int fullHeight = list.Height + (navigation.Visibility == ViewStates.Visible ? navigation.Height : 0);
            bool fits = (long)((options.Count + columns - 1) / columns) * itemHeight <= fullHeight;
            int available = fullHeight - (fits ? 0 : Math.Max(navigation.Height, Dp(54)));
            int capacity = Math.Max(columns, available / itemHeight * columns);
            var visibility = fits ? ViewStates.Gone : ViewStates.Visible;
            if (capacity == pageSize && navigation.Visibility == visibility) return;
            pageSize = capacity; navigation.Visibility = visibility; Render();
            list.Post(Reflow);
        }
        list.LayoutChange += (_, _) => Reflow(); Render(); list.Post(Reflow);
    }

    private void ShowPagedText(string title, string message, Action<LinearLayout>? footer = null, Func<Task>? back = null)
    {
        if (IsDestroyed) return;
        int pageIndex = 0; string[] chunks = [message.Replace("\r", "")];
        var screen = OpenPanel(title);
        var text = new TextView(this) { Text = message, TextSize = 14 }; text.SetTextColor(TextColor); text.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));
        screen.AddView(text, new LinearLayout.LayoutParams(-1, 0, 1));
        Button previous = null!, next = null!; TextView counter = null!;
        var pager = Row(screen); previous = Button(pager, "上一页", () => { pageIndex--; Render(); return Task.CompletedTask; });
        counter = Label(pager, "", 12, 1); next = Button(pager, "下一页", () => { pageIndex++; Render(); return Task.CompletedTask; }); pager.Visibility = ViewStates.Gone;
        footer?.Invoke(screen);
        Button(screen, back is null ? "返回" : "返回设备列表", async () => { ClosePanel(); if (back is not null) await back(); });
        void Render()
        {
            pageIndex = Math.Clamp(pageIndex, 0, chunks.Length - 1); SetLabel(text, chunks[pageIndex]); SetLabel(counter, $"{pageIndex + 1}/{chunks.Length}");
            previous.Visibility = pageIndex == 0 ? ViewStates.Invisible : ViewStates.Visible; next.Visibility = pageIndex + 1 == chunks.Length ? ViewStates.Invisible : ViewStates.Visible;
        }
        void Reflow()
        {
            int width = text.Width - text.PaddingLeft - text.PaddingRight, height = text.Height - text.PaddingTop - text.PaddingBottom;
            if (width <= 0 || height <= 0) return;
            using var value = new Java.Lang.String(message.Replace("\r", ""));
            using var builder = StaticLayout.Builder.Obtain(value, 0, message.Replace("\r", "").Length, text.Paint!, width)!;
            using var layout = builder.SetIncludePad(true)!.Build()!;
            int fullHeight = height + (pager.Visibility == ViewStates.Visible ? pager.Height : 0);
            if (layout.Height <= fullHeight)
            {
                chunks = [value.ToString()]; pager.Visibility = ViewStates.Gone; Render(); return;
            }
            int available = Math.Max(Dp(48), height - (pager.Visibility == ViewStates.Visible ? 0 : Dp(54)));
            var parts = new List<string>(); int first = 0;
            while (first < layout.LineCount)
            {
                int last = first;
                while (last + 1 < layout.LineCount && layout.GetLineBottom(last + 1) - layout.GetLineTop(first) <= available) last++;
                int start = layout.GetLineStart(first), end = layout.GetLineEnd(last);
                parts.Add(value.ToString()[start..end].TrimEnd('\n')); first = last + 1;
            }
            chunks = parts.ToArray(); pager.Visibility = ViewStates.Visible; Render();
        }
        text.LayoutChange += (_, _) => Reflow(); screen.Post(Reflow);
    }

    private void ShowMessage(string title, string message) => ShowPagedText(title, message);
    private void ShowError(string message)
    {
        if (IsDestroyed) return;
        notice = message; noticeClose.Visibility = ViewStates.Visible;
        statusBar.Background = Surface(Dark ? "#3B301E" : "#FFF1D6");
        RefreshStatus(); Reveal(statusBar);
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
