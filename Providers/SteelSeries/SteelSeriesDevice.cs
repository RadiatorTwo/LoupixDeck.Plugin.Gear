using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.SteelSeries;

/// <summary>
/// A SteelSeries mouse behind its vendor HID collection. All commands run one at a time under a lock; the connection
/// is opened on first use.
/// </summary>
/// <remarks>
/// These mice cannot report their polling rate or DPI. After Gear has set one, the device reports the value it wrote;
/// before that, and after a replug (a new device instance), the value is unknown. A change made elsewhere (vendor
/// app, DPI button) is not seen. Writes are not saved to the mouse's memory, so a replug restores its own settings.
/// </remarks>
internal sealed class SteelSeriesDevice : IGearDevice
{
    private const int Attempts = 2;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(500);

    // rivalcfg waits this long after every command before the next one.
    private static readonly TimeSpan CommandDelay = TimeSpan.FromMilliseconds(50);

    // Long enough for an already queued report to be delivered (a zero timeout would cancel the Windows read before it is issued).
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMilliseconds(1);
    private const int MaxDrainedReports = 64;

    private readonly HidDeviceInfo _info;
    private readonly IHidTransport _transport;
    private readonly SteelSeriesModel _model;
    private readonly int _payloadOffset;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly BatteryFilter _filter = new();
    private readonly CancellationTokenSource _disposed = new();
    private IHidConnection? _connection;
    private volatile int _pollingRate;
    private volatile int _dpi;

    /// <param name="readIncludesReportId">
    /// True where a read returns the report id 0 in front of the payload (Windows); hidraw leaves it out for devices
    /// without numbered reports.
    /// </param>
    public SteelSeriesDevice(HidDeviceInfo info, IHidTransport transport, SteelSeriesModel model, bool? readIncludesReportId = null)
    {
        _info = info;
        _transport = transport;
        _model = model;
        _payloadOffset = (readIncludesReportId ?? OperatingSystem.IsWindows()) ? 1 : 0;
        AllowedPollingRates = model.Profile.PollingCodes.Keys.Order().ToArray();
    }

    public string ContainerKey => _info.ContainerKey;

    public string? Key => DeviceKey.ForSteelSeries(_model.ProductId, _info.SerialNumber);

    public string Name => _model.Name;

    public string ShortName => SteelSeriesModels.ShortName(Name);

    public GearLink Link => _model.Link;

    public GearCapabilities Capabilities => GearCapabilities.PollingRate | GearCapabilities.Dpi
        | (_model.Profile.Battery != SteelSeriesBattery.None ? GearCapabilities.Battery : GearCapabilities.None);

    public bool Tested => _model.Tested;

    public IReadOnlyList<int> AllowedPollingRates { get; }

