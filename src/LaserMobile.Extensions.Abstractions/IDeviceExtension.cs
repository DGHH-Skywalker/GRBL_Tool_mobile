using LaserMobile.Domain;
namespace LaserMobile.Extensions.Abstractions;

public interface IDeviceExtension
{
    string Id { get; }
    IReadOnlyList<DeviceProfile> Profiles { get; }
    bool Matches(DeviceIdentity identity);
}
