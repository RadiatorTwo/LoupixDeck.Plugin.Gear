using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

/// <summary>Scripted connection: every sent report is recorded and answered by the responder.</summary>
internal sealed class FakeHidConnection(Func<byte[], IEnumerable<byte[]>>? responder = null) : IHidConnection
{
    private readonly Queue<byte[]> _inbound = new();

    public List<byte[]> Sent { get; } = [];

    /// <summary>When set, Read signals <see cref="ReadEntered"/> and blocks until the gate is opened.</summary>
    public ManualResetEventSlim? ReadGate { get; init; }

    public ManualResetEventSlim ReadEntered { get; } = new(false);

    public int DisposeCount;

    /// <summary>When set, Read throws it instead of returning a report (a dead handle).</summary>
    public Exception? ReadFailure { get; init; }

    /// <summary>Queues a report as if the mouse had sent it before the next request.</summary>
    public void Preload(byte[] report) => _inbound.Enqueue(report);

    public void SendOutputReport(ReadOnlySpan<byte> report)
    {
        byte[] copy = report.ToArray();
        Sent.Add(copy);
        if (responder is not null)
        {
            foreach (byte[] reply in responder(copy))
            {
                _inbound.Enqueue(reply);
            }
        }
    }

    /// <summary>Reports sent through the interrupt write path.</summary>
    public List<byte[]> Written { get; } = [];

    public void WriteReport(ReadOnlySpan<byte> report) => Written.Add(report.ToArray());

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        if (ReadGate is not null)
        {
            ReadEntered.Set();
            ReadGate.Wait();
        }

        if (!_inbound.TryDequeue(out byte[]? report))
        {
            return 0;
        }

        report.CopyTo(buffer);
        return report.Length;
    }

    public void Dispose()
    {
        Interlocked.Increment(ref DisposeCount);
    }
}

internal sealed class FakeHidTransport(IHidConnection connection) : IHidTransport
{
    public IReadOnlyList<HidDeviceInfo> Enumerate() => [];

    public IHidConnection Open(HidDeviceInfo device) => connection;
}

/// <summary>Hands out one scripted connection per Open call and counts the calls.</summary>
internal sealed class SequenceHidTransport(params IHidConnection[] connections) : IHidTransport
{
    public int OpenCount { get; private set; }

    public IReadOnlyList<HidDeviceInfo> Enumerate() => [];

    public IHidConnection Open(HidDeviceInfo device) => connections[OpenCount++];
}
