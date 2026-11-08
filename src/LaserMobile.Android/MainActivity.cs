using Android.App;
using Android.OS;
using Android.Content;
using Android.Widget;
using Android.Hardware.Usb;
using SkiaSharp;
using SkiaSharp.Views.Android;
using LaserMobile.AndroidApp.Services;
using Anotherlab.UsbSerialForAndroid.Driver;

namespace LaserMobile.AndroidApp;

[Activity(Label = "GRBL Tool Mobile", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private TextView status = null!;
    private bool visible;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.main);
        status = FindViewById<TextView>(Resource.Id.status)!;
        FindViewById<Button>(Resource.Id.scan)!.Click += async (_, _) =>
        {
            var label = FindViewById<TextView>(Resource.Id.devices)!;
            try
            {
                var manager = (UsbManager)GetSystemService(UsbService)!;
                var drivers = await Task.Run(() => new UsbSerialProber(UsbSerialProber.DefaultProbeTable).FindAllDrivers(manager));
                label.Text = drivers.Count == 0 ? "未发现支持的 USB 串口" : string.Join("\n", drivers.Select(d => $"VID {d.Device.VendorId:X4} · PID {d.Device.ProductId:X4} · {d.Ports.Count} 个端口"));
            }
            catch (Exception e) { label.Text = e.Message; }
        };
        FindViewById<Button>(Resource.Id.simulate)!.Click += (_, _) =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
                RequestPermissions([Android.Manifest.Permission.PostNotifications], 100);
            StartForegroundService(new Intent(this, typeof(SimulationService)));
        };
        var canvas = new SKCanvasView(this);
        canvas.PaintSurface += (_, e) =>
        {
            e.Surface.Canvas.Clear(new SKColor(245, 247, 250));
            using var paint = new SKPaint { Color = new SKColor(25, 110, 180), StrokeWidth = 4, Style = SKPaintStyle.Stroke, IsAntialias = true };
            e.Surface.Canvas.DrawRect(new SKRect(30, 30, e.Info.Width - 30, e.Info.Height - 30), paint);
            e.Surface.Canvas.DrawLine(30, 30, e.Info.Width - 30, e.Info.Height - 30, paint);
        };
        FindViewById<FrameLayout>(Resource.Id.preview)!.AddView(canvas);
    }
    protected override void OnResume() { base.OnResume(); visible = true; Refresh(); }
    protected override void OnPause() { visible = false; handler.RemoveCallbacksAndMessages(null); base.OnPause(); }
    private void Refresh()
    {
        if (!visible) return;
        status.Text = SimulationService.Latest;
        handler.PostDelayed(Refresh, 250);
    }
}
