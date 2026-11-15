using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

public sealed class PulsarDpiTests
{
    private static readonly HidDeviceInfo Vendor = new("hid-ff02", 0x3710, 0x5406, 1, 0xFF02, 0x0002, 17, 17, null, "Pulsar", "dongle");

    // Config blob 0x00..0x2b of the X2 CrazyLight capture in docs/protocol-x2-crazylight.md (pulsar-mouse-linux):
    // stage count 4 (04 51), active stage 2 (02 53), stages 400/800/1600/3200 DPI.
    private static readonly byte[] Capture =
    [
        0x10, 0x45, 0x04, 0x51, 0x02, 0x53, 0x00, 0x55, 0x00, 0x55,
        0x01, 0x54, 0x27, 0x27, 0x00, 0x07, 0x4f, 0x4f, 0x00, 0xb7,
        0x9f, 0x9f, 0x00, 0x17, 0x3f, 0x3f, 0x44, 0x93, 0x7f, 0x7f,
        0x88, 0xcf, 0x37, 0x37, 0x22, 0xc5, 0x37, 0x37, 0x22, 0xc5,
        0x37, 0x37, 0x22, 0xc5,
    ];

    private static byte[] InfoReply(ushort model)
    {
        return PulsarProtocol.Frame(PulsarProtocol.Info, [0, 0, 0, 8, 0, 0, 0, 0, (byte)(model >> 8), (byte)model, 1]);
    }

    /// <summary>A mouse that serves MemGet from <paramref name="memory"/> and applies MemSet without acknowledging it.</summary>
    private static FakeHidConnection Mouse(byte[] memory, ushort model = 0x5717)
    {
        return new FakeHidConnection(sent =>
        {
            int address = (sent[3] << 8) | sent[4];
            int length = sent[5];
            switch (sent[1])
            {
                case PulsarProtocol.Info:
                    return [InfoReply(model)];
                case PulsarProtocol.MemGet:
                    byte[] payload = new byte[4 + length];
                    payload[1] = sent[3];
                    payload[2] = sent[4];
                    payload[3] = sent[5];
                    memory.AsSpan(address, length).CopyTo(payload.AsSpan(4));
                    return [PulsarProtocol.Frame(PulsarProtocol.MemGet, payload)];
                case PulsarProtocol.MemSet:
                    sent.AsSpan(6, length).CopyTo(memory.AsSpan(address));
                    return [];
                default:
                    return [];
            }
        });
    }

    private static async Task<PulsarDevice> CreateAsync(FakeHidConnection connection)
    {
        PulsarDevice device = new(Vendor, new FakeHidTransport(connection), "Pulsar X2 CrazyLight", true);
        await device.RefreshIdentityAsync(CancellationToken.None);
        return device;
    }

    [Theory]
    [InlineData(400, 0x27, 0x00)]
    [InlineData(800, 0x4f, 0x00)]
    [InlineData(3200, 0x3f, 0x44)]
    [InlineData(6400, 0x7f, 0x88)]
    [InlineData(12800, 0x37, 0x22)]
    [InlineData(25600, 0x37, 0x66)]
    [InlineData(32000, 0x77, 0x33)]
    public void Encode_MatchesCapturedRecords(int dpi, byte low, byte high)
    {
        byte[] record = PulsarDpi.Encode(dpi);

        Assert.Equal([low, low, high, (byte)(0x55 - (low + low + high))], record);
        Assert.True(PulsarDpi.TryDecode(record, out int decoded));
        Assert.Equal(dpi, decoded);
    }

