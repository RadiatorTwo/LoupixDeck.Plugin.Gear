namespace LoupixDeck.Plugin.Gear.Devices;

/// <remarks>
/// After <see cref="IDisposable.Dispose"/> every command throws <see cref="ObjectDisposedException"/>.
/// A command already in flight is cancelled and ends with <see cref="OperationCanceledException"/> (or its
/// result, if the reply was already in); the connection is closed once that command has let go.
/// </remarks>
internal interface IGearDevice : IDisposable
{
    string ContainerKey { get; }

    /// <summary>
    /// False while the device must not be listed: an empty receiver slot or a paired device that is not a mouse.
    /// The registry still polls it on the normal schedule, so it appears once it becomes present.
    /// </summary>
    bool IsPresent => true;

    /// <summary>Stable identity of the mouse; null until <see cref="RefreshIdentityAsync"/> (or an earlier cached read) knew it.</summary>
    string? Key { get; }

    string Name { get; }

    /// <summary>Name without the vendor prefix for narrow displays.</summary>
    string ShortName => Name;

    GearLink Link { get; }

    GearCapabilities Capabilities { get; }

    /// <summary>True if the protocol of this model was verified on real hardware.</summary>
    bool Tested { get; }

    IReadOnlyList<int> AllowedPollingRates { get; }

    /// <summary>Reads the identity from the device; true when <see cref="Key"/> is known afterwards.</summary>
    Task<bool> RefreshIdentityAsync(CancellationToken ct);

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
