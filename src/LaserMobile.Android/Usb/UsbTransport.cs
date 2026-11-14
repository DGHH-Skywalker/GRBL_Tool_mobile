using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using LaserMobile.Domain;
using System.Runtime.CompilerServices;
namespace LaserMobile.AndroidApp.Usb;

public sealed class UsbTransport : IByteTransport
{
    private readonly UsbSerialPort port;
    private readonly UsbDeviceConnection connection;
    private bool disposed;
    public UsbTransport(UsbManager manager, UsbSerialPort port, int baud, UsbConnectionSettings? settings = null)
    {
        if (!manager.HasPermission(port.Driver.Device)) throw new UnauthorizedAccessException("USB 权限尚未授予");
        if (baud is < 1200 or > 2000000) throw new ArgumentOutOfRangeException(nameof(baud));
        settings ??= new UsbConnectionSettings(); settings.Validate();
        this.port = port;
        connection = manager.OpenDevice(port.Driver.Device) ?? throw new IOException("无法打开 USB 设备");
        bool opened = false;
        try
        {
            port.Open(connection); opened = true; port.SetParameters(baud, 8, StopBits.One, Parity.None);
            settings.ApplyLines(value => SetLine("DTR", () => port.SetDTR(value)), value => SetLine("RTS", () => port.SetRTS(value)));
        }
        catch { if (opened) { try { port.Close(); } catch (Exception) { } } connection.Close(); connection.Dispose(); throw; }
    }
    private static void SetLine(string name, Action set)
    { try { set(); } catch (Exception e) { throw new IOException($"{name} 设置失败：{e.Message}", e); } }
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var copy = bytes.ToArray();
        var count = await Task.Run(() => port.Write(copy, 500), cancellationToken);
        if (count != copy.Length) throw new IOException("串口写入不完整，执行位置待确认");
    }
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested && !disposed)
        {
            int count = await Task.Run(() => port.Read(buffer, 200), cancellationToken);
            if (count > 0) yield return buffer.AsMemory(0, count).ToArray();
        }
    }
    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask; disposed = true;
        try { port.Close(); } catch (Exception) { } finally { connection.Close(); connection.Dispose(); }
        return ValueTask.CompletedTask;
    }
}
