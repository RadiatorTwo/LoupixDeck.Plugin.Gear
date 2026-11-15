using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

public sealed class PulsarDeviceTests
{
    private static readonly HidDeviceInfo Vendor = Collection(0xFF02, 0x0002, 17, 17, 1);

    private static HidDeviceInfo Collection(ushort page, ushort usage, int input, int output, int iface)
    {
        return new HidDeviceInfo("hid-" + page.ToString("x4") + "-" + usage.ToString("x4"), 0x3710, 0x5406, iface, page, usage, input, output, null, "Pulsar", "dongle");
    }

    private static byte[] PowerReply(byte raw, bool charging, ushort mv)
    {
        return PulsarProtocol.Frame(PulsarProtocol.Power, [0, 0, 0, 2, raw, (byte)(charging ? 1 : 0), (byte)(mv >> 8), (byte)mv]);
    }

    private static byte[] InfoReply(ushort model, byte link, byte[]? echoNonce = null)
    {
        byte[] n = echoNonce ?? [0, 0, 0, 0];
        return PulsarProtocol.Frame(PulsarProtocol.Info, [0, 0, 0, 8, n[0], n[1], n[2], n[3], (byte)(model >> 8), (byte)model, link]);
    }

    private static PulsarDevice Create(FakeHidConnection connection)
    {
        return new PulsarDevice(Vendor, new FakeHidTransport(connection), "Pulsar X2 CrazyLight", true);
    }

    [Fact]
    public void Match_FindsVendorCollectionOnly()
    {
        List<HidDeviceInfo> all =
        [
            Collection(0x0001, 0x0002, 8, 0, 0),
            Collection(0x0001, 0x0006, 8, 0, 0),
            Collection(0x000C, 0x0001, 3, 0, 0),
            Collection(0xFF01, 0x0001, 8, 8, 0),
            Collection(0xFF00, 0x0001, 64, 64, 0),
            Collection(0xFF02, 0x0002, 17, 17, 1),
            Collection(0xFF06, 0x0001, 49, 0, 1),
            Collection(0x0001, 0x0006, 8, 0, 1),
            Collection(0x0001, 0x0002, 8, 0, 1),
            Collection(0xFF02, 0x0002, 64, 64, 2),
        ];

        IReadOnlyList<IGearDevice> devices = new PulsarProvider().Match(all, new FakeHidTransport(new FakeHidConnection()));

        IGearDevice device = Assert.Single(devices);
        Assert.Equal("dongle", device.ContainerKey);
        Assert.True(device.Tested);
        Assert.Equal(GearCapabilities.Battery | GearCapabilities.PollingRate, device.Capabilities);
    }

