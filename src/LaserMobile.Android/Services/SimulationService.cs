using Android.App;
using Android.Content;
using Android.OS;
using Android.Content.PM;
using LaserMobile.Application;
using LaserMobile.Persistence;
namespace LaserMobile.AndroidApp.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class SimulationService : Service
{
    private static SimulationService? instance;
    private DeviceSession? session;
    private SessionMonitor? monitor;
    public static SimulationService? Current => Volatile.Read(ref instance);
    public DeviceSession? Session => Volatile.Read(ref session);
    public DemoTransport? Transport { get; private set; }
    public string? ConnectionError { get; private set; }
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate(); Volatile.Write(ref instance, this);
        monitor = new SessionMonitor(this, () => Session, "simulation", 1, true);
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("simulation", "演示设备与任务", NotificationImportance.Low));
        StartForeground(1, SessionMonitor.Create(this, "simulation", true, Session?.Snapshot));
        if (intent?.Action == "pause") { _ = Pause(intent.GetStringExtra("execution")); return StartCommandResult.NotSticky; }
        if (session is null)
        {
            try
            {
                if (DeviceExecutionService.Current?.Session is not null) throw new InvalidOperationException("请先断开 USB 设备");
                var store = new ExecutionStore(Path.Combine(FilesDir!.AbsolutePath, "executions.sqlite")); store.RecoverInterrupted();
                Transport = new DemoTransport(); session = new DeviceSession(Transport, store); session.Run(); ConnectionError = null;
            }
            catch (Exception e) { ConnectionError = e.Message; }
        }
        return StartCommandResult.NotSticky;
    }
    private async Task Pause(string? id)
    { try { if (Guid.TryParse(id, out var execution) && Session is { } s) await s.Pause(execution); } catch (Exception e) { ConnectionError = e.Message; } }
    public void Disconnect() => StopSelf();
    public override void OnDestroy()
    {
        Volatile.Write(ref instance, null); monitor?.Dispose();
        var current = session; session = null;
        if (current is not null) _ = current.DisposeAsync().AsTask();
        base.OnDestroy();
    }
}
