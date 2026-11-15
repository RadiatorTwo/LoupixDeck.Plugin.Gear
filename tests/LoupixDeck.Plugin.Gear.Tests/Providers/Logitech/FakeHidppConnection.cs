using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

/// <summary>One scripted HID collection: records written reports, hands out queued input reports.</summary>
internal sealed class FakeHidppConnection : IHidConnection
{
    private readonly Queue<byte[]> _inbound = new();

    public List<byte[]> Written { get; } = [];

    public int DisposeCount;

    /// <summary>Runs for every written report; it may enqueue replies on this or another connection.</summary>
    public Action<byte[]>? OnWrite { get; set; }

    /// <summary>When set, Read throws it (a dead handle).</summary>
    public Exception? ReadFailure { get; set; }

    public void Enqueue(byte[] report) => _inbound.Enqueue(report);

    public void SendOutputReport(ReadOnlySpan<byte> report) => throw new InvalidOperationException("HID++ must use WriteReport.");

    public void WriteReport(ReadOnlySpan<byte> report)
    {
        byte[] copy = report.ToArray();
        Written.Add(copy);
        OnWrite?.Invoke(copy);
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        if (!_inbound.TryDequeue(out byte[]? report))
        {
            Thread.Sleep(1);
            return 0;
        }

        report.CopyTo(buffer);
        return report.Length;
    }

    public void Dispose() => Interlocked.Increment(ref DisposeCount);
}

/// <summary>Enumerates the given collections and hands out one connection per path (or a queue of them).</summary>
internal sealed class FakeHidppTransport(IReadOnlyList<HidDeviceInfo> infos) : IHidTransport
{
    private readonly Dictionary<string, Queue<IHidConnection>> _connections = [];

    public List<string> Opened { get; } = [];

    public Exception? OpenFailure { get; set; }

    public void Add(string path, IHidConnection connection)
    {
        if (!_connections.TryGetValue(path, out Queue<IHidConnection>? queue))
        {
            _connections[path] = queue = new();
        }

        queue.Enqueue(connection);
    }

    public IReadOnlyList<HidDeviceInfo> Enumerate() => infos;

    public IHidConnection Open(HidDeviceInfo device)
    {
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        Opened.Add(device.Path);
        return _connections[device.Path].Dequeue();
    }
}
