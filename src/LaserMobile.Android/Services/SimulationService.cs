using Android.App;
using Android.Content;
using Android.OS;
using Android.Content.PM;
using LaserMobile.Application;
namespace LaserMobile.AndroidApp.Services;

// dataSync is for this finite local simulation. Real USB execution uses connectedDevice after USB permission.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class SimulationService : Service
{
    private CancellationTokenSource? stop;
    private static string latest = "尚未运行模拟任务";
    public static string Latest => Volatile.Read(ref latest);
    public override IBinder? OnBind(Intent? intent) => null;
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("simulation", "模拟任务", NotificationImportance.Low));
        var notification = new Notification.Builder(this, "simulation")
            .SetContentTitle("GRBL 模拟任务").SetContentText("设备执行服务验证")
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).Build();
        StartForeground(1, notification);
        if (stop != null) return StartCommandResult.NotSticky;
        stop = new CancellationTokenSource();
        var token = stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var session = new SimulationSession(); session.Start();
                while (!session.Sender.IsTerminal)
                {
                    session.Step();
                    var snapshot = session.Sender.Snapshot;
                    Volatile.Write(ref latest, $"模拟：{snapshot.State} · 已确认 {snapshot.AcknowledgedLines}/{snapshot.TotalLines}");
                    await Task.Delay(150, token);
                }
            }
            catch (System.OperationCanceledException) { Volatile.Write(ref latest, "模拟执行已中断"); }
            finally { StopSelf(); }
        }, token);
        return StartCommandResult.NotSticky;
    }
    public override void OnDestroy() { stop?.Cancel(); stop?.Dispose(); stop = null; base.OnDestroy(); }
}
