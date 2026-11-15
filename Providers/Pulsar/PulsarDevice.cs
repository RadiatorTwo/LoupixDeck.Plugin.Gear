using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, PulsarInfo> _identities;
    private readonly CancellationTokenSource _disposed = new();
    private IHidConnection? _connection;
    private PulsarInfo? _deviceInfo;

    public PulsarDevice(
        HidDeviceInfo info,
        IHidTransport transport,
        string receiverName,
        bool tested,
        ConcurrentDictionary<string, PulsarInfo>? identities = null)
    {
        _info = info;
        _transport = transport;
        _receiverName = receiverName;
        Tested = tested;
        _identities = identities ?? new();
        if (_identities.TryGetValue(info.ContainerKey, out PulsarInfo cached))
        {
            _deviceInfo = cached;
        }
    }

    public string ContainerKey => _info.ContainerKey;

    public string? Key => _deviceInfo is { } i ? DeviceKey.ForPulsar(i.ModelId, MouseSerial) : null;

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

    public async Task<bool> RefreshIdentityAsync(CancellationToken ct)
    {
        uint nonce = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        byte[] request = PulsarProtocol.InfoRequest(nonce);
        byte[]? reply = await ExchangeAsync(
            request,
            PulsarProtocol.Info,
            Attempts,
            ct,
            r =>
            {
                // If the reply echoes the nonce in bytes 6..9 we can tell it belongs to this request;
                // otherwise the cmd echo and checksum are all we have.
                InfoNonceEchoed = r.AsSpan(6, 4).SequenceEqual(request.AsSpan(6, 4));
                LastInfoReply = r;
                PulsarInfo parsed = PulsarReplies.ParseInfo(r);
                _deviceInfo = parsed;
                _identities[_info.ContainerKey] = parsed;
            });
        return reply is not null;
    }

    public async Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct)
    {
        GearPowerReading? result = null;
        await ExchangeAsync(
            PulsarProtocol.Frame(PulsarProtocol.Power),
            PulsarProtocol.Power,
            Attempts,
            ct,
            reply =>
            {
                // Runs under the command lock, which the battery filter needs.
                PulsarPower power = PulsarReplies.ParsePower(reply);
                if (power.Charging)
                {
                    // The voltage is inflated while charging, so the table would overstate the level:
                    // show the raw percentage and forget the history.
                    _filter.Reset();
                    result = new GearPowerReading(power.RawPercent, true, power.Millivolts);
                    return;
                }

                int percent = _filter.Add(PulsarBattery.PercentFromMillivolts(power.Millivolts));
                result = new GearPowerReading(percent, false, power.Millivolts);
            });
        return result;
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
        if (reply is not null)
        {
            return;
        }

        // The acknowledgement format of MemSet is unverified: fall back to reading the value back.
        if (await ReadPollingRateAsync(ct) != hz)
        {
            throw new TimeoutException("The mouse did not acknowledge the polling-rate change.");
        }
    }

    public Task<GearDpiState?> ReadDpiAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task SetDpiStageAsync(int stage, CancellationToken ct) => throw new NotSupportedException();

    public Task SetDpiValueAsync(int dpi, CancellationToken ct) => throw new NotSupportedException();

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

    private void CloseConnection()
    {
        Interlocked.Exchange(ref _connection, null)?.Dispose();
    }

    /// <summary>
    /// Sends the request under the command lock; <paramref name="onReply"/> runs on a valid reply while the lock is still held.
    /// Returns null if no valid reply arrived after all attempts.
    /// </summary>
    private async Task<byte[]?> ExchangeAsync(byte[] request, byte cmd, int attempts, CancellationToken ct, Action<byte[]>? onReply = null)
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposed.Token);
        CancellationToken token = linked.Token;

        await _lock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            IHidConnection connection = _connection ??= _transport.Open(_info);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                byte[]? reply = await Task.Run(() => SendAndAwait(connection, request, cmd, token), CancellationToken.None);
                if (reply is not null)
                {
                    onReply?.Invoke(reply);
                    return reply;
                }
            }

            // A cancelled read looks like a timeout; do not report it as "asleep".
            token.ThrowIfCancellationRequested();
            return null;
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
