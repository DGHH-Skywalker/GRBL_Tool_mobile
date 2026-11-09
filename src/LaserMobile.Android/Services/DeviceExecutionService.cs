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
    private PowerManager.WakeLock? wake;
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
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("device", "设备连接与加工", NotificationImportance.Low));
        using var open = new Intent(this, typeof(MainActivity));
        using var pending = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(this, "device").SetContentTitle("GRBL 设备会话")
            .SetContentText("点击查看连接、任务和加工状态").SetContentIntent(pending)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).SetOngoing(true).Build();
        StartForeground(2, notification);
        if (intent is not null) _ = Connect(intent.GetIntExtra("device", -1), intent.GetIntExtra("port", 0), intent.GetIntExtra("baud", 115200));
        return StartCommandResult.NotSticky;
    }
    public string? ConnectionError { get; private set; }
    private async Task Connect(int deviceId, int portIndex, int baud)
    {
        await lifecycle.WaitAsync();
        try
        {
            if (destroyed) return;
            if (session?.Snapshot.Execution?.State is ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Draining or ExecutionState.Cancelling)
                throw new InvalidOperationException("请先结束当前任务再重新连接");
            if (session is not null) { await session.DisposeAsync(); session = null; }
            var manager = (UsbManager)GetSystemService(UsbService)!;
            var drivers = await Task.Run(() => new UsbSerialProber(UsbSerialProber.DefaultProbeTable).FindAllDrivers(manager));
            var driver = drivers.SingleOrDefault(d => d.Device.DeviceId == deviceId) ?? throw new IOException("USB 设备已断开");
            if (portIndex < 0 || portIndex >= driver.Ports.Count) throw new IOException("串口索引无效");
            var transport = new UsbTransport(manager, driver.Ports[portIndex], baud);
            session = new DeviceSession(transport, store); session.Run(); ConnectionError = null;
            wake?.Release(); wake?.Dispose();
            wake = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "GRBL:DeviceSession");
            wake!.Acquire();
        }
        catch (Exception e) { ConnectionError = e.Message; }
        finally { lifecycle.Release(); }
    }
    public async Task Disconnect()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (session is not null) { await session.DisposeAsync(); session = null; }
            if (wake?.IsHeld == true) wake.Release(); wake?.Dispose(); wake = null;
            StopSelf();
        }
        finally { lifecycle.Release(); }
    }
    public override void OnDestroy()
    {
        destroyed = true; Volatile.Write(ref instance, null);
        _ = Disconnect(); base.OnDestroy();
    }
}
