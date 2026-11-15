using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

public sealed class PulsarProtocolTests
{
    private static byte[] Hex(string dashed) => Convert.FromHexString(dashed.Replace("-", string.Empty));

    private const string AwakeCapture = "08-03-00-00-00-01-01-9E-35-0B-00-00-00-00-00-00-6A";
    private const string PowerCapture = "08-04-00-00-00-02-5F-00-10-09-00-00-00-00-00-00-CF";

    [Fact]
    public void Frame_Power_MatchesCapture()
    {
        byte[] expected = new byte[17];
        expected[0] = 0x08;
        expected[1] = 0x04;
        expected[16] = 0x49;

        Assert.Equal(expected, PulsarProtocol.Frame(PulsarProtocol.Power));
    }

    [Fact]
    public void Checksum_Captures_MakeFrameSumTo0x55()
    {
        foreach (string capture in new[] { AwakeCapture, PowerCapture })
        {
            byte[] report = Hex(capture);
            Assert.Equal(report[16], PulsarProtocol.Checksum(report.AsSpan(0, 16)));
        }
    }

    [Fact]
    public void ParsePower_Capture()
    {
        PulsarPower power = PulsarReplies.ParsePower(Hex(PowerCapture));

        Assert.Equal(new PulsarPower(95, false, 4105), power);
    }

    [Fact]
    public void ParsePower_Charging_ReadsFlag()
    {
        byte[] report = Hex(PowerCapture);
        report[7] = 1;
        report[16] = PulsarProtocol.Checksum(report.AsSpan(0, 16));

        Assert.True(PulsarReplies.ParsePower(report).Charging);
    }

    [Fact]
    public void ParseStatus_Awake()
    {
        Assert.True(PulsarReplies.ParseStatus(Hex(AwakeCapture)).MouseAwake);
    }

    [Fact]
    public void ParseStatus_Asleep()
    {
        byte[] report = Hex(AwakeCapture);
        report[6] = 0;
        report[16] = PulsarProtocol.Checksum(report.AsSpan(0, 16));

        Assert.True(PulsarProtocol.IsValidReply(report, PulsarProtocol.Status));
        Assert.False(PulsarReplies.ParseStatus(report).MouseAwake);
    }

    [Fact]
    public void ParseInfo_ReadsModelAndLink()
    {
        byte[] report = new byte[17];
        report[0] = 0x08;
        report[1] = 0x01;
        report[10] = 0x12;
        report[11] = 0x34;
        report[12] = 5;

        Assert.Equal(new PulsarInfo(0x1234, 5), PulsarReplies.ParseInfo(report));
    }

    [Fact]
    public void IsValidReply_Capture_True()
    {
        Assert.True(PulsarProtocol.IsValidReply(Hex(PowerCapture), PulsarProtocol.Power));
    }

    [Fact]
    public void IsValidReply_WrongChecksum_False()
    {
        byte[] report = Hex(PowerCapture);
        report[16] ^= 0x01;

        Assert.False(PulsarProtocol.IsValidReply(report, PulsarProtocol.Power));
    }

    [Fact]
    public void IsValidReply_WrongCmd_False()
    {
        Assert.False(PulsarProtocol.IsValidReply(Hex(PowerCapture), PulsarProtocol.Status));
    }

    [Fact]
    public void IsValidReply_WrongReportIdOrLength_False()
    {
        byte[] foreign = Hex(PowerCapture);
        foreign[0] = 0x09;
        foreign[16] = PulsarProtocol.Checksum(foreign.AsSpan(0, 16));

        Assert.False(PulsarProtocol.IsValidReply(foreign, PulsarProtocol.Power));
        Assert.False(PulsarProtocol.IsValidReply(Hex(PowerCapture).AsSpan(0, 16), PulsarProtocol.Power));
    }

    [Fact]
    public void InfoRequest_PlacesLengthAndNonce()
    {
        byte[] frame = PulsarProtocol.InfoRequest(0x11223344);

        Assert.Equal(0x01, frame[1]);
        Assert.Equal(8, frame[5]);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, frame[6..10]);
        Assert.Equal(frame[16], PulsarProtocol.Checksum(frame.AsSpan(0, 16)));
    }

    [Fact]
    public void MemGetRequest_PlacesAddressAndLength()
    {
        byte[] frame = PulsarProtocol.MemGetRequest(0x01B8, 10);

        Assert.Equal(0x08, frame[1]);
        Assert.Equal(0x01, frame[3]);
        Assert.Equal(0xB8, frame[4]);
        Assert.Equal(10, frame[5]);
        Assert.Equal(frame[16], PulsarProtocol.Checksum(frame.AsSpan(0, 16)));
    }

    [Fact]
    public void PollingRateWrite_4000()
    {
        byte[] frame = PulsarProtocol.PollingRateWrite(0x20);

        Assert.Equal(0x07, frame[1]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x00, frame[4]);
        Assert.Equal(2, frame[5]);
        Assert.Equal(0x20, frame[6]);
        Assert.Equal(0x35, frame[7]);
        Assert.Equal(frame[16], PulsarProtocol.Checksum(frame.AsSpan(0, 16)));
    }
}
