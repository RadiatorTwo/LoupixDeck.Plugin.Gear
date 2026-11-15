using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.SteelSeries;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.SteelSeries;

public sealed class SteelSeriesDeviceTests
{
    private static HidDeviceInfo Collection(ushort pid = 0x172B, int iface = 0, ushort page = 0xFFC0, int input = 65, int output = 65, string? serial = "000000000000")
    {
        return new HidDeviceInfo($"hid-{pid:x4}-{iface}-{page:x4}", 0x1038, pid, iface, page, 0x0001, input, output, serial, "SteelSeries", "usb");
    }

    private static SteelSeriesDevice Create(FakeSteelSeriesConnection connection, ushort pid = 0x172B, bool readIncludesReportId = true)
    {
        Assert.True(SteelSeriesModels.TryGet(pid, out SteelSeriesModel model));
        return new SteelSeriesDevice(Collection(pid), new FakeSteelSeriesTransport(connection), model, readIncludesReportId);
    }

    [Fact]
    public async Task Battery_DecodesTheRival650Reply()
    {
        // Reply read from a real Rival 650 on the cable (Windows, report id 0 in front).
        FakeSteelSeriesConnection connection = new(_ => [[0x00, 0x0C, 0x00, 0x01]]);
        using SteelSeriesDevice device = Create(connection);

        GearPowerReading? reading = await device.ReadPowerAsync(CancellationToken.None);

        Assert.Equal(new GearPowerReading(12, true, null), reading);
        Assert.Equal([0x00, 0xAA, 0x01], connection.Written.Single());
    }

    [Fact]
    public async Task Battery_WithoutReportIdByte()
    {
        FakeSteelSeriesConnection connection = new(_ => [[0x40, 0x00, 0x00]]);
        using SteelSeriesDevice device = Create(connection, readIncludesReportId: false);

        Assert.Equal(new GearPowerReading(64, false, null), await device.ReadPowerAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Battery_NoReply_IsNull()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        Assert.Null(await device.ReadPowerAsync(CancellationToken.None));
        Assert.Equal(2, connection.Written.Count);
    }

    [Fact]
    public async Task Battery_StaleReportIsDrainedFirst()
    {
        FakeSteelSeriesConnection connection = new(_ => [[0x00, 0x50, 0x00, 0x00]]);
        connection.Preload([0x00, 0xFF, 0xFF, 0xFF]);
        using SteelSeriesDevice device = Create(connection);

        Assert.Equal(80, (await device.ReadPowerAsync(CancellationToken.None))?.Percent);
    }

    [Theory]
    [InlineData(0x0F, 70, false)]
    [InlineData(0x15, 100, false)]
    [InlineData(0x8F, 70, true)]
    [InlineData(0x01, 0, false)]
    [InlineData(0x16, 0, false)] // (22 - 1) * 5 = 105 is out of range
    public void StepsBattery_Decodes(byte raw, int percent, bool charging)
    {
        bool ok = SteelSeriesReplies.TryParseBattery(SteelSeriesBattery.Steps, [0x92, raw], out int p, out bool c);

        Assert.Equal(raw != 0x16, ok);
        if (ok)
        {
            Assert.Equal((percent, charging), (p, c));
        }
    }

    [Fact]
    public async Task PollingRate_IsUnknownUntilWritten()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        Assert.Null(await device.ReadPollingRateAsync(CancellationToken.None));
        await device.SetPollingRateAsync(500, CancellationToken.None);

        Assert.Equal([0x00, 0x17, 0x02], connection.Written.Single());
        Assert.Equal(500, await device.ReadPollingRateAsync(CancellationToken.None));
        Assert.Equal([125, 250, 500, 1000], device.AllowedPollingRates);
    }

