using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>
/// One Logitech mouse: a receiver slot or a device on a cable. All commands run one at a time under a lock and share
/// the <see cref="HidppLink"/> of their receiver. A slot that is empty, holds a non-mouse or a HID++ 1.0 device is
/// reported through <see cref="IsPresent"/> so the registry hides it.
/// </summary>
internal sealed class LogitechDevice : IGearDevice
{
    private const string VendorPrefix = "Logitech";

    // What the identity read found out about the mouse; replaced as a whole.
    private sealed record Profile(
        LogitechIdentity Identity,
        string MarketingName,
        GearCapabilities Capabilities,
        int[] Rates,
        int[] DpiList,
        ushort? BatteryFeature,
        ushort? RateFeature,
        bool UnifiedPercentage);

    private enum Contact
    {
        Alive,
        Offline,
        SlotEmpty,
        V10Only,
    }

    private readonly HidDeviceInfo _info;
    private readonly HidppLink _link;
    private readonly byte _deviceIndex;
    private readonly bool _viaReceiver;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly BatteryFilter _filter = new();
    private readonly CancellationTokenSource _disposed = new();
    private int _released;
    private volatile Profile? _profile;
    private Dictionary<ushort, byte> _featureIndexes = [];
    private byte _pingData;

    // The mouse did not answer in between: the feature indexes (and, on a receiver, the mouse itself) may differ now.
    private bool _stale;
    private bool _reloadProfile;
    private volatile bool _emptySlot;
    private volatile bool _v10Only;
    private volatile bool _nonPointing;

    public LogitechDevice(HidDeviceInfo info, HidppLink link, byte deviceIndex, bool viaReceiver)
    {
        _info = info;
        _link = link;
        _deviceIndex = deviceIndex;
        _viaReceiver = viaReceiver;
        _link.Acquire();
    }

    public string ContainerKey => _info.ContainerKey;

    public bool IsPresent => !(_emptySlot || _v10Only || _nonPointing);

    public string? Key => _profile is { } p ? DeviceKey.ForLogitech(p.Identity.ModelId, p.Identity.UnitId) : null;

    public string Name => _profile is { MarketingName.Length: > 0 } p ? $"{VendorPrefix} {WithoutVendor(p.MarketingName)}" : $"{VendorPrefix} mouse";

    public string ShortName => _profile is { MarketingName.Length: > 0 } p ? LogitechDecoding.ShortModelName(WithoutVendor(p.MarketingName)) : "mouse";

    public GearLink Link => _profile is not { } p
        ? GearLink.Unknown
        : _viaReceiver ? LogitechDecoding.LinkFromRates(p.Rates) : GearLink.Wired;

    public GearCapabilities Capabilities => _profile?.Capabilities ?? GearCapabilities.None;

    public bool Tested => false;

    public IReadOnlyList<int> AllowedPollingRates => _profile?.Rates ?? [];

