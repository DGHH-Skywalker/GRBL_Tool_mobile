using LaserMobile.Domain;
using LaserMobile.Extensions.Abstractions;
namespace LaserMobile.Extensions.StandardGrbl;

public sealed class StandardGrblExtension : IDeviceExtension
{
    public string Id => "standard-grbl";
    public IReadOnlyList<DeviceProfile> Profiles { get; } = [new("standard-grbl-unknown", "GRBL — awaiting hardware verification", null, null, null, false)];
    public bool Matches(DeviceIdentity identity) => identity.Firmware?.StartsWith("Grbl 1.1", StringComparison.Ordinal) == true;
}
