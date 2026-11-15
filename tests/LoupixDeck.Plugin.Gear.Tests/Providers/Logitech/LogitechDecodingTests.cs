using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Providers.Logitech;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

public sealed class LogitechDecodingTests
{
    [Theory]
    [InlineData("Tunable RGB Gaming Mouse G502", "G502")]
    [InlineData("G502 X PLUS", "G502 X PLUS")]
    [InlineData("PRO X SUPERLIGHT 2", "PRO X SUPERLIGHT 2")]
    [InlineData("Wireless Gaming Mouse G903 HERO", "G903 HERO")]
    public void ShortModelName_StartsAtTheModelNumber(string name, string expected)
    {
        Assert.Equal(expected, LogitechDecoding.ShortModelName(name));
    }

    private static byte[] Payload(params byte[] bytes) => [.. bytes, .. new byte[16 - bytes.Length]];

    [Fact]
    public void DeviceInformation_ReadsUnitIdAndModelIds()
    {
        byte[] payload = Payload(3, 0xA1, 0xB2, 0xC3, 0xD4, 0x00, 0x0C, 0x40, 0x82, 0x00, 0x00, 0x12, 0x34);

        LogitechIdentity identity = LogitechDecoding.ParseDeviceInformation(payload);

        Assert.Equal([0xA1, 0xB2, 0xC3, 0xD4], identity.UnitId);
        Assert.Equal([0x4082, 0x0000, 0x1234], identity.ModelIds);
        Assert.Equal(0x4082, identity.ModelId);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    public void PointingDevices_AreMouseTrackpadTrackball(byte type, bool pointing)
    {
        Assert.Equal(pointing, LogitechDecoding.IsPointingDevice(type));
    }

    [Fact]
    public void AssembleName_JoinsChunksAndCutsToLength()
    {
        string name = LogitechDecoding.AssembleName(18, [Encoding("G PRO X SUPERLIG"), Encoding("HT" + new string('\0', 14))]);

        Assert.Equal("G PRO X SUPERLIGHT", name);
    }

    [Fact]
    public void AssembleName_ShortChunks_AndTrailingNulsAreTrimmed()
    {
        Assert.Equal("MX Ma", LogitechDecoding.AssembleName(5, [Encoding("MX "), Encoding("Mas")]));
        Assert.Equal("Mouse", LogitechDecoding.AssembleName(8, [Encoding("Mouse\0\0\0")]));
    }

    private static byte[] Encoding(string text) => System.Text.Encoding.ASCII.GetBytes(text);

    // Vectors from openlogi-net MvpFeatureTests (MIT).

    [Fact]
    public void DpiList_ParsesPlainValues()
    {
        Assert.True(LogitechDecoding.TryParseDpiList([0x01, 0x90, 0x03, 0x20, 0x06, 0x40, 0x00, 0x00], out int[] values));
        Assert.Equal([400, 800, 1600], values);
    }

    [Fact]
    public void DpiList_ExpandsRanges()
    {
        Assert.True(LogitechDecoding.TryParseDpiList([0x01, 0x90, 0xE1, 0x90, 0x06, 0x40, 0x00, 0x00], out int[] values));
        Assert.Equal([400, 800, 1200, 1600], values);
    }

    [Fact]
    public void DpiList_KeepsOffGridHighEndpoint()
    {
        Assert.True(LogitechDecoding.TryParseDpiList([0x01, 0x90, 0xE1, 0x90, 0x05, 0xDC, 0x00, 0x00], out int[] values));
        Assert.Equal([400, 800, 1200, 1500], values);
    }

    [Fact]
    public void DpiList_SortsAndDeduplicates()
    {
        Assert.True(LogitechDecoding.TryParseDpiList([0x06, 0x40, 0x03, 0x20, 0x03, 0x20, 0x00, 0x00], out int[] values));
        Assert.Equal([800, 1600], values);
    }

    [Fact]
    public void DpiList_ParsesFullListWithoutTerminator()
    {
        Assert.True(LogitechDecoding.TryParseDpiList([0x01, 0x90, 0x03, 0x20, 0x06, 0x40], out int[] values));
        Assert.Equal([400, 800, 1600], values);
    }

    [Theory]
    [InlineData(new byte[] { 0xE0, 0x32, 0x1F, 0x40, 0x00, 0x00 })] // range marker without previous value
    [InlineData(new byte[] { 0x01, 0x90, 0xE0, 0x32 })] // range marker without end value
    [InlineData(new byte[] { 0x01, 0x90, 0xE0, 0x00, 0x06, 0x40, 0x00, 0x00 })] // zero step
    [InlineData(new byte[] { 0x06, 0x40, 0xE0, 0x32, 0x01, 0x90, 0x00, 0x00 })] // descending range
    [InlineData(new byte[] { 0x00, 0x00 })] // no values
    public void DpiList_RejectsMalformedLists(byte[] bytes)
    {
        Assert.False(LogitechDecoding.TryParseDpiList(bytes, out _));
    }

    [Fact]
    public void Dpi_IsBigEndianAfterTheSensorIndex()
    {
        Assert.Equal(1600, LogitechDecoding.ParseDpi(Payload(0, 0x06, 0x40)));
    }

    [Fact]
    public void SnapDpi_PicksNearestSupportedValue_AndRejectsOutOfRange()
    {
        int[] supported = [200, 400, 800, 1600];

        Assert.True(LogitechDecoding.TrySnapDpi(supported, 1000, out int snapped));
        Assert.Equal(800, snapped);
        Assert.True(LogitechDecoding.TrySnapDpi(supported, 1200, out snapped));
        Assert.Equal(800, snapped); // tie goes to the lower value
        Assert.True(LogitechDecoding.TrySnapDpi(supported, 1600, out snapped));
        Assert.Equal(1600, snapped);
        Assert.False(LogitechDecoding.TrySnapDpi(supported, 100, out _));
        Assert.False(LogitechDecoding.TrySnapDpi(supported, 3200, out _));
    }

    [Fact]
    public void DpiState_UsesTheLadderInsideTheRange()
    {
        GearDpiState state = LogitechDecoding.BuildDpiState([200, 400, 800, 1600, 3200, 6400, 12800, 25600], 1600);

        Assert.Equal([400, 800, 1600, 3200, 6400, 12800, 25600], state.Stages);
        Assert.Equal(2, state.ActiveStage);
    }

    [Fact]
    public void DpiState_SnapsRungsDedupesAndLimitsToTheRange()
    {
        // Supported 100..8000 in steps of 100 but 25600 is out of range; 12800 too.
        int[] supported = [.. Enumerable.Range(1, 80).Select(i => i * 100)];

        GearDpiState state = LogitechDecoding.BuildDpiState(supported, 800);

        Assert.Equal([400, 800, 1600, 3200, 6400], state.Stages);
        Assert.Equal(1, state.ActiveStage);
    }

    [Fact]
    public void DpiState_SparseSensorCollapsesRungs()
    {
        GearDpiState state = LogitechDecoding.BuildDpiState([500, 1000, 2000], 1000);

        Assert.Equal([1000, 2000], state.Stages); // 400 is below the range, 800 and 1600 snap to 1000 and 2000
    }

    [Fact]
    public void DpiState_InsertsTheCurrentValueInOrder()
    {
        GearDpiState state = LogitechDecoding.BuildDpiState([100, 400, 800, 1000, 1600], 1000);

        Assert.Equal([400, 800, 1000, 1600], state.Stages);
        Assert.Equal(2, state.ActiveStage);
    }

    [Fact]
    public void ExtendedRates_UseByteOneAlone()
    {
        foreach (byte byteZero in new byte[] { 0x00, 0x01, 0xFF })
        {
            // Vector from openlogi-hidpp extended_report_rate.rs: 125, 1000 and 8000 Hz.
            Assert.Equal([125, 1000, 8000], LogitechDecoding.ParseExtendedRates(Payload(byteZero, 0b0100_1001)));
        }
    }

    [Fact]
    public void ExtendedRates_CodesMapToHz()
    {
        int[] expected = [125, 250, 500, 1000, 2000, 4000, 8000];
        for (byte code = 0; code < expected.Length; code++)
        {
            Assert.True(LogitechDecoding.TryExtendedHzForCode(code, out int hz));
            Assert.Equal(expected[code], hz);
            Assert.True(LogitechDecoding.TryExtendedCodeForHz(hz, out byte back));
            Assert.Equal(code, back);
        }

        Assert.False(LogitechDecoding.TryExtendedHzForCode(7, out _));
        Assert.False(LogitechDecoding.TryExtendedCodeForHz(3000, out _));
    }

    [Fact]
    public void LegacyRates_BitNIsNPlusOneMs_OnlyStandardRatesKept()
    {
        // 1 ms, 2 ms, 3 ms, 8 ms -> 1000, 500, 125 Hz (3 ms has no standard rate).
        Assert.Equal([125, 500, 1000], LogitechDecoding.ParseLegacyRates(Payload(0b1000_0111)));
        Assert.Equal([250], LogitechDecoding.ParseLegacyRates(Payload(0b0000_1000)));
    }

    [Fact]
    public void LegacyRates_MsAndHzConvert()
    {
        Assert.True(LogitechDecoding.TryLegacyHzForMs(2, out int hz));
        Assert.Equal(500, hz);
        Assert.False(LogitechDecoding.TryLegacyHzForMs(3, out _));
        Assert.True(LogitechDecoding.TryLegacyMsForHz(250, out byte ms));
        Assert.Equal(4, ms);
        Assert.False(LogitechDecoding.TryLegacyMsForHz(2000, out _));
    }

    [Theory]
    [InlineData(new byte[] { 87, 0, 0 }, 87, false)]
    [InlineData(new byte[] { 50, 0, 1 }, 50, true)]
    [InlineData(new byte[] { 40, 0, 2 }, 40, true)]
    [InlineData(new byte[] { 99, 0, 3 }, 100, false)]
    public void UnifiedBattery_WithPercentage(byte[] bytes, int percent, bool charging)
    {
        GearPowerReading? reading = LogitechDecoding.DecodeUnifiedBattery(Payload(bytes), true);

        Assert.Equal(new GearPowerReading(percent, charging, null), reading);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 20)]
    [InlineData(4, 50)]
    [InlineData(8, 90)]
    public void UnifiedBattery_WithoutPercentage_UsesTheLevel(byte level, int percent)
    {
        Assert.Equal(new GearPowerReading(percent, false, null), LogitechDecoding.DecodeUnifiedBattery(Payload(0, level, 0), false));
    }

