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

    public string ShortName => PulsarModels.ShortName(Name);

    public GearLink Link => _deviceInfo is { } i ? PulsarModels.LinkFromByte(i.Link) : GearLink.Unknown;

    // The DPI register layout is only established for the X2 CrazyLight (see PulsarDpi).
    public GearCapabilities Capabilities => GearCapabilities.Battery | GearCapabilities.PollingRate
        | (_deviceInfo?.ModelId == PulsarModels.X2CrazyLightModelId ? GearCapabilities.Dpi : GearCapabilities.None);

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

    /// <summary>The stages stored in the mouse and the 0-based active stage; null if it did not answer or the data is inconsistent.</summary>
    public async Task<GearDpiState?> ReadDpiAsync(CancellationToken ct)
    {
        EnsureDpiModel();
        byte[]? header = await ReadMemoryAsync(PulsarDpi.StageCountAddress, 4, ct);
        if (header is null
            || header[1] != (byte)(0x55 - header[0])
            || header[3] != (byte)(0x55 - header[2]))
        {
            return null;
        }

        int count = header[0];
        int active = header[2];
        if (count is < 1 or > PulsarDpi.MaxStages || active >= count)
        {
            return null;
        }

        byte[] records = new byte[count * PulsarDpi.StageRecordSize];
        for (int offset = 0; offset < records.Length; offset += 8)
        {
            byte[]? chunk = await ReadMemoryAsync((ushort)(PulsarDpi.StageBaseAddress + offset), (byte)Math.Min(8, records.Length - offset), ct);
            if (chunk is null)
            {
                return null;
            }

            chunk.CopyTo(records, offset);
        }

        int[] stages = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (!PulsarDpi.TryDecode(records.AsSpan(i * PulsarDpi.StageRecordSize, PulsarDpi.StageRecordSize), out stages[i]))
            {
                return null;
            }
        }

        return new GearDpiState(stages, active);
    }

    /// <summary>Makes the 0-based stage the active one.</summary>
    public async Task SetDpiStageAsync(int stage, CancellationToken ct)
    {
        EnsureDpiModel();
        GearDpiState state = await ReadDpiOrThrowAsync(ct);
        if (stage < 0 || stage >= state.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "No such DPI stage.");
        }

        await WriteDpiAsync(PulsarDpi.ActiveStageWrite(stage), async () => (await ReadDpiAsync(ct))?.ActiveStage == stage, ct);
    }

    /// <summary>Sets the DPI value of the active stage; a value between two encodable steps snaps to the nearest one.</summary>
    public async Task SetDpiValueAsync(int dpi, CancellationToken ct)
    {
        EnsureDpiModel();
        PulsarDpi.TryDecode(PulsarDpi.Encode(dpi), out int stored);
        GearDpiState state = await ReadDpiOrThrowAsync(ct);
        int active = state.ActiveStage;
        await WriteDpiAsync(
            PulsarDpi.StageWrite(active, dpi),
            async () =>
            {
                GearDpiState? after = await ReadDpiAsync(ct);
                return after is { } s && s.ActiveStage == active && s.Stages[active] == stored;
            },
            ct);
    }

    private void EnsureDpiModel()
    {
        if (!Capabilities.HasFlag(GearCapabilities.Dpi))
        {
            throw new NotSupportedException("DPI is only supported for the X2 CrazyLight.");
        }
    }

    private async Task<GearDpiState> ReadDpiOrThrowAsync(CancellationToken ct)
    {
        return await ReadDpiAsync(ct) ?? throw new TimeoutException("The mouse did not report its DPI stages.");
    }

    private async Task WriteDpiAsync(byte[] frame, Func<Task<bool>> readBack, CancellationToken ct)
    {
        // The acknowledgement format of MemSet is unverified: fall back to reading the value back.
        if (await ExchangeAsync(frame, PulsarProtocol.MemSet, 1, ct) is null && !await readBack())
        {
            throw new TimeoutException("The mouse did not acknowledge the DPI change.");
        }
    }

    /// <summary>Data bytes of a MemGet reply, or null if the mouse did not answer.</summary>
    private async Task<byte[]?> ReadMemoryAsync(ushort address, byte length, CancellationToken ct)
    {
        byte[]? reply = await ExchangeAsync(PulsarProtocol.MemGetRequest(address, length), PulsarProtocol.MemGet, Attempts, ct);
        return reply?.AsSpan(6, length).ToArray();
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
            try
            {
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
            // Every open handle receives every report, so a MemGet reply only counts if it echoes address and length.
            if (PulsarProtocol.IsValidReply(report, cmd)
                && (cmd != PulsarProtocol.MemGet || report.Slice(3, 3).SequenceEqual(request.AsSpan(3, 3))))
            {
                return report.ToArray();
            }
        }

        return null;
    }
}
