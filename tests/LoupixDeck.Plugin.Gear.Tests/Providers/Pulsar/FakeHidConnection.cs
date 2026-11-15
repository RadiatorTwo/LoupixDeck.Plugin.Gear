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

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
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