    [Fact]
    public void UnifiedBattery_InvalidData_IsNull()
    {
        Assert.Null(LogitechDecoding.DecodeUnifiedBattery(Payload(50, 4, 4), true)); // error status
        Assert.Null(LogitechDecoding.DecodeUnifiedBattery(Payload(150, 4, 0), true));
        Assert.Null(LogitechDecoding.DecodeUnifiedBattery(Payload(0, 3, 0), false)); // not a level flag
    }

    [Fact]
    public void UnifiedBattery_CapabilityBit()
    {
        Assert.True(LogitechDecoding.UnifiedBatteryReportsPercentage(Payload(0x0F, 0b11)));
        Assert.False(LogitechDecoding.UnifiedBatteryReportsPercentage(Payload(0x0F, 0b01)));
    }

    [Theory]
    [InlineData(4300, 100)]
    [InlineData(4200, 100)]
    [InlineData(4000, 78)]
    [InlineData(3800, 45)]
    [InlineData(3600, 10)]
    [InlineData(3400, 0)]
    [InlineData(3000, 0)]
    public void VoltageCurve_HitsPointsAndClamps(int millivolts, int percent)
    {
        Assert.Equal(percent, LogitechDecoding.PercentFromMillivolts(millivolts));
    }

    [Fact]
    public void VoltageCurve_InterpolatesAndIsMonotonic()
    {
        Assert.Equal(54, LogitechDecoding.PercentFromMillivolts(3850));

        int previous = 0;
        for (int mv = 3300; mv <= 4300; mv += 10)
        {
            int percent = LogitechDecoding.PercentFromMillivolts(mv);
            Assert.True(percent >= previous, $"dropped at {mv} mV");
            previous = percent;
        }
    }

