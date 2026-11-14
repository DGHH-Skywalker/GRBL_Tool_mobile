using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using LaserMobile.Domain;
namespace LaserMobile.AndroidApp.Usb;

public sealed record UsbCatalogEntry(UsbDevice Device, UsbInterfaceDescriptor[] Interfaces, string Match, string DriverName, UsbSerialPort[] Ports);
public static class UsbCatalog
{
    public static UsbCatalogEntry[] Enumerate(UsbManager manager, UsbConnectionSettings settings)
    {
        settings.Validate(); var defaults = new UsbSerialProber(UsbSerialProber.DefaultProbeTable);
        return manager.DeviceList!.Values.Select(device =>
        {
            var descriptors = new List<UsbInterfaceDescriptor>();
            for (int i = 0; i < device.InterfaceCount; i++)
            {
                var face = device.GetInterface(i)!; bool input = false, output = false;
                for (int e = 0; e < face.EndpointCount; e++) { var ep = face.GetEndpoint(e)!; if ((int)ep.Type == 2) { input |= (int)ep.Direction == 128; output |= (int)ep.Direction == 0; } }
                descriptors.Add(new((int)face.InterfaceClass, (int)face.InterfaceSubclass, face.InterfaceProtocol, input, output));
            }
            string match = "未匹配串口驱动"; string driverName = ""; UsbSerialPort[] ports = [];
            try
            {
                var mapping = settings.Mappings?.SingleOrDefault(m => m.VendorId == device.VendorId && m.ProductId == device.ProductId);
                if (mapping is not null)
                {
                    if (!mapping.IsCandidate(descriptors)) return new UsbCatalogEntry(device, descriptors.ToArray(), "自定义映射与接口类型不符", "", []);
                    // Use typed constructors so optional upstream parameters never depend on reflection binding.
                    ports = MappedPorts(device, mapping.Driver); driverName = DriverType(mapping.Driver).Name;
                    return new UsbCatalogEntry(device, descriptors.ToArray(), $"自定义映射 · {driverName} · {ports.Length} 端口", driverName, ports);
                }
                var driver = defaults.ProbeDevice(device);
                if (driver is null && mapping is null && UsbCompatibility.IsCdcAcm(descriptors))
                { driver = new CdcAcmSerialDriver(device); match = "标准 CDC-ACM 接口匹配"; }
                else if (driver is not null && mapping is null) match = "内置 VID/PID 匹配";
                if (driver is not null) { ports = driver.Ports.ToArray(); driverName = driver.GetType().Name; match += $" · {driverName} · {ports.Length} 端口"; }
            }
            catch (Exception e) { match = "驱动匹配失败：" + e.Message; }
            return new UsbCatalogEntry(device, descriptors.ToArray(), match, driverName, ports);
        }).OrderBy(e => e.Device.VendorId).ThenBy(e => e.Device.ProductId).ToArray();
    }
    private static UsbSerialPort[] MappedPorts(UsbDevice device, UsbDriverFamily driver) => driver switch
    {
        UsbDriverFamily.CdcAcm => new CdcAcmSerialDriver(device).Ports.ToArray(), UsbDriverFamily.Ftdi => new FtdiSerialDriver(device).Ports.ToArray(),
        UsbDriverFamily.Cp21xx => new Cp21xxSerialDriver(device).Ports.ToArray(), UsbDriverFamily.Ch34x => new Ch34xSerialDriver(device).Ports.ToArray(),
        UsbDriverFamily.Prolific => new ProlificSerialDriver(device).Ports.ToArray(), _ => throw new FormatException("不支持的串口驱动")
    };
    private static Type DriverType(UsbDriverFamily family) => family switch
    {
        UsbDriverFamily.CdcAcm => typeof(CdcAcmSerialDriver), UsbDriverFamily.Ftdi => typeof(FtdiSerialDriver),
        UsbDriverFamily.Cp21xx => typeof(Cp21xxSerialDriver), UsbDriverFamily.Ch34x => typeof(Ch34xSerialDriver),
        UsbDriverFamily.Prolific => typeof(ProlificSerialDriver), _ => throw new FormatException("不支持的串口驱动")
    };
}
