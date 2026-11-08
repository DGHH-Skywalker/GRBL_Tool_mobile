using LaserMobile.Domain;
using LaserMobile.Extensions.Abstractions;
namespace LaserMobile.Extensions.Xingguang;

public sealed class XingguangExtension : IDeviceExtension
{
    public string Id => "xingguang";
    public IReadOnlyList<DeviceProfile> Profiles { get; } = [new("xingguang-unverified", "星光 — 待确认板号与固件", null, null, null, false)];
    public bool Matches(DeviceIdentity identity) => false; // USB bridge identity alone is not a machine identity.
}
