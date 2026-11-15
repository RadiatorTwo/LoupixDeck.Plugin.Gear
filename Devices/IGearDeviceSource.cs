namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>Read-only view of the known mice, so display code does not depend on the HID-backed registry.</summary>
internal interface IGearDeviceSource
{
    IReadOnlyList<GearDeviceState> Devices { get; }
}
