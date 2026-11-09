using Android.App;
using Android.Content;
using Android.Hardware.Usb;
namespace LaserMobile.AndroidApp.Usb;

[BroadcastReceiver(Exported = false)]
public sealed class UsbPermissionReceiver : BroadcastReceiver
{
    internal static event Action<int, bool>? Result;
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Action) return;
        Result?.Invoke(intent.GetIntExtra("deviceId", -1), intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false));
    }
    public const string Action = "io.github.dghhskywalker.grbltoolmobile.USB_PERMISSION";
    public static async Task<bool> Request(Activity activity, UsbManager manager, UsbDevice device)
    {
        if (manager.HasPermission(device)) return true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(int id, bool granted) { if (id == device.DeviceId) completion.TrySetResult(granted); }
        Result += Handler;
        try
        {
            using var intent = new Intent(activity, typeof(UsbPermissionReceiver)); intent.SetAction(Action); intent.PutExtra("deviceId", device.DeviceId);
            using var pending = PendingIntent.GetBroadcast(activity, device.DeviceId, intent, (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0) | PendingIntentFlags.UpdateCurrent)!;
            manager.RequestPermission(device, pending);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally { Result -= Handler; }
    }
}