    [Fact]
    public async Task PollingRate_UnsupportedValueIsRejectedWithoutWriting()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetPollingRateAsync(2000, CancellationToken.None));
        Assert.Empty(connection.Written);
    }

    [Fact]
    public async Task Dpi_BeforeAnyWrite_IsTheLadderWithoutActiveStage()
    {
        using SteelSeriesDevice device = Create(new FakeSteelSeriesConnection());

        GearDpiState? dpi = await device.ReadDpiAsync(CancellationToken.None);

        // Rival 650: 100..12000 DPI, so 12800 and 25600 snap to 12000.
        Assert.Equal([400, 800, 1600, 3200, 6400, 12000], dpi?.Stages);
        Assert.Equal(-1, dpi?.ActiveStage);
    }

    [Fact]
    public async Task Dpi_WrittenValueIsInsertedAndActive()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        await device.SetDpiValueAsync(1240, CancellationToken.None);

        Assert.Equal([[0x00, 0x15, 0x01, 0x0B], [0x00, 0x15, 0x02, 0x0B]], connection.Written);
        GearDpiState? dpi = await device.ReadDpiAsync(CancellationToken.None);
        Assert.Equal([400, 800, 1200, 1600, 3200, 6400, 12000], dpi?.Stages);
        Assert.Equal(2, dpi?.ActiveStage);
    }

    [Fact]
    public async Task DpiStage_WritesTheLadderValue()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        await device.SetDpiStageAsync(2, CancellationToken.None);

        Assert.Equal([0x00, 0x15, 0x01, 0x0F], connection.Written[0]);
        Assert.Equal(2, (await device.ReadDpiAsync(CancellationToken.None))?.ActiveStage);
    }

    [Fact]
    public async Task DpiStage_OutOfRangeIsRejectedWithoutWriting()
    {
        FakeSteelSeriesConnection connection = new();
        using SteelSeriesDevice device = Create(connection);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetDpiStageAsync(6, CancellationToken.None));
        Assert.Empty(connection.Written);
    }

    [Fact]
    public async Task Dpi_LadderIsLimitedToTheSensorChoices()
    {
        using SteelSeriesDevice device = Create(new FakeSteelSeriesConnection(), pid: 0x1366);

        Assert.Equal([400, 800, 1600, 3200], (await device.ReadDpiAsync(CancellationToken.None))?.Stages);
    }

    [Fact]
    public void Identity_UsesModelAndSerial()
    {
        using SteelSeriesDevice device = Create(new FakeSteelSeriesConnection());

        Assert.Equal("steelseries:model:172b", device.Key);
        Assert.Equal("Rival 650 Wireless", device.ShortName);
        Assert.Equal(GearLink.Wired, device.Link);
        Assert.True(device.Tested);
        Assert.Equal(GearCapabilities.Battery | GearCapabilities.PollingRate | GearCapabilities.Dpi, device.Capabilities);
    }

    [Fact]
    public void WiredOnlyMouse_HasNoBattery()
    {
        using SteelSeriesDevice device = Create(new FakeSteelSeriesConnection(), pid: 0x1720);

        Assert.False(device.Capabilities.HasFlag(GearCapabilities.Battery));
        Assert.False(device.Tested);
    }

    [Fact]
    public void Provider_MatchesOnlyTheVendorCollectionOfTheModelInterface()
    {
        SteelSeriesProvider provider = new();
        FakeSteelSeriesTransport transport = new(new FakeSteelSeriesConnection());

        IReadOnlyList<IGearDevice> matched = provider.Match(
        [
            Collection(),
            Collection(iface: 3, page: 0xFFC1, output: 0),
            Collection(iface: 1, page: 0x0001),
            Collection(pid: 0x1B04, iface: 0),
        ], transport);

        Assert.Single(matched);
    }

    [Fact]
    public void Models_HaveUniqueProductIds()
    {
        Assert.Equal(76, SteelSeriesModels.Models.Count);
        Assert.Equal(SteelSeriesModels.Models.Count, SteelSeriesModels.Models.Select(m => m.ProductId).Distinct().Count());
    }
}

/// <summary>Scripted connection for the interrupt write path the SteelSeries device uses.</summary>
internal sealed class FakeSteelSeriesConnection(Func<byte[], IEnumerable<byte[]>>? responder = null) : IHidConnection
{
    private readonly Queue<byte[]> _inbound = new();

    public List<byte[]> Written { get; } = [];

    public void Preload(byte[] report) => _inbound.Enqueue(report);

    public void SendOutputReport(ReadOnlySpan<byte> report) => throw new InvalidOperationException("SteelSeries uses the interrupt write path.");

    public void WriteReport(ReadOnlySpan<byte> report)
    {
        byte[] copy = report.ToArray();
        Written.Add(copy);
        foreach (byte[] reply in responder?.Invoke(copy) ?? [])
        {
            _inbound.Enqueue(reply);
        }
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (!_inbound.TryDequeue(out byte[]? report))
        {
            return 0;
        }

        report.CopyTo(buffer);
        return report.Length;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeSteelSeriesTransport(IHidConnection connection) : IHidTransport
{
    public IReadOnlyList<HidDeviceInfo> Enumerate() => [];

    public IHidConnection Open(HidDeviceInfo device) => connection;
}
