namespace LoupixDeck.Plugin.Gear.Devices;

internal interface IGearDevice : IDisposable
{
    string ContainerKey { get; }

    string Key { get; }

    string Name { get; }

    GearLink Link { get; }

    GearCapabilities Capabilities { get; }

    /// <summary>True if the protocol of this model was verified on real hardware.</summary>
    bool Tested { get; }

    IReadOnlyList<int> AllowedPollingRates { get; }

    /// <summary>True if the mouse answers; false while it sleeps or is out of reach.</summary>
    Task<bool> IsAwakeAsync(CancellationToken ct);

    /// <summary>Null if the mouse did not answer.</summary>
    Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct);

    Task<int?> ReadPollingRateAsync(CancellationToken ct);

    Task SetPollingRateAsync(int hz, CancellationToken ct);

    Task<GearDpiState?> ReadDpiAsync(CancellationToken ct);

    Task SetDpiStageAsync(int stage, CancellationToken ct);

    Task SetDpiValueAsync(int dpi, CancellationToken ct);
}
