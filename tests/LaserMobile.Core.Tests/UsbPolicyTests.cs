using LaserMobile.Domain;
using LaserMobile.Extensions.Abstractions;
using Xunit;
namespace LaserMobile.Core.Tests;
public sealed class UsbPolicyTests
{
    private static readonly UsbInterfaceDescriptor[] Cdc = [new(2, 2, 1, false, false), new(10, 0, 0, true, true)];
    [Fact] public void UnknownVidCdcDeviceCanMatchByStandardAcmInterfaces()
    {
        var mapping = new UsbDriverMapping(0x1234, 0x5678, UsbDriverFamily.CdcAcm);
        Assert.True(UsbCompatibility.IsCdcAcm(Cdc)); Assert.True(mapping.IsCandidate(Cdc));
        Assert.False(UsbCompatibility.IsCdcAcm([new(2, 6, 0, false, false), new(10, 0, 0, true, true)]));
        Assert.False(mapping.IsCandidate([new(3, 1, 1, false, false)]));
    }
    [Theory]
    [InlineData(UsbDriverFamily.Ftdi)][InlineData(UsbDriverFamily.Cp21xx)][InlineData(UsbDriverFamily.Ch34x)][InlineData(UsbDriverFamily.Prolific)]
    public void CustomVendorDriversRequireTheRightEndpointShape(UsbDriverFamily driver)
    {
        var mapping = new UsbDriverMapping(0x1A86, 0x0001, driver);
        Assert.True(mapping.IsCandidate([new(255, 0, 0, true, true)]));
        Assert.False(mapping.IsCandidate([new(255, 0, 0, true, false)]));
        Assert.False(mapping.IsCandidate([new(8, 6, 80, true, true)]));
    }
    [Fact] public void PreservedLinesMakeNoControlRequestsAndExplicitLevelsKeepTheirOrder()
    {
        var calls = new List<string>(); var settings = new UsbConnectionSettings();
        settings.ApplyLines(v => calls.Add("DTR=" + v), v => calls.Add("RTS=" + v)); Assert.Empty(calls);
        settings = settings with { Dtr = UsbLineLevel.High, Rts = UsbLineLevel.Low };
        settings.ApplyLines(v => calls.Add("DTR=" + v), v => calls.Add("RTS=" + v));
        Assert.Equal(new[] { "DTR=True", "RTS=False" }, calls);
        Assert.Throws<NotSupportedException>(() => settings.ApplyLines(_ => throw new NotSupportedException(), _ => calls.Add("unexpected")));
        Assert.DoesNotContain("unexpected", calls);
    }
    [Fact] public void SettingsRoundTripAndInvalidMappingsAreRejected()
    {
        var settings = new UsbConnectionSettings(UsbLineLevel.High, UsbLineLevel.Preserve, [new(0x1234, 0x5678, UsbDriverFamily.CdcAcm)]);
        var restored = UsbConnectionSettings.Parse(settings.ToJson()); Assert.Equal(settings.Dtr, restored.Dtr); Assert.Equal(settings.Mappings![0], restored.Mappings![0]);
        Assert.Throws<FormatException>(() => new UsbConnectionSettings((UsbLineLevel)9).Validate());
        Assert.Throws<FormatException>(() => new UsbDriverMapping(0, 4, UsbDriverFamily.Ftdi).Validate());
        Assert.Throws<FormatException>(() => new UsbDriverMapping(2, 65536, UsbDriverFamily.Ftdi).Validate());
        Assert.Throws<FormatException>(() => new UsbConnectionSettings(Mappings: [settings.Mappings[0], settings.Mappings[0]]).Validate());
        Assert.Throws<FormatException>(() => UsbConnectionSettings.Parse("{\"Mappings\":[null]}"));
    }
    [Fact] public void DevicePresetCanCarryUsbDriverAndLineConfiguration()
    {
        var preset = DevicePreset.Parse("""{"SchemaVersion":1,"Id":"test","DisplayName":"Test","FirmwareFamily":"grbl1.1","Width":40,"Height":40,"MaxFeed":1200,"MaxPower":1000,"Usb":{"Dtr":"High","Rts":"Preserve","Mappings":[{"VendorId":4660,"ProductId":22136,"Driver":"CdcAcm"}]}}""");
        Assert.NotNull(preset.Usb); Assert.Equal(UsbLineLevel.High, preset.Usb.Dtr); Assert.Single(preset.Usb.Mappings!);
    }
}
