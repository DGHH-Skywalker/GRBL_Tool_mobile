using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using LaserMobile.Application;
using LaserMobile.Domain;
namespace LaserMobile.AndroidApp.Services;

/// <summary>Foreground progress and task-scoped partial wake lock, independent of the Activity.</summary>
internal sealed class SessionMonitor : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly PowerManager.WakeLock wake;
    private readonly Task loop;
    public static bool Active(ExecutionState? state) => state is ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Draining or ExecutionState.Cancelling;
    public SessionMonitor(Service service, Func<DeviceSession?> getSession, string channel, int notificationId, bool demo)
    {
        wake = ((PowerManager)service.GetSystemService(Context.PowerService)!).NewWakeLock(WakeLockFlags.Partial, "GRBL:Execution")!;
        loop = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    var snapshot = getSession()?.Snapshot;
                    if (Active(snapshot?.Execution?.State)) { if (!wake.IsHeld) wake.Acquire(); }
                    else if (wake.IsHeld) wake.Release();
                    using var notification = Create(service, channel, demo, snapshot);
                    ((NotificationManager)service.GetSystemService(Context.NotificationService)!).Notify(notificationId, notification);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { if (wake.IsHeld) wake.Release(); }
        });
    }
    public static Notification Create(Service service, string channel, bool demo, SessionSnapshot? snapshot)
    {
        using var open = new Intent(service, typeof(MainActivity)); open.SetFlags(ActivityFlags.SingleTop);
        using var pending = PendingIntent.GetActivity(service, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var execution = snapshot?.Execution;
        var text = execution is null ? snapshot?.Machine ?? "正在建立连接" : $"{execution.State} · 确认 {execution.AcknowledgedLines}/{execution.TotalLines} 行";
        var builder = new Notification.Builder(service, channel).SetContentTitle(demo ? "GRBL Tool · 演示设备" : "GRBL Tool · USB 设备")
            .SetContentText(text).SetContentIntent(pending).SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).SetOnlyAlertOnce(true).SetOngoing(true);
        if (execution is not null && Active(execution.State)) builder.SetProgress(execution.TotalLines, execution.AcknowledgedLines, execution.State == ExecutionState.Draining);
        if (execution?.State is ExecutionState.Running or ExecutionState.Draining)
        {
            using var action = new Intent(service, demo ? typeof(SimulationService) : typeof(DeviceExecutionService)); action.SetAction("pause"); action.PutExtra("execution", execution.ExecutionId.ToString());
            using var pause = PendingIntent.GetService(service, demo ? 11 : 12, action, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            builder.AddAction(new Notification.Action.Builder((Icon?)null, "暂停", pause).Build());
        }
        return builder.Build();
    }
    public void Dispose()
    {
        stop.Cancel();
        _ = loop.ContinueWith(_ => { wake.Dispose(); stop.Dispose(); }, TaskScheduler.Default);
    }
}
