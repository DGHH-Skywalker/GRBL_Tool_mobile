using Android.Hardware.Usb;
using Android.Widget;
using LaserMobile.AndroidApp.Usb;
using LaserMobile.Domain;
namespace LaserMobile.AndroidApp;

public sealed partial class MainActivity
{
    private UsbCatalogEntry[] usbInventory = [];
    private Button dtrButton = null!, rtsButton = null!;
    private UsbConnectionSettings ReadUsbSettings() => UsbConnectionSettings.Parse(preferences.GetString("usbSettings", "{}")!);
    private void SaveUsbSettings(UsbConnectionSettings value)
    {
        value.Validate(); using var edit = preferences.Edit(); edit!.PutString("usbSettings", value.ToJson()); edit.Apply();
        if (dtrButton is not null) dtrButton.Text = "DTR · " + LineName(value.Dtr);
        if (rtsButton is not null) rtsButton.Text = "RTS · " + LineName(value.Rts);
    }
    private static string LineName(UsbLineLevel level) => level switch { UsbLineLevel.Low => "拉低", UsbLineLevel.High => "拉高", _ => "保持" };
    private void ChooseLine(bool dtr)
    {
        var value = ReadUsbSettings();
        Choices(dtr ? "DTR · 下次连接应用" : "RTS · 下次连接应用", ["保持", "拉低", "拉高"], index =>
        { SaveUsbSettings(dtr ? value with { Dtr = (UsbLineLevel)index } : value with { Rts = (UsbLineLevel)index }); return Task.CompletedTask; }, (int)(dtr ? value.Dtr : value.Rts));
    }
    private async Task ShowUsbInventory()
    {
        await Scan();
        if (usbInventory.Length > 4)
        {
            Choices($"USB {usbInventory.Length} 个 · 串口 {devices.Count} 个", usbInventory.Select(e => $"{e.Device.VendorId:X4}:{e.Device.ProductId:X4} · {(e.Ports.Length > 0 ? "已匹配" : "未匹配")}").ToArray(), index => { ShowUsbDevice(usbInventory[index]); return Task.CompletedTask; }); return;
        }
        var screen = OpenPanel("USB 设备诊断");
        Label(screen, $"Android 已枚举 {usbInventory.Length} 个 USB 设备 · 串口 {devices.Count} 个", 14, 2);
        Label(screen, $"下次连接：DTR {LineName(ReadUsbSettings().Dtr)} / RTS {LineName(ReadUsbSettings().Rts)}", 12, 2);
        if (usbInventory.Length == 0) Label(screen, "Android 当前未枚举到 USB 设备，请检查 OTG 数据连接。", 14, 3);
        foreach (var row in usbInventory)
        {
            var value = row;
            Button(screen, $"VID {row.Device.VendorId:X4} / PID {row.Device.ProductId:X4} · {(row.Ports.Length > 0 ? "已匹配" : "未匹配")}", () => { ShowUsbDevice(value); return Task.CompletedTask; });
        }
        Button(screen, "重新扫描", ShowUsbInventory); Button(screen, "返回", () => { ClosePanel(); return Task.CompletedTask; });
    }
    private void ShowUsbDevice(UsbCatalogEntry entry)
    {
        var d = entry.Device;
        string description = $"VID 0x{d.VendorId:X4} · PID 0x{d.ProductId:X4}\n设备类别 {(int)d.DeviceClass:X2}\n{entry.Match}\n";
        description += string.Join('\n', entry.Interfaces.Select((face, index) => $"接口 {index} · Class {face.Class:X2} / Sub {face.Subclass:X2} / Proto {face.Protocol:X2} · Bulk IN {(face.BulkIn ? "有" : "无")} / OUT {(face.BulkOut ? "有" : "无")}"));
        ShowPagedText($"USB {d.VendorId:X4}:{d.ProductId:X4}", description, screen =>
        {
        var families = Enum.GetValues<UsbDriverFamily>().Where(f => new UsbDriverMapping(d.VendorId, d.ProductId, f).IsCandidate(entry.Interfaces)).ToArray();
        if (families.Length > 0)
            Button(screen, "自定义驱动映射", () => { Choices("选择兼容串口驱动", families.Select(f => f.ToString()).ToArray(), async index =>
            {
                var settings = ReadUsbSettings(); var mappings = (settings.Mappings ?? []).Where(m => m.VendorId != d.VendorId || m.ProductId != d.ProductId).Append(new UsbDriverMapping(d.VendorId, d.ProductId, families[index])).ToArray();
                SaveUsbSettings(settings with { Mappings = mappings }); await ShowUsbInventory();
            }); return Task.CompletedTask; });
        else Label(screen, "接口不符合当前串口驱动的候选条件。", 12, 2);
        Button(screen, "恢复内置匹配", async () => { var settings = ReadUsbSettings(); SaveUsbSettings(settings with { Mappings = (settings.Mappings ?? []).Where(m => m.VendorId != d.VendorId || m.ProductId != d.ProductId).ToArray() }); await ShowUsbInventory(); });
        }, ShowUsbInventory);
    }
}