    public Task<bool> IsAwakeAsync(CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                Contact contact = await PingAsync(token);
                if (contact == Contact.Alive && _reloadProfile)
                {
                    await TryLoadProfileAsync(token);
                }

                return contact == Contact.Alive;
            },
            ct);
    }

    public Task<bool> RefreshIdentityAsync(CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                if (await PingAsync(token) != Contact.Alive)
                {
                    return false;
                }

                if (_profile is null || _reloadProfile)
                {
                    await TryLoadProfileAsync(token);
                }

                return _profile is not null && !_nonPointing;
            },
            ct);
    }

    public Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct)
    {
        return RunReadAsync<GearPowerReading?>(
            async token =>
            {
                if (_profile is not { BatteryFeature: { } feature } profile)
                {
                    return null;
                }

                switch (feature)
                {
                    case LogitechFeatures.UnifiedBattery:
                        return LogitechDecoding.DecodeUnifiedBattery(await CallAsync(feature, 1, [], token), profile.UnifiedPercentage);
                    case LogitechFeatures.BatteryVoltage:
                        GearPowerReading voltage = LogitechDecoding.DecodeBatteryVoltage(await CallAsync(feature, 0, [], token));
                        if (voltage.Charging)
                        {
                            // The voltage is inflated while charging: forget the history.
                            _filter.Reset();
                            return voltage;
                        }

                        return voltage with { Percent = _filter.Add(voltage.Percent) };
                    default:
                        return LogitechDecoding.DecodeLegacyBattery(await CallAsync(feature, 0, [], token));
                }
            },
            ct);
    }

    public Task<int?> ReadPollingRateAsync(CancellationToken ct) => RunReadAsync(ReadRateCoreAsync, ct);

    public Task SetPollingRateAsync(int hz, CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                if (_profile is not { RateFeature: { } feature } profile || !profile.Rates.Contains(hz))
                {
                    throw new ArgumentOutOfRangeException(nameof(hz), hz, "Polling rate not supported on this link.");
                }

                if (feature == LogitechFeatures.ExtendedReportRate)
                {
                    LogitechDecoding.TryExtendedCodeForHz(hz, out byte code);
                    await SetAsync(feature, 3, [code], token);
                }
                else
                {
                    LogitechDecoding.TryLegacyMsForHz(hz, out byte ms);
                    await SetAsync(feature, 2, [ms], token);
                }

                if (await ReadRateCoreAsync(token) != hz)
                {
                    throw new TimeoutException("The mouse did not acknowledge the polling-rate change.");
                }

                return true;
            },
            ct);
    }

    public Task<GearDpiState?> ReadDpiAsync(CancellationToken ct) => RunReadAsync(ReadDpiCoreAsync, ct);

    public Task SetDpiStageAsync(int stage, CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                GearDpiState state = await ReadDpiCoreAsync(token) ?? throw new TimeoutException("The mouse did not report its DPI.");
                if (stage < 0 || stage >= state.Stages.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, "No such DPI stage.");
                }

                await SetDpiCoreAsync(state.Stages[stage], token);
                return true;
            },
            ct);
    }

    public Task SetDpiValueAsync(int dpi, CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                if (_profile is not { } profile || !LogitechDecoding.TrySnapDpi(profile.DpiList, dpi, out int snapped))
                {
                    throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI outside the range of the sensor.");
                }

                await SetDpiCoreAsync(snapped, token);
                return true;
            },
            ct);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _disposed.Cancel();

        // A request in flight finishes (or is cancelled); the link closes its connections once it lets go.
        _link.Release();
    }

    private static string WithoutVendor(string name)
    {
        return name.StartsWith(VendorPrefix, StringComparison.OrdinalIgnoreCase) ? name[VendorPrefix.Length..].TrimStart() : name;
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposed.Token);
        CancellationToken token = linked.Token;

        await _lock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            return await body(token);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>A read that yields null when the mouse did not give a usable answer (asleep, error reply, timeout).</summary>
    private Task<T?> RunReadAsync<T>(Func<CancellationToken, Task<T?>> body, CancellationToken ct)
    {
        return RunAsync(
            async token =>
            {
                try
                {
                    return await body(token);
                }
                catch (Exception ex) when (ex is TimeoutException or HidppException)
                {
                    return default;
                }
            },
            ct);
    }

    private async Task<Contact> PingAsync(CancellationToken ct)
    {
        byte data = unchecked(++_pingData);
        try
        {
            await SendAsync(0, 1, [0, 0, data], ct);
        }
        catch (HidppException ex) when (ex.IsSlotEmpty)
        {
            return Contact.SlotEmpty;
        }
        catch (HidppException ex) when (ex.IsV10Only)
        {
            return Contact.V10Only;
        }
        catch (HidppException ex) when (ex.IsOffline)
        {
            return Contact.Offline;
        }
        catch (TimeoutException)
        {
            return Contact.Offline;
        }
        catch (HidppException)
        {
            // A HID++ 2.0 error reply still means the device is there.
        }

        _emptySlot = false;
        _v10Only = false;
        if (_stale)
        {
            _stale = false;
            _featureIndexes = [];
            _nonPointing = false;
            _reloadProfile = _profile is not null;
        }

        return Contact.Alive;
    }

    private async Task<byte[]> SendAsync(byte featureIndex, byte function, byte[] parameters, CancellationToken ct)
    {
        try
        {
            return await _link.RequestAsync(_deviceIndex, featureIndex, function, parameters, ct);
        }
        catch (HidppException ex) when (ex.IsV10)
        {
            _emptySlot |= ex.IsSlotEmpty;
            _v10Only |= ex.IsV10Only;
            _stale = true;
            throw;
        }
        catch (TimeoutException)
        {
            _stale = true;
            throw;
        }
    }

    /// <summary>The index of the feature on this device (Root, function 0), or null if the device lacks it.</summary>
    private async Task<byte?> FeatureIndexAsync(ushort featureId, CancellationToken ct)
    {
        if (!_featureIndexes.TryGetValue(featureId, out byte index))
        {
            byte[] reply = await SendAsync(0, 0, [(byte)(featureId >> 8), (byte)featureId, 0], ct);
            index = reply[0];
            _featureIndexes[featureId] = index;
        }

        return index == 0 ? null : index;
    }

    private async Task<byte[]> CallAsync(ushort featureId, byte function, byte[] parameters, CancellationToken ct)
    {
        byte index = await FeatureIndexAsync(featureId, ct)
            ?? throw new NotSupportedException($"The device has no HID++ feature 0x{featureId:x4}.");
        return await SendAsync(index, function, parameters, ct);
    }

    /// <summary>A write: an error reply that says the device refused the value is a rejection, not a device failure.</summary>
    private async Task SetAsync(ushort featureId, byte function, byte[] parameters, CancellationToken ct)
    {
        try
        {
            await CallAsync(featureId, function, parameters, ct);
        }
        catch (HidppException ex) when (ex.IsRejection)
        {
            throw new ArgumentException($"The device rejected the setting: {ex.Message}", ex);
        }
    }

    private async Task TryLoadProfileAsync(CancellationToken ct)
    {
        try
        {
            if (await LoadProfileAsync(ct))
            {
                _reloadProfile = false;
            }
        }
        catch (Exception ex) when (ex is TimeoutException or HidppException)
        {
            // Asleep again or the device is odd: the next contact retries.
        }
    }

    private async Task<bool> LoadProfileAsync(CancellationToken ct)
    {
        string marketingName = string.Empty;
        if (await FeatureIndexAsync(LogitechFeatures.DeviceTypeAndName, ct) is { } nameFeature)
        {
            byte[] type = await SendAsync(nameFeature, 2, [], ct);
            if (!LogitechDecoding.IsPointingDevice(type[0]))
            {
                _nonPointing = true;
                return false;
            }

            marketingName = await ReadNameAsync(nameFeature, ct);
        }

        _nonPointing = false;
        byte[] info = await CallAsync(LogitechFeatures.DeviceInformation, 0, [], ct);
        LogitechIdentity identity = LogitechDecoding.ParseDeviceInformation(info);

        GearCapabilities capabilities = GearCapabilities.None;

        ushort? batteryFeature = null;
        foreach (ushort candidate in new[] { LogitechFeatures.UnifiedBattery, LogitechFeatures.BatteryVoltage, LogitechFeatures.BatteryStatus })
        {
            if (await FeatureIndexAsync(candidate, ct) is not null)
            {
                batteryFeature = candidate;
                capabilities |= GearCapabilities.Battery;
                break;
            }
        }

        bool unifiedPercentage = batteryFeature == LogitechFeatures.UnifiedBattery
            && LogitechDecoding.UnifiedBatteryReportsPercentage(await CallAsync(LogitechFeatures.UnifiedBattery, 0, [], ct));

        ushort? rateFeature = null;
        int[] rates = [];
        if (await FeatureIndexAsync(LogitechFeatures.ExtendedReportRate, ct) is not null)
        {
            rateFeature = LogitechFeatures.ExtendedReportRate;
            rates = LogitechDecoding.ParseExtendedRates(await CallAsync(LogitechFeatures.ExtendedReportRate, 1, [], ct));
        }
        else if (await FeatureIndexAsync(LogitechFeatures.ReportRate, ct) is not null)
        {
            rateFeature = LogitechFeatures.ReportRate;
            rates = LogitechDecoding.ParseLegacyRates(await CallAsync(LogitechFeatures.ReportRate, 0, [], ct));
        }

        if (rates.Length > 0)
        {
            capabilities |= GearCapabilities.PollingRate;
        }

        int[] dpiList = [];
        if (await FeatureIndexAsync(LogitechFeatures.AdjustableDpi, ct) is not null
            && LogitechDecoding.TryParseDpiList(
                (await CallAsync(LogitechFeatures.AdjustableDpi, 1, [0], ct)).AsSpan(1),
                out int[] parsed))
        {
            dpiList = parsed;
            capabilities |= GearCapabilities.Dpi;
        }

        _filter.Reset();
        _profile = new Profile(identity, marketingName, capabilities, rates, dpiList, batteryFeature, rateFeature, unifiedPercentage);
        return true;
    }

    private async Task<string> ReadNameAsync(byte nameFeature, CancellationToken ct)
    {
        const int ChunkLength = HidppProtocol.PayloadLength;
        const int MaxNameLength = 64;

        int length = Math.Min((int)(await SendAsync(nameFeature, 0, [], ct))[0], MaxNameLength);
        List<byte[]> chunks = [];
        for (int offset = 0; offset < length; offset += ChunkLength)
        {
            chunks.Add(await SendAsync(nameFeature, 1, [(byte)offset], ct));
        }

        return LogitechDecoding.AssembleName(length, chunks);
    }

    private async Task<int?> ReadRateCoreAsync(CancellationToken ct)
    {
        if (_profile is not { RateFeature: { } feature })
        {
            return null;
        }

        if (feature == LogitechFeatures.ExtendedReportRate)
        {
            // Connection type: 0 wired, 1 gaming wireless (behind a receiver).
            byte[] reply = await CallAsync(feature, 2, [(byte)(_viaReceiver ? 1 : 0)], ct);
            return LogitechDecoding.TryExtendedHzForCode(reply[0], out int extended) ? extended : null;
        }

        byte[] legacy = await CallAsync(feature, 1, [], ct);
        return LogitechDecoding.TryLegacyHzForMs(legacy[0], out int hz) ? hz : null;
    }

    private async Task<GearDpiState?> ReadDpiCoreAsync(CancellationToken ct)
    {
        if (_profile is not { Capabilities: var capabilities, DpiList: var list } || !capabilities.HasFlag(GearCapabilities.Dpi))
        {
            return null;
        }

        int current = LogitechDecoding.ParseDpi(await CallAsync(LogitechFeatures.AdjustableDpi, 2, [0], ct));
        return LogitechDecoding.BuildDpiState(list, current);
    }

    private async Task SetDpiCoreAsync(int dpi, CancellationToken ct)
    {
        await SetAsync(LogitechFeatures.AdjustableDpi, 3, [0, (byte)(dpi >> 8), (byte)dpi], ct);
        if (LogitechDecoding.ParseDpi(await CallAsync(LogitechFeatures.AdjustableDpi, 2, [0], ct)) != dpi)
        {
            throw new TimeoutException("The mouse did not acknowledge the DPI change.");
        }
    }
}
