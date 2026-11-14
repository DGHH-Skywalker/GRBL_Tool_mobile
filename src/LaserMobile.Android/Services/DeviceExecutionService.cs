using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Anotherlab.UsbSerialForAndroid.Driver;
using LaserMobile.Application;
using LaserMobile.Domain;
using LaserMobile.Persistence;
using LaserMobile.AndroidApp.Usb;

namespace LaserMobile.AndroidApp.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class DeviceExecutionService : Service
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private DeviceSession? session;
    private SessionMonitor? monitor;
    private ExecutionStore store = null!;
    private bool destroyed;
    private static DeviceExecutionService? instance;
    public static DeviceExecutionService? Current => Volatile.Read(ref instance);
    public DeviceSession? Session => Volatile.Read(ref session);
    public override IBinder? OnBind(Intent? intent) => new LocalBinder(this);
    public sealed class LocalBinder(DeviceExecutionService service) : Binder { public DeviceExecutionService Service { get; } = service; }
    public override void OnCreate()
    {
        base.OnCreate(); store = new ExecutionStore(Path.Combine(FilesDir!.AbsolutePath, "executions.sqlite")); store.RecoverInterrupted();
        Volatile.Write(ref instance, this);
        monitor = new SessionMonitor(this, () => Session, "device", 2, false);
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("device", "设备连接与加工", NotificationImportance.Low));
        StartForeground(2, SessionMonitor.Create(this, "device", false, Session?.Snapshot));
        if (intent?.Action == "pause") { _ = Pause(intent.GetStringExtra("execution")); return StartCommandResult.NotSticky; }
        if (intent is not null) _ = Connect(intent.GetIntExtra("device", -1), intent.GetIntExtra("port", 0), intent.GetIntExtra("baud", 115200), intent.GetStringExtra("driver"));
        return StartCommandResult.NotSticky;
    }
    public string? ConnectionError { get; private set; }
    private async Task Connect(int deviceId, int portIndex, int baud, string? expectedDriver)
    {
        await lifecycle.WaitAsync();
        try
        {
            if (destroyed) return;
            if (SimulationService.Current?.Session is not null) throw new InvalidOperationException("请先退出演示模式");
            if (session?.Snapshot.Execution?.State is ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Draining or ExecutionState.Cancelling)
                throw new InvalidOperationException("请先结束当前任务再重新连接");
            if (session is not null) { await session.DisposeAsync(); session = null; }
            var manager = (UsbManager)GetSystemService(UsbService)!;
            var settings = UsbConnectionSettings.Parse(GetSharedPreferences("settings", FileCreationMode.Private)!.GetString("usbSettings", "{}")!);
            var rows = await Task.Run(() => UsbCatalog.Enumerate(manager, settings));
            var row = rows.SingleOrDefault(d => d.Device.DeviceId == deviceId) ?? throw new IOException("USB 设备已断开");
            if (row.Ports.Length == 0) throw new IOException(row.Match);
            if (expectedDriver is not null && row.DriverName != expectedDriver) throw new IOException("USB 驱动映射已变化，请重新扫描并选择端口");
            var selectedPort = row.Ports.SingleOrDefault(p => p.PortNumber == portIndex) ?? throw new IOException("串口索引无效");
            var transport = new UsbTransport(manager, selectedPort, baud, settings);
            session = new DeviceSession(transport, store); session.Run(); ConnectionError = null;

        }
        catch (Exception e) { ConnectionError = e.Message; }
        finally { lifecycle.Release(); }
    }
    private async Task Pause(string? id)
    { try { if (Guid.TryParse(id, out var execution) && Session is { } s) await s.Pause(execution); } catch (Exception e) { ConnectionError = e.Message; } }
    public async Task Disconnect()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (session is not null) { await session.DisposeAsync(); session = null; }

            StopSelf();
        }
        finally { lifecycle.Release(); }
    }
    public override void OnDestroy()
    {
        destroyed = true; Volatile.Write(ref instance, null); monitor?.Dispose();
        _ = Disconnect(); base.OnDestroy();
    }
}
