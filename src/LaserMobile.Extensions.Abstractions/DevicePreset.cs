using System.Text.Json;
using LaserMobile.Domain;
namespace LaserMobile.Extensions.Abstractions;

public sealed record DevicePreset(int SchemaVersion, string Id, string DisplayName, string FirmwareFamily, double Width, double Height, double MaxFeed, double MaxPower)
{
    public static DevicePreset Parse(string json)
    {
        if (json.Length > 64000) throw new FormatException("设备配置超过 64 KB");
        var preset=JsonSerializer.Deserialize<DevicePreset>(json,new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? throw new FormatException("设备配置为空");
        if(preset.SchemaVersion!=1 || string.IsNullOrWhiteSpace(preset.Id) || preset.Id.Length>100 || string.IsNullOrWhiteSpace(preset.DisplayName) || preset.DisplayName.Length>100 || preset.FirmwareFamily!="grbl1.1")
            throw new FormatException("设备配置版本、标识或固件类型不支持");
        preset.Limits.Validate();
        if(preset.Width>10000 || preset.Height>10000 || preset.MaxFeed>100000 || preset.MaxPower>1000000)throw new FormatException("设备配置范围超限");
        return preset;
    }
    public JobLimits Limits=>new(Width,Height,MaxFeed,MaxPower);
}
