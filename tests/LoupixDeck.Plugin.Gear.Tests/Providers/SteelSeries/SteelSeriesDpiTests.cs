using LoupixDeck.Plugin.Gear.Providers.SteelSeries;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.SteelSeries;

public sealed class SteelSeriesDpiTests
{
    private static readonly SteelSeriesDpiCodec Range12K = SteelSeriesDpiCodec.Range(100, 12000, 100, 0);

    [Theory]
    [InlineData(100, 0x00, 100)]
    [InlineData(800, 0x07, 800)]
    [InlineData(1600, 0x0F, 1600)]
    [InlineData(12000, 0x77, 12000)]
    [InlineData(849, 0x07, 800)]
    [InlineData(850, 0x08, 900)] // half rounds up, as in rivalcfg
    [InlineData(10, 0x00, 100)]
    [InlineData(32000, 0x77, 12000)]
    public void Range_SnapsAndEncodes(int dpi, int code, int actual)
    {
        Assert.Equal((code, actual), Range12K.Encode(dpi));
    }

    [Theory]
    [InlineData(800, 3, 800)]
    [InlineData(1200, 3, 800)] // tie between 800 and 1600 goes to the lower value
    [InlineData(1201, 2, 1600)]
    [InlineData(100, 4, 400)]
    public void Choices_PickNearest(int dpi, int code, int actual)
    {
        SteelSeriesDpiCodec kinzu = SteelSeriesDpiCodec.Choices((400, 4), (800, 3), (1600, 2), (3200, 1));

        Assert.Equal((code, actual), kinzu.Encode(dpi));
    }

    [Fact]
    public void SensorTables_MatchRivalcfg()
    {
        Assert.Equal((0x04, 200), SteelSeriesSensors.Pmw3326.Encode(200));
        Assert.Equal((0xA7, 7200), SteelSeriesSensors.Pmw3326.Encode(7200));
        Assert.Equal((0x04, 200), SteelSeriesSensors.TrueMoveCore.Encode(100));
        Assert.Equal((0x00, 100), SteelSeriesSensors.TrueMoveAir.Encode(100));
        Assert.Equal((0x09, 800), SteelSeriesSensors.TrueMoveAir.Encode(800));
    }

    [Fact]
    public void Rival650_WritesBothSlots()
    {
        Assert.True(SteelSeriesModels.TryGet(0x172B, out SteelSeriesModel model));

        (IReadOnlyList<byte[]> packets, int dpi) = model.Profile.Dpi.Build(800);

        Assert.Equal(800, dpi);
        Assert.Equal([[0x15, 0x01, 0x07], [0x15, 0x02, 0x07]], packets);
    }

    [Fact]
    public void SlotSuffix_FollowsTheCode()
    {
        Assert.True(SteelSeriesModels.TryGet(0x1720, out SteelSeriesModel rival310));

        (IReadOnlyList<byte[]> packets, _) = rival310.Profile.Dpi.Build(1600);

        Assert.Equal([0x53, 0x00, 0x01, 0x0F, 0x00, 0x42], packets[0]);
    }

    [Fact]
    public void Presets_WriteOnePreset()
    {
        SteelSeriesPresetDpiWriter writer = new([0x2D], SteelSeriesSensors.TrueMoveAir, 0);

        (IReadOnlyList<byte[]> packets, int dpi) = writer.Build(800);

        Assert.Equal(800, dpi);
        Assert.Equal([[0x2D, 0x01, 0x00, 0x09]], packets);
    }

    [Fact]
    public void Presets_TwoByteRangeIsLittleEndian()
    {
        Assert.True(SteelSeriesModels.TryGet(0x1832, out SteelSeriesModel senseiTen));

        (IReadOnlyList<byte[]> packets, _) = senseiTen.Profile.Dpi.Build(18000);

        // 18000 / 50 = 360 = 0x0168; first preset is 1 on this model.
        Assert.Equal([[0x55, 0x00, 0x01, 0x01, 0x68, 0x01]], packets);
    }

    [Fact]
    public void Presets_XyInterleaved()
    {
        SteelSeriesPresetDpiWriter writer = new([0x34], SteelSeriesSensors.TrueMoveCore, 0, SteelSeriesPresetLayout.XyInterleaved);

        Assert.Equal([0x34, 0x01, 0x00, 0x04, 0x04], writer.Build(200).Packets[0]);
    }

    [Fact]
    public void Presets_XThenY_PadsToFivePresets()
    {
        SteelSeriesPresetDpiWriter writer = new([0x2C], SteelSeriesSensors.TrueMoveAir, 1, SteelSeriesPresetLayout.XThenY);

        Assert.Equal([0x2C, 0x01, 0x01, 0x09, 0, 0, 0, 0, 0x09, 0, 0, 0, 0], writer.Build(800).Packets[0]);
    }

    [Fact]
    public void WidenedTable_UsesTwoBytes()
    {
        Assert.True(SteelSeriesModels.TryGet(0x1830, out SteelSeriesModel rival3Wireless));

        Assert.Equal([0x20, 0x01, 0x01, 0x09, 0x00], rival3Wireless.Profile.Dpi.Build(800).Packets[0]);
    }
}
