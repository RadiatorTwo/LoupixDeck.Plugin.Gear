using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Tests.Devices;

public sealed class DeviceKeyTests
{
    [Fact]
    public void ForLogitech_IsLowercaseModelAndUnitId()
    {
        Assert.Equal("logitech:model:4082:a1b2c3d4", DeviceKey.ForLogitech(0x4082, [0xA1, 0xB2, 0xC3, 0xD4]));
        Assert.Equal("logitech:model:000a:00000001", DeviceKey.ForLogitech(0x000A, [0, 0, 0, 1]));
    }
}
