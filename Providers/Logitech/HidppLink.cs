using System.Diagnostics;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>Timing of a <see cref="HidppLink"/>; tests shorten it.</summary>
internal sealed record HidppLinkOptions(TimeSpan ReplyTimeout, TimeSpan ReadSlice, TimeSpan DrainTimeout)
{
    public static HidppLinkOptions Default { get; } = new(
        TimeSpan.FromSeconds(2.5),
        TimeSpan.FromMilliseconds(20),
        // Just long enough for an already queued report to be delivered on both platforms (a zero timeout would
        // cancel the Windows overlapped read before it is issued).
        TimeSpan.FromMilliseconds(1));
}

/// <summary>
/// Synchronous HID++ request/response over the short collection (report 0x10) and, if present, the long
/// collection (report 0x11) of one receiver or directly attached device. Shared by all slot devices of the
/// receiver: one request at a time, the connections open on first use and close when the last user released the
/// link or after an I/O failure (the next request reopens them).
/// </summary>
internal sealed class HidppLink(HidDeviceInfo shortCollection, IHidTransport transport, HidppLinkOptions? options = null)
{
    private const ushort VendorUsagePage = 0xFF00;
    private const int MaxDrainedReports = 256;

    private readonly HidppLinkOptions _options = options ?? HidppLinkOptions.Default;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _users;
    private IHidConnection? _short;
    private IHidConnection? _long;
    private bool _longAvailable;

    /// <summary>Registers a user of the link (a slot device).</summary>
    public void Acquire() => Interlocked.Increment(ref _users);

    /// <summary>Drops a user; the connections close when the last one is gone (after a request in flight finishes).</summary>
    public void Release()
    {
        int users = Interlocked.Decrement(ref _users);
        if (users < 0)
        {
            Interlocked.Increment(ref _users);
            throw new InvalidOperationException("The link was released more often than it was acquired.");
        }

        if (users == 0)
        {
            CloseIfIdle();
        }
    }

    /// <summary>
    /// Calls <paramref name="function"/> of the feature at <paramref name="featureIndex"/> and returns the 16 payload
    /// bytes of the reply (bytes 4..19, zero-padded).
    /// </summary>
    /// <exception cref="HidppException">The receiver or the device answered with an error.</exception>
    /// <exception cref="TimeoutException">No matching reply arrived in time.</exception>
    /// <exception cref="HidAccessException">The process lacks permission to open the device.</exception>
    public async Task<byte[]> RequestAsync(
        byte deviceIndex,
        byte featureIndex,
        byte function,
        ReadOnlyMemory<byte> parameters,
        CancellationToken ct)
    {
        if (Volatile.Read(ref _users) <= 0)
        {
            throw new ObjectDisposedException(nameof(HidppLink), "The link has no users.");
        }

        byte[] parameterBytes = parameters.ToArray();
        await _lock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                EnsureOpen();
                return await Task.Run(
                    () => SendAndAwait(deviceIndex, featureIndex, function, parameterBytes, ct),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or HidppException or TimeoutException))
            {
                // A failed handle stays dead after a replug or re-enumeration: drop it so the next request reopens.
                CloseConnections();
                throw;
            }
        }
        finally
        {
            _lock.Release();
            CloseIfIdle();
        }
    }

    private void EnsureOpen()
    {
        if (_short is not null)
        {
            return;
        }

        IHidConnection shortConnection = transport.Open(shortCollection);
        IHidConnection? longConnection = null;
        bool longAvailable = false;
        try
        {
            HidDeviceInfo? sibling = FindLongCollection();
            if (sibling is not null)
            {
                longAvailable = true;
                // On Linux both reports arrive on one hidraw node: open it once.
                if (sibling.Path != shortCollection.Path)
                {
                    longConnection = transport.Open(sibling);
                }
            }
        }
        catch
        {
            shortConnection.Dispose();
            longConnection?.Dispose();
            throw;
        }

        _short = shortConnection;
        _long = longConnection;
        _longAvailable = longAvailable;
    }

    private HidDeviceInfo? FindLongCollection()
    {
        foreach (HidDeviceInfo info in transport.Enumerate())
        {
            if (info.ContainerKey == shortCollection.ContainerKey
                && info.VendorId == shortCollection.VendorId
                && info.UsagePage == VendorUsagePage
                && info.InputLength == HidppProtocol.LongLength)
            {
                return info;
            }
        }

        return null;
    }

    private byte[] SendAndAwait(byte deviceIndex, byte featureIndex, byte function, byte[] parameters, CancellationToken ct)
    {
        IHidConnection shortConnection = _short!;
        bool useLong = parameters.Length > HidppProtocol.MaxShortParameters;
        if (useLong && !_longAvailable)
        {
            throw new NotSupportedException("This device has no long HID++ collection.");
        }

        byte[] buffer = new byte[64];
        Drain(shortConnection, buffer);
        if (_long is not null)
        {
            Drain(_long, buffer);
        }

        byte[] frame = HidppProtocol.BuildRequest(useLong, deviceIndex, featureIndex, function, HidppProtocol.SoftwareId, parameters);
        (useLong ? _long ?? shortConnection : shortConnection).WriteReport(frame);

        byte address = HidppProtocol.Address(function, HidppProtocol.SoftwareId);
        Stopwatch watch = Stopwatch.StartNew();
        int turn = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            TimeSpan remaining = _options.ReplyTimeout - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException("The device did not answer the HID++ request.");
            }

            // A reply may arrive on either collection, so read them alternately in short slices.
            IHidConnection connection = _long is not null && (turn++ & 1) == 1 ? _long : shortConnection;
            TimeSpan slice = remaining < _options.ReadSlice ? remaining : _options.ReadSlice;
            int count = connection.Read(buffer, slice);
            if (count > 0
                && HidppProtocol.TryMatch(buffer.AsSpan(0, count), deviceIndex, featureIndex, address, out byte[] payload, out HidppException? error))
            {
                return error is null ? payload : throw error;
            }
        }
    }

    private void Drain(IHidConnection connection, byte[] buffer)
    {
        for (int drained = 0; drained < MaxDrainedReports && connection.Read(buffer, _options.DrainTimeout) > 0; drained++)
        {
        }
    }

    private void CloseIfIdle()
    {
        if (Volatile.Read(ref _users) > 0 || !_lock.Wait(0))
        {
            // A request in flight closes the connections when it lets go.
            return;
        }

        try
        {
            if (Volatile.Read(ref _users) == 0)
            {
                CloseConnections();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void CloseConnections()
    {
        Interlocked.Exchange(ref _short, null)?.Dispose();
        Interlocked.Exchange(ref _long, null)?.Dispose();
        _longAvailable = false;
    }
}
