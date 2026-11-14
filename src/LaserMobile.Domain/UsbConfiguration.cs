using System.Text.Json;
using System.Text.Json.Serialization;
namespace LaserMobile.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<UsbDriverFamily>))]
public enum UsbDriverFamily { CdcAcm, Ftdi, Cp21xx, Ch34x, Prolific }
[JsonConverter(typeof(JsonStringEnumConverter<UsbLineLevel>))]
public enum UsbLineLevel { Preserve, Low, High }
public sealed record UsbInterfaceDescriptor(int Class, int Subclass, int Protocol, bool BulkIn, bool BulkOut);
public sealed record UsbDriverMapping(int VendorId, int ProductId, UsbDriverFamily Driver)
{
    public void Validate()
    { if (VendorId is < 1 or > 65535 || ProductId is < 0 or > 65535 || !Enum.IsDefined(Driver)) throw new FormatException("USB 映射的 VID/PID 或驱动类型无效"); }
    public bool IsCandidate(IReadOnlyList<UsbInterfaceDescriptor> interfaces)
    {
        Validate();
        if (Driver == UsbDriverFamily.CdcAcm) return UsbCompatibility.IsCdcAcm(interfaces);
        return interfaces.Any(i => i.Class == 255 && i.BulkIn && i.BulkOut);
    }
}
public sealed record UsbConnectionSettings(UsbLineLevel Dtr = UsbLineLevel.Preserve, UsbLineLevel Rts = UsbLineLevel.Preserve, IReadOnlyList<UsbDriverMapping>? Mappings = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Dtr) || !Enum.IsDefined(Rts)) throw new FormatException("DTR/RTS 配置无效");
        if (Mappings is not { } maps) return;
        if (maps.Count > 32) throw new FormatException("USB 映射超过 32 项");
        foreach (var map in maps) { if (map is null) throw new FormatException("USB 映射为空"); map.Validate(); }
        if (maps.Select(m => (m.VendorId, m.ProductId)).Distinct().Count() != maps.Count) throw new FormatException("USB 映射重复");
    }
    public void ApplyLines(Action<bool> dtr, Action<bool> rts)
    {
        Validate(); if (Dtr != UsbLineLevel.Preserve) dtr(Dtr == UsbLineLevel.High);
        if (Rts != UsbLineLevel.Preserve) rts(Rts == UsbLineLevel.High);
    }
    public static UsbConnectionSettings Parse(string json)
    {
        if (json.Length > 64000) throw new FormatException("USB 配置过大");
        var value = JsonSerializer.Deserialize<UsbConnectionSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new FormatException("USB 配置为空"); value.Validate(); return value;
    }
    public string ToJson() { Validate(); return JsonSerializer.Serialize(this); }
}
public static class UsbCompatibility
{
    public static bool IsCdcAcm(IReadOnlyList<UsbInterfaceDescriptor> interfaces) => interfaces.Any(i => i.Class == 2 && i.Subclass == 2) && interfaces.Any(i => i.Class == 10 && i.BulkIn && i.BulkOut);
}
