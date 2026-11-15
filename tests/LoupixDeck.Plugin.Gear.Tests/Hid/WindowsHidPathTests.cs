using LoupixDeck.Plugin.Gear.Hid.Windows;

namespace LoupixDeck.Plugin.Gear.Tests.Hid;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsHidPathTests
{
    private const string Col05 = @"\\?\hid#vid_3710&pid_5406&mi_01&col05#a&337d08&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string Col01 = @"\\?\hid#vid_3710&pid_5406&mi_01&col01#a&337d08&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void Interface_number_is_read_from_the_path()
    {
        Assert.Equal(1, WindowsHidTransport.ParseInterfaceNumber(Col05));
        Assert.Equal(-1, WindowsHidTransport.ParseInterfaceNumber(@"\\?\hid#vid_1&pid_2#a&b&0&0000#{x}"));
    }

    [Fact]
    public void Collections_of_one_interface_share_a_container_key()
    {
        Assert.Equal(WindowsHidTransport.ParseContainerKey(Col01), WindowsHidTransport.ParseContainerKey(Col05));
    }
}
