using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>
/// Finds mice through the providers and keeps their state current.
/// </summary>
/// <remarks>
/// Schedule: the HID tree is enumerated every <see cref="ScanInterval"/> (sends nothing). A new device is read
/// immediately. An awake mouse is read again every <see cref="AwakeInterval"/>; a sleeping or failing one only has
/// its status checked every <see cref="AsleepInterval"/> (no battery query while it sleeps) and is read in full
/// right after it wakes. Devices are tracked by HID path: a path that is still present keeps its device instance,
/// a path that vanished has its instance disposed. All device I/O runs on the thread pool.
/// </remarks>
internal sealed class GearDeviceRegistry(IHidTransport transport, IReadOnlyList<IGearProvider> providers, IPluginLogger logger, TimeProvider time) : IGearDeviceControl, IDisposable
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AwakeInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AsleepInterval = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly List<Entry> _entries = [];
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private IReadOnlyList<GearDeviceState> _snapshot = [];
    private Task? _loop;
    private bool _scanFailed;
    private bool _disposed;

    public IReadOnlyList<GearDeviceState> Devices
    {
        get
        {
            lock (_lock)
            {
                return _snapshot;
            }
        }
    }

    public event Action? Changed;

    /// <summary>Null or empty key: the first device. Otherwise the device with exactly that (non-null) key.</summary>
    public GearDeviceState? Find(string? key)
    {
        IReadOnlyList<GearDeviceState> devices = Devices;
        if (string.IsNullOrEmpty(key))
        {
            return devices.Count > 0 ? devices[0] : null;
        }

        return devices.FirstOrDefault(d => d.Key == key);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _loop != null)
            {
                return;
            }

            _loop = Task.Run(LoopAsync);
        }
    }

    public Task<bool> SetPollingRateAsync(string? key, int hz)
    {
        return WriteAsync(key, GearCapabilities.PollingRate, async (device, ct) =>
        {
            if (!device.AllowedPollingRates.Contains(hz))
            {
                return false;
            }

            await Run(() => device.SetPollingRateAsync(hz, ct), ct);
            return true;
        });
    }

    public Task<bool> CyclePollingRateAsync(string? key)
    {
        return WriteAsync(key, GearCapabilities.PollingRate, async (device, ct) =>
        {
            IReadOnlyList<int> allowed = device.AllowedPollingRates;
            if (allowed.Count == 0)
            {
                return false;
            }

            int? current = await Run(() => device.ReadPollingRateAsync(ct), ct);
            int index = current is null ? -1 : allowed.ToList().IndexOf(current.Value);
            await Run(() => device.SetPollingRateAsync(allowed[(index + 1) % allowed.Count], ct), ct);
            return true;
        });
    }

    public Task<bool> SetDpiAsync(string? key, int dpi)
    {
        return WriteAsync(key, GearCapabilities.Dpi, async (device, ct) =>
        {
            await Run(() => device.SetDpiValueAsync(dpi, ct), ct);
            return true;
        });
    }

    /// <summary>Selects the next (positive) or previous (negative) DPI stage, wrapping around.</summary>
    public Task<bool> StepDpiAsync(string? key, int direction)
    {
        return WriteAsync(key, GearCapabilities.Dpi, async (device, ct) =>
        {
            if (direction == 0)
            {
                return false;
            }

            GearDpiState? dpi = await Run(() => device.ReadDpiAsync(ct), ct);
            if (dpi is null || dpi.Value.Stages.Count == 0)
            {
                return false;
            }

            int count = dpi.Value.Stages.Count;
            int stage = direction > 0 ? PulsarDpi.NextStage(dpi.Value.ActiveStage, count) : PulsarDpi.PreviousStage(dpi.Value.ActiveStage, count);
            await Run(() => device.SetDpiStageAsync(stage, ct), ct);
            return true;
        });
    }

    /// <summary>One scan plus every poll that is due; the background loop calls this every <see cref="ScanInterval"/>.</summary>
    internal async Task TickAsync()
    {
        CancellationToken ct = _cts.Token;
        try
        {
            await _tickGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            bool changed = await ScanAsync(ct);
            Entry[] snapshot;
            lock (_lock)
            {
                snapshot = [.. _entries];
            }

            bool[] results = await Task.WhenAll(snapshot.Select(e => PollIfDueAsync(e, ct)));
            if (changed || results.Any(r => r))
            {
                RaiseChanged();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _tickGate.Release();
        }
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            entries = [.. _entries];
            _entries.Clear();
            _snapshot = [];
        }

        _cts.Cancel();
        foreach (Entry entry in entries)
        {
            DisposeEntry(entry);
        }
    }

    private async Task LoopAsync()
    {
        CancellationToken ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                // Never let the loop die; the next tick tries again.
                logger.Error("Gear: device polling tick failed.", ex);
            }

            try
            {
                await Task.Delay(ScanInterval, time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Reconciles the tracked devices with the HID tree; true if devices were added or removed.</summary>
    private async Task<bool> ScanAsync(CancellationToken ct)
    {
        IReadOnlyList<HidDeviceInfo> infos;
        try
        {
            infos = await Task.Run(transport.Enumerate, ct);
            _scanFailed = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!_scanFailed)
            {
                logger.Error("Gear: enumerating HID devices failed.", ex);
            }

            _scanFailed = true;
            return false;
        }

        HashSet<string> present = [.. infos.Select(i => i.Path)];
        List<Entry> gone;
        HashSet<string> known;
        lock (_lock)
        {
            gone = _entries.Where(e => !present.Contains(e.Path)).ToList();
            _entries.RemoveAll(gone.Contains);
            known = [.. _entries.Select(e => e.Path)];
            if (gone.Count > 0)
            {
                _snapshot = BuildSnapshot();
            }
        }

        foreach (Entry entry in gone)
        {
            DisposeEntry(entry);
        }

        // A new entry has no state yet, so it is not visible before its first poll has finished.
        foreach (HidDeviceInfo info in infos)
        {
            if (known.Contains(info.Path))
            {
                continue;
            }

            IGearDevice? device = MatchOne(info);
            if (device == null)
            {
                continue;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    device.Dispose();
                    return gone.Count > 0;
                }

                _entries.Add(new Entry(info.Path, device));
            }
        }

        return gone.Count > 0;
    }

    // Providers see one collection at a time so the resulting device can be tied to its path.
    private IGearDevice? MatchOne(HidDeviceInfo info)
    {
        foreach (IGearProvider provider in providers)
        {
            try
            {
                IReadOnlyList<IGearDevice> matched = provider.Match([info], transport);
                if (matched.Count == 0)
                {
                    continue;
                }

                for (int i = 1; i < matched.Count; i++)
                {
                    matched[i].Dispose();
                }

                return matched[0];
            }
            catch (Exception ex)
            {
                logger.Error($"Gear: provider {provider.GetType().Name} failed on {info.Path}.", ex);
            }
        }

        return null;
    }

    private async Task<bool> PollIfDueAsync(Entry entry, CancellationToken ct)
    {
        if (entry.Removed || entry.NextPoll > time.GetUtcNow())
        {
            return false;
        }

        // A write in progress refreshes the state itself; skip instead of queueing behind it.
        if (!await entry.Gate.WaitAsync(0, ct))
        {
            return false;
        }

        try
        {
            return await PollAsync(entry, ct);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    // Caller holds entry.Gate.
    private async Task<bool> PollAsync(Entry entry, CancellationToken ct)
    {
        IGearDevice device = entry.Device;
        GearDeviceState? previous = entry.State;
        int? percent = previous?.Percent;
        bool charging = previous?.Charging ?? false;
        int? millivolts = previous?.Millivolts;
        int? rate = previous?.PollingRate;
        GearDpiState? dpi = previous?.Dpi;
        DateTimeOffset? lastContact = previous?.LastContact;
        GearAvailability availability;
        TimeSpan next;
        try
        {
            if (!entry.IdentityTried || device.Key == null)
            {
                entry.IdentityTried = true;
                await Run(() => device.RefreshIdentityAsync(ct), ct);
            }

            bool hasBattery = device.Capabilities.HasFlag(GearCapabilities.Battery);
            GearPowerReading? power = null;
            bool awake = await Run(() => device.IsAwakeAsync(ct), ct);
            if (awake && hasBattery)
            {
                power = await Run(() => device.ReadPowerAsync(ct), ct);
                awake = power != null;
            }

            if (awake)
            {
                if (power is { } reading)
                {
                    percent = reading.Percent;
                    charging = reading.Charging;
                    millivolts = reading.Millivolts;
                }

                if (device.Capabilities.HasFlag(GearCapabilities.PollingRate))
                {
                    rate = await Run(() => device.ReadPollingRateAsync(ct), ct) ?? rate;
                }

                if (device.Capabilities.HasFlag(GearCapabilities.Dpi))
                {
                    dpi = await Run(() => device.ReadDpiAsync(ct), ct) ?? dpi;
                }

                availability = GearAvailability.Awake;
                next = AwakeInterval;
                lastContact = time.GetUtcNow();
            }
            else
            {
                availability = GearAvailability.Asleep;
                next = AsleepInterval;
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested || entry.Removed || ex is ObjectDisposedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            availability = ClassifyFailure(entry, ex);
            next = AsleepInterval;
        }

        entry.NextPoll = time.GetUtcNow() + next;
        return Update(entry, Build(entry, availability, percent, charging, millivolts, rate, dpi, lastContact));
    }

    private async Task<bool> WriteAsync(string? key, GearCapabilities required, Func<IGearDevice, CancellationToken, Task<bool>> command)
    {
        Entry? entry = FindEntry(key);
        if (entry == null || !entry.Device.Capabilities.HasFlag(required))
        {
            return false;
        }

        CancellationToken ct = _cts.Token;
        bool ok;
        bool changed;
        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            if (entry.Removed)
            {
                return false;
            }

            try
            {
                ok = await command(entry.Device, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested || entry.Removed || ex is ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                GearAvailability availability = ClassifyFailure(entry, ex);
                GearDeviceState? p = entry.State;
                ok = false;
                entry.NextPoll = time.GetUtcNow() + AsleepInterval;
                changed = Update(entry, Build(entry, availability, p?.Percent, p?.Charging ?? false, p?.Millivolts, p?.PollingRate, p?.Dpi, p?.LastContact));
                if (changed)
                {
                    RaiseChanged();
                }

                return false;
            }

            changed = ok && await RefreshAfterWriteAsync(entry, ct).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }

        if (changed)
        {
            RaiseChanged();
        }

        return ok;
    }

    // Reads back what a write may have changed. A failing read-back does not fail the write.
    private async Task<bool> RefreshAfterWriteAsync(Entry entry, CancellationToken ct)
    {
        IGearDevice device = entry.Device;
        GearDeviceState? previous = entry.State;
        int? rate = previous?.PollingRate;
        GearDpiState? dpi = previous?.Dpi;
        try
        {
            if (device.Capabilities.HasFlag(GearCapabilities.PollingRate))
            {
                rate = await Run(() => device.ReadPollingRateAsync(ct), ct) ?? rate;
            }

            if (device.Capabilities.HasFlag(GearCapabilities.Dpi))
            {
                dpi = await Run(() => device.ReadDpiAsync(ct), ct) ?? dpi;
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && !entry.Removed)
            {
                logger.Warn($"Gear: reading back {device.Name} after a write failed: {ex.Message}");
            }

            return false;
        }

        return Update(entry, Build(entry, GearAvailability.Awake, previous?.Percent, previous?.Charging ?? false, previous?.Millivolts, rate, dpi, time.GetUtcNow()));
    }

    private GearAvailability ClassifyFailure(Entry entry, Exception ex)
    {
        GearAvailability availability = ex is HidAccessException ? GearAvailability.NoAccess : GearAvailability.Error;

        // Log once per transition into a failure state, not on every retry.
        if (entry.State?.Availability != availability)
        {
            if (availability == GearAvailability.NoAccess)
            {
                logger.Warn($"Gear: no permission to open {entry.Device.Name}: {ex.Message}");
            }
            else
            {
                logger.Error($"Gear: {entry.Device.Name} failed.", ex);
            }
        }

        return availability;
    }

    private static GearDeviceState Build(Entry entry, GearAvailability availability, int? percent, bool charging, int? millivolts, int? rate, GearDpiState? dpi, DateTimeOffset? lastContact)
    {
        IGearDevice d = entry.Device;
        return new GearDeviceState(d.Key, d.Name, d.Link, d.Capabilities, d.Tested, availability, percent, charging, millivolts, rate, dpi, lastContact, d.ShortName);
    }

    // Stores the state unless the entry was removed meanwhile; true if the visible state changed.
    private bool Update(Entry entry, GearDeviceState state)
    {
        lock (_lock)
        {
            if (entry.Removed || _disposed)
            {
                return false;
            }

            bool changed = entry.State != state;
            entry.State = state;
            _snapshot = BuildSnapshot();
            return changed;
        }
    }

    // Caller holds _lock.
    private GearDeviceState[] BuildSnapshot() => _entries.Where(e => e.State != null).Select(e => e.State!).ToArray();

    private Entry? FindEntry(string? key)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.State != null && (string.IsNullOrEmpty(key) || e.Device.Key == key));
        }
    }

    private void DisposeEntry(Entry entry)
    {
        entry.Removed = true;
        try
        {
            entry.Device.Dispose();
        }
        catch (Exception ex)
        {
            logger.Warn($"Gear: disposing {entry.Path} failed: {ex.Message}");
        }
    }

    private void RaiseChanged()
    {
        Action? handlers = Changed;
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception ex)
            {
                logger.Error("Gear: a Changed handler threw.", ex);
            }
        }
    }

    // Device commands may block the calling thread before their first await.
    private static Task<T> Run<T>(Func<Task<T>> work, CancellationToken ct) => Task.Run(work, ct);

    private static Task Run(Func<Task> work, CancellationToken ct) => Task.Run(work, ct);

    private sealed class Entry(string path, IGearDevice device)
    {
        public string Path { get; } = path;

        public IGearDevice Device { get; } = device;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile bool Removed;

        // Replaced under the registry lock; null until the first poll has finished.
        public GearDeviceState? State;

        public DateTimeOffset NextPoll = DateTimeOffset.MinValue;

        public bool IdentityTried;
    }
}