    // The identity is the model plus the USB serial: nothing to ask the mouse.
    public Task<bool> RefreshIdentityAsync(CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Opens the connection. A wired mouse is awake when it can be opened; a wireless one is judged by its battery reply.
    /// </summary>
    public async Task<bool> IsAwakeAsync(CancellationToken ct)
    {
        await RunAsync(_ => true, ct);
        return true;
    }

    public async Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct)
    {
        SteelSeriesProfile profile = _model.Profile;
        if (profile.Battery == SteelSeriesBattery.None)
        {
            return null;
        }

        return await RunAsync(connection =>
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                byte[]? payload = Exchange(connection, profile.BatteryCommand, ct);
                if (payload is not null && SteelSeriesReplies.TryParseBattery(profile.Battery, payload, out int percent, out bool charging))
                {
                    return new GearPowerReading(_filter.Add(percent), charging, null);
                }
            }

            // A cancelled read looks like a timeout; do not report it as "asleep".
            ct.ThrowIfCancellationRequested();
            _filter.Reset();
            return (GearPowerReading?)null;
        }, ct);
    }

    public Task<int?> ReadPollingRateAsync(CancellationToken ct) => Task.FromResult(_pollingRate == 0 ? null : (int?)_pollingRate);

    public async Task SetPollingRateAsync(int hz, CancellationToken ct)
    {
        if (!_model.Profile.PollingCodes.TryGetValue(hz, out byte code))
        {
            throw new ArgumentOutOfRangeException(nameof(hz), hz, "Polling rate not supported by this mouse.");
        }

        await WriteAsync([[.. _model.Profile.PollingCommand, code]], ct);
        _pollingRate = hz;
    }

    /// <summary>The DPI Gear last set, as the single active stage; null before the first write.</summary>
    public Task<GearDpiState?> ReadDpiAsync(CancellationToken ct) =>
        Task.FromResult(_dpi == 0 ? null : (GearDpiState?)new GearDpiState([_dpi], 0));

    /// <summary>Only the one stage Gear knows exists: selecting it writes its DPI again.</summary>
    public async Task SetDpiStageAsync(int stage, CancellationToken ct)
    {
        if (stage != 0 || _dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "No such DPI stage.");
        }

        await SetDpiValueAsync(_dpi, ct);
    }

    /// <summary>Makes the mouse run at <paramref name="dpi"/>, snapped to the nearest value its sensor supports.</summary>
    public async Task SetDpiValueAsync(int dpi, CancellationToken ct)
    {
        (IReadOnlyList<byte[]> packets, int actual) = _model.Profile.Dpi.Build(dpi);
        await WriteAsync(packets, ct);
        _dpi = actual;
    }

    public void Dispose()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        _disposed.Cancel();

        // A command in flight holds the lock and the connection; it closes the connection when it lets go.
        if (_lock.Wait(0))
        {
            try
            {
                CloseConnection();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private Task WriteAsync(IReadOnlyList<byte[]> packets, CancellationToken ct)
    {
        return RunAsync(connection =>
        {
            foreach (byte[] packet in packets)
            {
                ct.ThrowIfCancellationRequested();
                Drain(connection);
                connection.WriteReport([0x00, .. packet]);
                Thread.Sleep(CommandDelay);
            }

            return true;
        }, ct);
    }

    /// <summary>Runs <paramref name="work"/> on the thread pool under the command lock with an open connection.</summary>
    private async Task<T> RunAsync<T>(Func<IHidConnection, T> work, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposed.Token);
        CancellationToken token = linked.Token;

        await _lock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            try
            {
                IHidConnection connection = _connection ??= _transport.Open(_info);
                return await Task.Run(() => work(connection), CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed handle stays dead after a replug or re-enumeration: drop it so the next command reopens.
                CloseConnection();
                throw;
            }
        }
        finally
        {
            _lock.Release();
            if (_disposed.IsCancellationRequested && _lock.Wait(0))
            {
                CloseConnection();
                _lock.Release();
            }
        }
    }

    /// <summary>Sends a query and returns the payload of the first report that comes back; null on timeout.</summary>
    private byte[]? Exchange(IHidConnection connection, byte[] command, CancellationToken ct)
    {
        // A late reply of an earlier query (or the echo of a write) must not pass for this answer.
        Drain(connection);
        connection.WriteReport([0x00, .. command]);

        byte[] buffer = new byte[Math.Max(_info.InputLength, 2) + 1];
        DateTime deadline = DateTime.UtcNow + ReplyTimeout;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            int count = connection.Read(buffer, remaining);
            if (count == 0)
            {
                return null;
            }

            if (count > _payloadOffset)
            {
                return buffer.AsSpan(_payloadOffset, count - _payloadOffset).ToArray();
            }
        }

        return null;
    }

    private static void Drain(IHidConnection connection)
    {
        byte[] buffer = new byte[128];
        for (int drained = 0; drained < MaxDrainedReports && connection.Read(buffer, DrainTimeout) > 0; drained++)
        {
        }
    }

    private void CloseConnection()
    {
        Interlocked.Exchange(ref _connection, null)?.Dispose();
    }
}

internal static class SteelSeriesReplies
{
    private const byte ChargingFlag = 0x80;

    /// <summary>Decodes a battery reply payload (without report id); false if it is too short or out of range.</summary>
    public static bool TryParseBattery(SteelSeriesBattery kind, ReadOnlySpan<byte> payload, out int percent, out bool charging)
    {
        switch (kind)
        {
            case SteelSeriesBattery.Percent when payload.Length >= 3:
                percent = payload[0];
                charging = payload[2] != 0;
                break;
            case SteelSeriesBattery.Steps when payload.Length >= 2:
                percent = ((payload[1] & ~ChargingFlag) - 1) * 5;
                charging = (payload[1] & ChargingFlag) != 0;
                break;
            default:
                percent = 0;
                charging = false;
                return false;
        }

        // rivalcfg treats anything outside 0..100 as "mouse off or not supported".
        return percent is >= 0 and <= 100;
    }
}
