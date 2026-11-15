namespace LoupixDeck.Plugin.Gear.Devices;

[Flags]
internal enum GearCapabilities
{
    None = 0,
    Battery = 1,
    PollingRate = 2,
    Dpi = 4,
}
