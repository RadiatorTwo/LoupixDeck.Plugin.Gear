using LoupixDeck.Plugin.Gear.Providers.Logitech;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

public sealed class HidppProtocolTests
{
    [Fact]
    public void BuildRequest_Short_HasIdIndexFeatureAddressAndParameters()
    {
        byte[] frame = HidppProtocol.BuildRequest(false, 0xFF, 0x05, 2, 0x0A, [1, 2]);

        Assert.Equal([0x10, 0xFF, 0x05, 0x2A, 1, 2, 0], frame);
    }

    [Fact]
    public void BuildRequest_Long_IsTwentyBytes()
    {
        byte[] frame = HidppProtocol.BuildRequest(true, 1, 0x05, 15, 0x0A, [9, 8, 7, 6]);

        Assert.Equal(20, frame.Length);
        Assert.Equal(0x11, frame[0]);
        Assert.Equal(0xFA, frame[3]);
        Assert.Equal([9, 8, 7, 6], frame[4..8]);
    }

    [Fact]
    public void BuildRequest_TooManyParameters_Throws()
    {
        Assert.Throws<ArgumentException>(() => HidppProtocol.BuildRequest(false, 1, 0, 0, 0x0A, [1, 2, 3, 4]));
        Assert.Throws<ArgumentException>(() => HidppProtocol.BuildRequest(true, 1, 0, 0, 0x0A, new byte[17]));
    }

    [Fact]
    public void TryMatch_ShortReport_ExtendsPayloadTo16Bytes()
    {
        byte[] report = [0x10, 1, 0, 0x1A, 4, 2, 0x5A];

        Assert.True(HidppProtocol.TryMatch(report, 1, 0, 0x1A, out byte[] payload, out HidppException? error));

        Assert.Null(error);
        Assert.Equal(16, payload.Length);
        Assert.Equal([4, 2, 0x5A], payload[..3]);
    }

    [Fact]
    public void TryMatch_TruncatedReport_DoesNotMatch()
    {
        Assert.False(HidppProtocol.TryMatch([0x11, 1, 0, 0x1A, 4, 2, 0x5A], 1, 0, 0x1A, out _, out _));
        Assert.False(HidppProtocol.TryMatch([], 1, 0, 0x1A, out _, out _));
    }

    [Fact]
    public void TryMatch_V10ErrorOnLongReport_DoesNotMatch()
    {
        byte[] report = new byte[20];
        report[0] = 0x11;
        report[1] = 1;
        report[2] = 0x8F;
        report[3] = 0;
        report[4] = 0x1A;
        report[5] = 8;

        Assert.False(HidppProtocol.TryMatch(report, 1, 0, 0x1A, out _, out _));
    }
}
