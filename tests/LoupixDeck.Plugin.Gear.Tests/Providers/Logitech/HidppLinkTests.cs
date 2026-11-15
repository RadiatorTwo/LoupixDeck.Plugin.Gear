using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Logitech;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

public sealed class HidppLinkTests
{
    private static readonly HidppLinkOptions Fast = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));

    private static readonly HidDeviceInfo ShortInfo = new("short", 0x046D, 0xC539, 2, 0xFF00, 0x0001, 7, 7, null, null, "c1");
    private static readonly HidDeviceInfo LongInfo = new("long", 0x046D, 0xC539, 2, 0xFF00, 0x0002, 20, 20, null, null, "c1");

    // Root.Ping (feature 0, function 1) with our software id: address byte 0x1A.
    private const byte Address = 0x1A;

    private sealed class Rig
    {
        public FakeHidppConnection Short { get; } = new();
        public FakeHidppConnection Long { get; } = new();
        public FakeHidppTransport Transport { get; }
        public HidppLink Link { get; }

        public Rig(bool sameNode = false)
        {
            HidDeviceInfo longInfo = sameNode ? LongInfo with { Path = "short" } : LongInfo;
            Transport = new FakeHidppTransport([ShortInfo, longInfo]);
            Transport.Add("short", Short);
            if (!sameNode)
            {
                Transport.Add("long", Long);
            }

            Link = new HidppLink(ShortInfo, Transport, Fast);
            Link.Acquire();
        }
    }

    private static byte[] ShortReport(params byte[] bytes) => [.. bytes, .. new byte[7 - bytes.Length]];

    private static byte[] LongReport(params byte[] bytes) => [.. bytes, .. new byte[20 - bytes.Length]];

    private static Task<byte[]> Ping(HidppLink link, CancellationToken ct = default)
        => link.RequestAsync(1, 0, 1, new byte[] { 0, 0, 0x5A }, ct);

    [Fact]
    public async Task ShortRequest_IsWrittenAsShortReport_AndShortReplyReturnsPaddedPayload()
    {
        Rig rig = new();
        rig.Short.OnWrite = _ => rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));

        byte[] payload = await Ping(rig.Link);

        Assert.Equal(ShortReport(0x10, 1, 0, Address, 0, 0, 0x5A), rig.Short.Written.Single());
        Assert.Equal(16, payload.Length);
        Assert.Equal([4, 2, 0x5A], payload[..3]);
        Assert.All(payload[3..], b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task ShortRequest_LongReplyOnOtherCollection_IsFound()
    {
        Rig rig = new();
        rig.Short.OnWrite = _ => rig.Long.Enqueue(LongReport(0x11, 1, 0, Address, 4, 2, 0x5A, 9));

        byte[] payload = await Ping(rig.Link);

        Assert.Equal(9, payload[3]);
        Assert.Empty(rig.Long.Written);
    }

    [Fact]
    public async Task LongParameters_AreSentAsLongReportOnTheLongCollection()
    {
        Rig rig = new();
        rig.Long.OnWrite = _ => rig.Long.Enqueue(LongReport(0x11, 1, 3, 0x2A, 7));

        byte[] payload = await rig.Link.RequestAsync(1, 3, 2, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);

        Assert.Equal(LongReport(0x11, 1, 3, 0x2A, 1, 2, 3, 4), rig.Long.Written.Single());
        Assert.Empty(rig.Short.Written);
        Assert.Equal(7, payload[0]);
    }

    [Fact]
    public async Task ForeignAndStaleReports_AreSkipped()
    {
        Rig rig = new();
        rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 0xEE, 0, 0)); // stale, drained before the request
        rig.Short.OnWrite = _ =>
        {
            rig.Short.Enqueue(ShortReport(0x10, 1, 5, 0x00, 1, 1, 1)); // notification (software id 0)
            rig.Short.Enqueue(ShortReport(0x10, 2, 0, Address, 1, 1, 1)); // other slot
            rig.Short.Enqueue(ShortReport(0x10, 1, 0, 0x2A, 1, 1, 1)); // other function
            rig.Short.Enqueue([0x20, 1, 0, Address]); // not HID++
            rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
        };

        byte[] payload = await Ping(rig.Link);

        Assert.Equal(4, payload[0]);
    }

    [Fact]
    public async Task V20Error_ThrowsWithCode()
    {
        Rig rig = new();
        rig.Short.OnWrite = _ => rig.Long.Enqueue(LongReport(0x11, 1, 0xFF, 0, Address, 0x02));

        HidppException ex = await Assert.ThrowsAsync<HidppException>(() => Ping(rig.Link));

        Assert.False(ex.IsV10);
        Assert.Equal(0x02, ex.Code);
        Assert.True(ex.IsRejection);
        Assert.False(ex.IsSlotEmpty);
    }

    [Theory]
    [InlineData(0x08, true, false, false)]
    [InlineData(0x09, false, true, false)]
    [InlineData(0x04, false, true, false)]
    [InlineData(0x01, false, false, true)]
    public async Task V10Error_ThrowsWithClassification(byte code, bool empty, bool offline, bool v10Only)
    {
        Rig rig = new();
        rig.Short.OnWrite = _ => rig.Short.Enqueue(ShortReport(0x10, 1, 0x8F, 0, Address, code));

        HidppException ex = await Assert.ThrowsAsync<HidppException>(() => Ping(rig.Link));

        Assert.True(ex.IsV10);
        Assert.Equal(empty, ex.IsSlotEmpty);
        Assert.Equal(offline, ex.IsOffline);
        Assert.Equal(v10Only, ex.IsV10Only);
        Assert.False(ex.IsRejection);
    }

    [Fact]
    public async Task ErrorOfAnotherRequest_IsNotMistakenForOurs()
    {
        Rig rig = new();
        rig.Short.OnWrite = _ =>
        {
            rig.Short.Enqueue(ShortReport(0x10, 1, 0x8F, 0, 0x2A, 0x08));
            rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
        };

        byte[] payload = await Ping(rig.Link);

        Assert.Equal(4, payload[0]);
    }

    [Fact]
    public async Task NoReply_ThrowsTimeout_AndKeepsConnectionsOpen()
    {
        Rig rig = new();

        await Assert.ThrowsAsync<TimeoutException>(() => Ping(rig.Link));
        await Assert.ThrowsAsync<TimeoutException>(() => Ping(rig.Link));

        Assert.Single(rig.Transport.Opened, p => p == "short");
        Assert.Equal(0, rig.Short.DisposeCount);
    }

    [Fact]
    public async Task IoFailure_ClosesConnections_AndNextRequestReopens()
    {
        Rig rig = new();
        FakeHidppConnection shortAgain = new();
        FakeHidppConnection longAgain = new();
        rig.Transport.Add("short", shortAgain);
        rig.Transport.Add("long", longAgain);
        rig.Short.ReadFailure = new IOException("gone");

        await Assert.ThrowsAsync<IOException>(() => Ping(rig.Link));

        Assert.Equal(1, rig.Short.DisposeCount);
        Assert.Equal(1, rig.Long.DisposeCount);

        shortAgain.OnWrite = _ => shortAgain.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
        byte[] payload = await Ping(rig.Link);

        Assert.Equal(4, payload[0]);
        Assert.Equal(["short", "long", "short", "long"], rig.Transport.Opened);
    }

    [Fact]
    public async Task Connections_CloseWhenTheLastUserReleases()
    {
        Rig rig = new();
        rig.Link.Acquire();
        rig.Short.OnWrite = _ => rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
        await Ping(rig.Link);

        rig.Link.Release();
        Assert.Equal(0, rig.Short.DisposeCount);

        rig.Link.Release();
        Assert.Equal(1, rig.Short.DisposeCount);
        Assert.Equal(1, rig.Long.DisposeCount);
        Assert.Throws<InvalidOperationException>(rig.Link.Release);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Ping(rig.Link));
    }

    [Fact]
    public async Task SameNodeForBothCollections_IsOpenedOnce()
    {
        Rig rig = new(sameNode: true);
        rig.Short.OnWrite = _ => rig.Short.Enqueue(LongReport(0x11, 1, 0, Address, 4, 2, 0x5A));

        byte[] payload = await Ping(rig.Link);
        rig.Link.Release();

        Assert.Equal(4, payload[0]);
        Assert.Equal(["short"], rig.Transport.Opened);
        Assert.Equal(1, rig.Short.DisposeCount);
    }

    [Fact]
    public async Task SameNode_LongParametersGoToTheSharedNode()
    {
        Rig rig = new(sameNode: true);
        rig.Short.OnWrite = _ => rig.Short.Enqueue(LongReport(0x11, 1, 3, 0x2A, 7));

        await rig.Link.RequestAsync(1, 3, 2, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);

        Assert.Equal(0x11, rig.Short.Written.Single()[0]);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceled()
    {
        Rig rig = new();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ping(rig.Link, cts.Token));
    }

    [Fact]
    public async Task AccessDenied_PassesThrough_AndNextRequestRetries()
    {
        Rig rig = new();
        rig.Transport.OpenFailure = new HidAccessException("denied");

        await Assert.ThrowsAsync<HidAccessException>(() => Ping(rig.Link));

        rig.Transport.OpenFailure = null;
        rig.Short.OnWrite = _ => rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
        Assert.Equal(4, (await Ping(rig.Link))[0]);
    }

    [Fact]
    public async Task OnlyOneRequestAtATime()
    {
        Rig rig = new();
        int inFlight = 0;
        int maxInFlight = 0;
        rig.Short.OnWrite = _ =>
        {
            maxInFlight = Math.Max(maxInFlight, Interlocked.Increment(ref inFlight));
            rig.Short.Enqueue(ShortReport(0x10, 1, 0, Address, 4, 2, 0x5A));
            Interlocked.Decrement(ref inFlight);
        };

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Ping(rig.Link)));

        Assert.Equal(8, rig.Short.Written.Count);
        Assert.Equal(1, maxInFlight);
    }
}
