namespace LoupixDeck.Plugin.Gear.Devices;

internal enum GearAvailability
{
    Awake,
    Asleep,
    NoAccess,
    Error,
}

/// <summary>Immutable snapshot of one mouse as last read by the registry.</summary>
/// <param name="Key">Stable identity of the mouse; null while it is not known yet (a sleeping mouse that never answered).</param>
/// <param name="ShortName">Model name without the vendor prefix for the tile header; null falls back to <see cref="Name"/>.</param>
/// <param name="Percent">Last known battery level; kept while the mouse sleeps, null if never read.</param>
/// <param name="LastContact">Time of the last successful read, null if the mouse never answered.</param>
internal sealed record GearDeviceState(
    string? Key,
    string Name,
    GearLink Link,
    GearCapabilities Capabilities,
    bool Tested,
    GearAvailability Availability,
    int? Percent,
    bool Charging,
    int? Millivolts,
    int? PollingRate,
    GearDpiState? Dpi,
    DateTimeOffset? LastContact,
    string? ShortName = null)
{
    /// <summary>The name for narrow displays (no vendor prefix); <see cref="Name"/> when the device has none.</summary>
    public string DisplayName => string.IsNullOrEmpty(ShortName) ? Name : ShortName;
}
