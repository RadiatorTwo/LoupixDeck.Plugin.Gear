using System.Security.Cryptography;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

/// <summary>
/// A Pulsar mouse behind its vendor HID collection. All commands run one at a time under a lock;
/// the connection is opened on first use.
/// </summary>
internal sealed class PulsarDevice : IGearDevice
{
    private const int Attempts = 2;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2.5);

    private readonly HidDeviceInfo _info;
    private readonly IHidTransport _transport;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly BatteryFilter _filter = new();
    private readonly string _receiverName;
    private IHidConnection? _connection;
    private PulsarInfo? _deviceInfo;

    public PulsarDevice(HidDeviceInfo info, IHidTransport transport, string receiverName, bool tested)
    {
        _info = info;
        _transport = transport;
        _receiverName = receiverName;
        Tested = tested;
    }

    public string ContainerKey => _info.ContainerKey;

    public string Key => _deviceInfo is { } i
        ? DeviceKey.ForPulsar(i.ModelId, MouseSerial)
        : $"pulsar:{_info.VendorId:x4}:{_info.ProductId:x4}";

    public string Name => _deviceInfo is { } i ? PulsarModels.NameForModel(i.ModelId) : _receiverName;

    public GearLink Link => _deviceInfo is { } i ? PulsarModels.LinkFromByte(i.Link) : GearLink.Unknown;

    public GearCapabilities Capabilities => GearCapabilities.Battery | GearCapabilities.PollingRate;

    public bool Tested { get; }

    public IReadOnlyList<int> AllowedPollingRates => PulsarModels.AllowedRates(_deviceInfo?.Link);

    /// <summary>Serial number of the mouse itself; null until a model reports one in its Info reply.</summary>
    public string? MouseSerial { get; private set; }

    /// <summary>Whether the last Info reply echoed the request nonce; null before the first Info reply.</summary>
    public bool? InfoNonceEchoed { get; private set; }

    /// <summary>Raw bytes of the last Info reply, for diagnostics.</summary>
    public byte[]? LastInfoReply { get; private set; }

    public async Task<bool> IsAwakeAsync(CancellationToken ct)
    {
        byte[]? reply = await ExchangeAsync(PulsarProtocol.Frame(PulsarProtocol.Status), PulsarProtocol.Status, Attempts, ct);
        return reply is not null && PulsarReplies.ParseStatus(reply).MouseAwake;
    }

    /// <summary>Reads model id and link; returns false if the mouse did not answer.</summary>
    public async Task<bool> RefreshInfoAsync(CancellationToken ct)
    {
        uint nonce = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        byte[] request = PulsarProtocol.InfoRequest(nonce);
        byte[]? reply = await ExchangeAsync(request, PulsarProtocol.Info, Attempts, ct);
        if (reply is null)
        {
            return false;
        }

        // If the reply echoes the nonce in bytes 6..9 we can tell it belongs to this request;
        // otherwise the cmd echo and checksum are all we have.
        InfoNonceEchoed = reply.AsSpan(6, 4).SequenceEqual(request.AsSpan(6, 4));
        LastInfoReply = reply;
        _deviceInfo = PulsarReplies.ParseInfo(reply);
        return true;
    }

    public async Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct)
    {
        byte[]? reply = await ExchangeAsync(PulsarProtocol.Frame(PulsarProtocol.Power), PulsarProtocol.Power, Attempts, ct);
        if (reply is null)
        {
            return null;
        }

        PulsarPower power = PulsarReplies.ParsePower(reply);
        await _lock.WaitAsync(ct);
        try
        {
            if (power.Charging)
            {
                // The voltage is inflated while charging, so the table would overstate the level:
                // show the raw percentage and forget the history.
                _filter.Reset();
                return new GearPowerReading(power.RawPercent, true, power.Millivolts);
            }

            int percent = _filter.Add(PulsarBattery.PercentFromMillivolts(power.Millivolts));
            return new GearPowerReading(percent, false, power.Millivolts);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int?> ReadPollingRateAsync(CancellationToken ct)
    {
        byte[]? reply = await ExchangeAsync(PulsarProtocol.MemGetRequest(0x0000, 2), PulsarProtocol.MemGet, Attempts, ct);
        if (reply is null)
        {
            return null;
        }

        byte code = reply[6];
        if (reply[7] != (byte)(0x55 - code) || !PulsarModels.TryHzForCode(code, out int hz))
        {
            return null;
        }

        return hz;
    }

    public async Task SetPollingRateAsync(int hz, CancellationToken ct)
    {
        if (!AllowedPollingRates.Contains(hz) || !PulsarModels.TryCodeForHz(hz, out byte code))
        {
            throw new ArgumentOutOfRangeException(nameof(hz), hz, "Polling rate not supported on this link.");
        }

        byte[]? reply = await ExchangeAsync(PulsarProtocol.PollingRateWrite(code), PulsarProtocol.MemSet, 1, ct);
        if (reply is null)
        {
            throw new TimeoutException("The mouse did not acknowledge the polling-rate change.");
        }
    }

    public Task<GearDpiState?> ReadDpiAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task SetDpiStageAsync(int stage, CancellationToken ct) => throw new NotSupportedException();

    public Task SetDpiValueAsync(int dpi, CancellationToken ct) => throw new NotSupportedException();

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _lock.Dispose();
    }

    private async Task<byte[]?> ExchangeAsync(byte[] request, byte cmd, int attempts, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IHidConnection connection = _connection ??= _transport.Open(_info);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                byte[]? reply = await Task.Run(() => SendAndAwait(connection, request, cmd, ct), CancellationToken.None);
                if (reply is not null)
                {
                    return reply;
                }
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sends the request and reads until a valid reply to <paramref name="cmd"/> arrives or the deadline passes.
    /// Everything else is skipped: reports of other collections (all collections of the interface arrive on
    /// Linux), stale late replies and corrupt reports.
    /// </summary>
    private static byte[]? SendAndAwait(IHidConnection connection, byte[] request, byte cmd, CancellationToken ct)
    {
        connection.SendOutputReport(request);

        DateTime deadline = DateTime.UtcNow + ReplyTimeout;
        byte[] buffer = new byte[64];
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

            ReadOnlySpan<byte> report = buffer.AsSpan(0, count);
            if (PulsarProtocol.IsValidReply(report, cmd))
            {
                return report.ToArray();
            }
        }

        return null;
    }
}