    [Fact]
    public void BatteryVoltage_DecodesMillivoltsAndFlags()
    {
        Assert.Equal(new GearPowerReading(47, false, 3811), LogitechDecoding.DecodeBatteryVoltage(Payload(0x0E, 0xE3, 0x00)));
        Assert.Equal(new GearPowerReading(47, true, 3811), LogitechDecoding.DecodeBatteryVoltage(Payload(0x0E, 0xE3, 0x80)));
        Assert.Equal(new GearPowerReading(100, false, 3811), LogitechDecoding.DecodeBatteryVoltage(Payload(0x0E, 0xE3, 0xC0)));
    }

    [Theory]
    [InlineData(new byte[] { 75, 50, 0 }, 75, false)]
    [InlineData(new byte[] { 0, 50, 1 }, 0, true)]
    [InlineData(new byte[] { 0, 50, 2 }, 90, true)]
    [InlineData(new byte[] { 0, 50, 3 }, 100, false)]
    [InlineData(new byte[] { 30, 20, 4 }, 30, true)]
    public void LegacyBattery_DecodesLevelAndStatus(byte[] bytes, int percent, bool charging)
    {
        Assert.Equal(new GearPowerReading(percent, charging, null), LogitechDecoding.DecodeLegacyBattery(Payload(bytes)));
    }

    [Fact]
    public void LegacyBattery_ErrorStatusOrBadLevel_IsNull()
    {
        Assert.Null(LogitechDecoding.DecodeLegacyBattery(Payload(50, 20, 5)));
        Assert.Null(LogitechDecoding.DecodeLegacyBattery(Payload(200, 20, 0)));
    }

    [Fact]
    public void Link_FollowsTheHighestAllowedRate()
    {
        Assert.Equal(GearLink.Wireless8K, LogitechDecoding.LinkFromRates([125, 1000, 8000]));
        Assert.Equal(GearLink.Wireless4K, LogitechDecoding.LinkFromRates([1000, 4000]));
        Assert.Equal(GearLink.Wireless2K, LogitechDecoding.LinkFromRates([1000, 2000]));
        Assert.Equal(GearLink.Wireless1K, LogitechDecoding.LinkFromRates([125, 500, 1000]));
        Assert.Equal(GearLink.Wireless1K, LogitechDecoding.LinkFromRates([]));
    }
}
