namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>
/// The mice plus their write API, so the commands can be tested against a fake without hardware. Every write
/// returns false when the device is not found, lacks the capability, rejects the value or the write failed.
/// </summary>
internal interface IGearDeviceControl : IGearDeviceSource
{
    /// <summary>Null or empty key: the first device.</summary>
    GearDeviceState? Find(string? key);

    Task<bool> SetPollingRateAsync(string? key, int hz);

    Task<bool> CyclePollingRateAsync(string? key);

    Task<bool> SetDpiAsync(string? key, int dpi);

    Task<bool> StepDpiAsync(string? key, int direction);
}