    [Fact]
    public async Task ReadResponse_SkipsForeignAndCorruptReports()
    {
        byte[] corrupt = PowerReply(0x5F, false, 4105);
        corrupt[16] ^= 0xFF;
        byte[] mouse = new byte[17];
        mouse[0] = 0x05;
        FakeHidConnection fake = new(_ => [mouse, corrupt, PowerReply(0x5F, false, 4105)]);
        using PulsarDevice device = Create(fake);

        GearPowerReading? reading = await device.ReadPowerAsync(CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Single(fake.Sent);
    }

    [Fact]
    public async Task ReadPower_UsesVoltageTable()
    {
        using PulsarDevice device = Create(new FakeHidConnection(_ => [PowerReply(0x5F, false, 4105)]));

        GearPowerReading? reading = await device.ReadPowerAsync(CancellationToken.None);

        Assert.Equal(new GearPowerReading(99, false, 4105), reading);
    }

    [Fact]
    public async Task ReadPower_Charging_ShowsRawPercent()
    {
        using PulsarDevice device = Create(new FakeHidConnection(_ => [PowerReply(0x50, true, 4200)]));

        GearPowerReading? reading = await device.ReadPowerAsync(CancellationToken.None);

        Assert.Equal(new GearPowerReading(0x50, true, 4200), reading);
    }

    [Fact]
    public async Task ReadPower_Asleep_ReturnsNullAfterRetry()
    {
        FakeHidConnection fake = new();
        using PulsarDevice device = Create(fake);

        GearPowerReading? reading = await device.ReadPowerAsync(CancellationToken.None);

        Assert.Null(reading);
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task IsAwake_ReflectsStatusByte()
    {
        using PulsarDevice awake = Create(new FakeHidConnection(_ => [PulsarProtocol.Frame(PulsarProtocol.Status, [0, 0, 0, 1, 1])]));
        using PulsarDevice asleep = Create(new FakeHidConnection(_ => [PulsarProtocol.Frame(PulsarProtocol.Status, [0, 0, 0, 1, 0])]));

        Assert.True(await awake.IsAwakeAsync(CancellationToken.None));
        Assert.False(await asleep.IsAwakeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AllowedRates_OneKDongle_StopsAt1000()
    {
        using PulsarDevice device = Create(new FakeHidConnection(_ => [InfoReply(0x0057, 0)]));
        await device.RefreshInfoAsync(CancellationToken.None);

        Assert.Equal(GearLink.Wireless1K, device.Link);
        Assert.Equal([125, 250, 500, 1000], device.AllowedPollingRates);
    }

    [Theory]
    [InlineData(1, 4000)]
    [InlineData(2, 1000)]
    [InlineData(3, 8000)]
    [InlineData(4, 2000)]
    [InlineData(5, 8000)]
    public async Task AllowedRates_TopRateByLink(byte link, int top)
    {
        using PulsarDevice device = Create(new FakeHidConnection(_ => [InfoReply(0x0057, link)]));
        await device.RefreshInfoAsync(CancellationToken.None);

        Assert.Equal(top, device.AllowedPollingRates[^1]);
    }

    [Fact]
    public async Task SetPollingRate_SendsMemSetFrame()
    {
        // Fake only: echoes every non-Info request as its own acknowledgement.
        FakeHidConnection fake = new(sent => sent[1] == PulsarProtocol.Info ? [InfoReply(0x0057, 1)] : [sent]);
        using PulsarDevice device = Create(fake);
        await device.RefreshInfoAsync(CancellationToken.None);

        await device.SetPollingRateAsync(4000, CancellationToken.None);

        Assert.Equal(PulsarProtocol.PollingRateWrite(0x20), fake.Sent[^1]);
    }

    [Fact]
    public async Task SetPollingRate_RejectsRateAboveLink()
    {
        FakeHidConnection fake = new(_ => [InfoReply(0x0057, 0)]);
        using PulsarDevice device = Create(fake);
        await device.RefreshInfoAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetPollingRateAsync(4000, CancellationToken.None));
        Assert.Single(fake.Sent);
    }

    [Fact]
    public async Task ReadPollingRate_ValidatesCodePair()
    {
        using PulsarDevice good = Create(new FakeHidConnection(_ => [PulsarProtocol.Frame(PulsarProtocol.MemGet, [0, 0, 0, 2, 0x20, 0x35])]));
        using PulsarDevice bad = Create(new FakeHidConnection(_ => [PulsarProtocol.Frame(PulsarProtocol.MemGet, [0, 0, 0, 2, 0x20, 0x00])]));

        Assert.Equal(4000, await good.ReadPollingRateAsync(CancellationToken.None));
        Assert.Null(await bad.ReadPollingRateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Key_UsesModelId()
    {
        using PulsarDevice device = Create(new FakeHidConnection(_ => [InfoReply(0x060A, 5)]));
        Assert.Equal("pulsar:3710:5406", device.Key);

        await device.RefreshInfoAsync(CancellationToken.None);

        Assert.Equal("pulsar:model:060a", device.Key);
        Assert.Equal("Pulsar 060a", device.Name);
        Assert.Equal(GearLink.Wireless8K, device.Link);
        Assert.Equal("8K 2.4G", device.Link.ToDisplayText());
    }

    [Fact]
    public async Task Info_DetectsNonceEcho()
    {
        using PulsarDevice echo = Create(new FakeHidConnection(sent => [InfoReply(1, 0, sent[6..10])]));
        using PulsarDevice plain = Create(new FakeHidConnection(_ => [InfoReply(1, 0)]));

        await echo.RefreshInfoAsync(CancellationToken.None);
        await plain.RefreshInfoAsync(CancellationToken.None);

        Assert.True(echo.InfoNonceEchoed);
        Assert.False(plain.InfoNonceEchoed);
    }

    [Fact]
    public async Task DpiMembers_AreNotSupportedYet()
    {
        using PulsarDevice device = Create(new FakeHidConnection());

        await Assert.ThrowsAsync<NotSupportedException>(() => device.ReadDpiAsync(CancellationToken.None));
        Assert.Equal("pulsar:model:0057:abc", DeviceKey.ForPulsar(0x57, "abc"));
    }
}