    [Fact]
    public async Task ReadDpi_ParsesStages()
    {
        using PulsarDevice device = await CreateAsync(Mouse((byte[])Capture.Clone()));

        GearDpiState? state = await device.ReadDpiAsync(CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal([400, 800, 1600, 3200], state.Value.Stages);
        Assert.Equal(2, state.Value.ActiveStage);
    }

    [Fact]
    public async Task ReadDpi_CorruptStageCount_ReturnsNull()
    {
        byte[] memory = (byte[])Capture.Clone();
        memory[3] = 0x00;
        using PulsarDevice device = await CreateAsync(Mouse(memory));

        Assert.Null(await device.ReadDpiAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadDpi_OtherModel_IsNotSupported()
    {
        using PulsarDevice device = await CreateAsync(Mouse((byte[])Capture.Clone(), 0x0057));

        Assert.False(device.Capabilities.HasFlag(GearCapabilities.Dpi));
        await Assert.ThrowsAsync<NotSupportedException>(() => device.ReadDpiAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Capabilities_X2CrazyLight_HasDpi()
    {
        using PulsarDevice device = await CreateAsync(Mouse((byte[])Capture.Clone()));

        Assert.True(device.Capabilities.HasFlag(GearCapabilities.Dpi));
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(3, 4, 0)]
    [InlineData(0, 1, 0)]
    public void NextStage_Wraps(int active, int count, int expected)
    {
        Assert.Equal(expected, PulsarDpi.NextStage(active, count));
    }

    [Theory]
    [InlineData(3, 4, 2)]
    [InlineData(0, 4, 3)]
    [InlineData(0, 1, 0)]
    public void PreviousStage_Wraps(int active, int count, int expected)
    {
        Assert.Equal(expected, PulsarDpi.PreviousStage(active, count));
    }

    [Fact]
    public async Task SetDpiValue_WritesActiveStage()
    {
        byte[] memory = (byte[])Capture.Clone();
        FakeHidConnection fake = Mouse(memory);
        using PulsarDevice device = await CreateAsync(fake);

        await device.SetDpiValueAsync(6400, CancellationToken.None);

        // Active stage is index 2, whose record sits at 0x0C + 2 * 4 = 0x14; 6400 DPI = 7f 7f 88 + checksum.
        byte[] write = Assert.Single(fake.Sent, s => s[1] == PulsarProtocol.MemSet);
        Assert.Equal(PulsarProtocol.Frame(PulsarProtocol.MemSet, [0, 0x00, 0x14, 4, 0x7f, 0x7f, 0x88, 0xcf]), write);
        GearDpiState? state = await device.ReadDpiAsync(CancellationToken.None);
        Assert.Equal([400, 800, 6400, 3200], state!.Value.Stages);
    }

    [Fact]
    public async Task SetDpiStage_WritesActiveStagePair()
    {
        FakeHidConnection fake = Mouse((byte[])Capture.Clone());
        using PulsarDevice device = await CreateAsync(fake);

        await device.SetDpiStageAsync(3, CancellationToken.None);

        byte[] write = Assert.Single(fake.Sent, s => s[1] == PulsarProtocol.MemSet);
        Assert.Equal(PulsarProtocol.Frame(PulsarProtocol.MemSet, [0, 0x00, 0x04, 2, 0x03, 0x52]), write);
    }

    [Fact]
    public async Task SetDpiStage_OutOfRange_Throws()
    {
        FakeHidConnection fake = Mouse((byte[])Capture.Clone());
        using PulsarDevice device = await CreateAsync(fake);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetDpiStageAsync(4, CancellationToken.None));
        Assert.DoesNotContain(fake.Sent, s => s[1] == PulsarProtocol.MemSet);
    }
}

public sealed class PulsarMemGetEchoTests
{
    private static readonly HidDeviceInfo Vendor = new("hid-ff02", 0x3710, 0x5406, 1, 0xFF02, 0x0002, 17, 17, null, "Pulsar", "dongle");

    private static byte[] MemGetReply(int address, byte length, params byte[] data)
    {
        return PulsarProtocol.Frame(PulsarProtocol.MemGet, [0, (byte)(address >> 8), (byte)address, length, .. data]);
    }

    private static byte[] InfoReply()
    {
        return PulsarProtocol.Frame(PulsarProtocol.Info, [0, 0, 0, 8, 0, 0, 0, 0, 0x57, 0x17, 1]);
    }

    [Fact]
    public async Task ReadPollingRate_SkipsReplyForOtherAddress()
    {
        FakeHidConnection fake = new(sent => sent[1] == PulsarProtocol.MemGet
            ? [MemGetReply(0x0010, 2, 0x01, 0x54), MemGetReply(0x0000, 2, 0x20, 0x35)]
            : []);
        using PulsarDevice device = new(Vendor, new FakeHidTransport(fake), "Pulsar", true);

        Assert.Equal(4000, await device.ReadPollingRateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadPollingRate_OnlyMismatchingReplies_IsNoAnswer()
    {
        FakeHidConnection fake = new(sent => sent[1] == PulsarProtocol.MemGet ? [MemGetReply(0x0010, 2, 0x20, 0x35)] : []);
        using PulsarDevice device = new(Vendor, new FakeHidTransport(fake), "Pulsar", true);

        Assert.Null(await device.ReadPollingRateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetDpiValue_OnlyMismatchingReplies_WritesNothing()
    {
        FakeHidConnection fake = new(sent => sent[1] switch
        {
            PulsarProtocol.Info => [InfoReply()],
            PulsarProtocol.MemGet => [MemGetReply(0x0100, 4, 0x04, 0x51, 0x02, 0x53)],
            _ => [],
        });
        using PulsarDevice device = new(Vendor, new FakeHidTransport(fake), "Pulsar", true);
        await device.RefreshIdentityAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => device.SetDpiValueAsync(800, CancellationToken.None));
        Assert.DoesNotContain(fake.Sent, s => s[1] == PulsarProtocol.MemSet);
    }
}
