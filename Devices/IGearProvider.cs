using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Devices;

internal interface IGearProvider
{
    /// <summary>Picks the collections this provider can talk to and wraps each in a device.</summary>
    IReadOnlyList<IGearDevice> Match(IReadOnlyList<HidDeviceInfo> devices, IHidTransport transport);
}
