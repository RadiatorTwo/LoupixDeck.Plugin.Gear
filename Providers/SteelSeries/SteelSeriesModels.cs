using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Providers.SteelSeries;

internal enum SteelSeriesBattery
{
    None,

    /// <summary>Reply byte 0 is the level in percent, byte 2 is non-zero while charging (Rival 650, Rival 3 Wireless).</summary>
    Percent,

    /// <summary>Reply byte 1: bit 7 is the charging flag, bits 0..6 are the level in 5 % steps plus one (Aerox, Prime Wireless).</summary>
    Steps,
}

/// <summary>The command set of one family. Every command is an output report with report id 0.</summary>
/// <param name="PollingCodes">Wire code per polling rate in Hz.</param>
internal sealed record SteelSeriesProfile(
    SteelSeriesBattery Battery,
    byte[] BatteryCommand,
    byte[] PollingCommand,
    IReadOnlyDictionary<int, byte> PollingCodes,
    SteelSeriesDpiWriter Dpi);

/// <param name="Interface">USB interface number of the vendor collection.</param>
internal sealed record SteelSeriesModel(ushort ProductId, string Name, int Interface, GearLink Link, SteelSeriesProfile Profile, bool Tested = false);

/// <summary>
/// Static knowledge about SteelSeries mice. Product ids, interfaces, commands, value encodings and the sensor tables
/// are ported from flozz/rivalcfg (WTFPL, <c>rivalcfg/devices/</c>); see THIRD-PARTY-NOTICES.md. Only the Rival 650
/// (wired) was verified on real hardware.
/// </summary>
internal static class SteelSeriesModels
{
    public const ushort VendorId = 0x1038;

    private static readonly IReadOnlyDictionary<int, byte> StandardRates = new Dictionary<int, byte>
    {
        [125] = 0x04, [250] = 0x03, [500] = 0x02, [1000] = 0x01,
    };

    private static readonly IReadOnlyDictionary<int, byte> ZeroBasedRates = new Dictionary<int, byte>
    {
        [125] = 0x03, [250] = 0x02, [500] = 0x01, [1000] = 0x00,
    };

    /// <summary>Two-slot mice: 100..12000 DPI, code 0..119.</summary>
    private static readonly SteelSeriesDpiCodec Range12K = SteelSeriesDpiCodec.Range(100, 12000, 100, 0);

    /// <summary>Prime and Sensei TEN: 50..18000 DPI, code 1..360 in two bytes.</summary>
    private static readonly SteelSeriesDpiCodec Range18K = SteelSeriesDpiCodec.Range(50, 18000, 50, 1, byteLength: 2);

    /// <summary>Suffix of the DPI packets of the Rival 310/500/600/700 and Sensei 310.</summary>
    private static readonly byte[] SlotSuffix = [0x00, 0x42];

    private static readonly byte[] PercentBattery = [0xAA, 0x01];

    private static SteelSeriesProfile Profile(byte[] pollingCommand, SteelSeriesDpiWriter dpi, IReadOnlyDictionary<int, byte>? rates = null) =>
        new(SteelSeriesBattery.None, [], pollingCommand, rates ?? StandardRates, dpi);

    private static SteelSeriesDpiWriter Slots(byte[] slot1, byte[] slot2, SteelSeriesDpiCodec codec, byte[]? suffix = null) =>
        new SteelSeriesSlotDpiWriter(slot1, slot2, codec, suffix);

    private static SteelSeriesDpiWriter Presets(byte[] command, SteelSeriesDpiCodec codec, int firstPreset, SteelSeriesPresetLayout layout = SteelSeriesPresetLayout.Single) =>
        new SteelSeriesPresetDpiWriter(command, codec, firstPreset, layout);

    private static class Profiles
    {
        public static readonly SteelSeriesProfile Aerox3 = Profile([0x2B], Presets([0x2D], SteelSeriesSensors.TrueMoveCore, 1));

        // Aerox 5 and Rival 5.
        public static readonly SteelSeriesProfile Aerox5 = Profile([0x2B], Presets([0x2D], SteelSeriesSensors.TrueMoveAir, 0));

        // Aerox 3/5/9 Wireless and Prime (Mini) Wireless on the cable.
        public static readonly SteelSeriesProfile WirelessFamilyWired = Profile([0x2B], Presets([0x2D], SteelSeriesSensors.TrueMoveAir, 0), ZeroBasedRates)
            with { Battery = SteelSeriesBattery.Steps, BatteryCommand = [0x92] };

        // The same mice through their dongle: every command has bit 0x40 set.
        public static readonly SteelSeriesProfile WirelessFamilyDongle = Profile([0x6B], Presets([0x6D], SteelSeriesSensors.TrueMoveAir, 0), ZeroBasedRates)
            with { Battery = SteelSeriesBattery.Steps, BatteryCommand = [0xD2] };

        public static readonly SteelSeriesProfile Kanav2 = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02],
            SteelSeriesDpiCodec.Choices((400, 8), (800, 7), (1200, 6), (1600, 5), (2000, 4), (2400, 3), (3200, 2), (4000, 1))));

        public static readonly SteelSeriesProfile Kinzuv2 = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02],
            SteelSeriesDpiCodec.Choices((400, 4), (800, 3), (1600, 2), (3200, 1))));

        // Prime and Prime+.
        public static readonly SteelSeriesProfile Prime = Profile([0x5D], Presets([0x61], Range18K, 0));

        public static readonly SteelSeriesProfile PrimeMini = Profile([0x2B], Presets([0x2D], Range18K, 0));

        // Rival 100/105 and Rival 95.
        public static readonly SteelSeriesProfile Rival100 = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02],
            SteelSeriesDpiCodec.Choices((250, 8), (500, 7), (1000, 6), (1250, 5), (1500, 4), (1750, 3), (2000, 2), (4000, 1))));

        // Rival 110/106 and Rival 300S.
        public static readonly SteelSeriesProfile Rival110 = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02], SteelSeriesSensors.Pmw3326));

        public static readonly SteelSeriesProfile Rival3 = Profile([0x04, 0x00], Presets([0x0B, 0x00], SteelSeriesSensors.TrueMoveCore, 1));

        public static readonly SteelSeriesProfile Rival300 = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02], SteelSeriesDpiCodec.Range(50, 6500, 50, 1)));

        // Rival 310 and Sensei 310.
        public static readonly SteelSeriesProfile Rival310 = Profile([0x54, 0x00], Slots([0x53, 0x00, 0x01], [0x53, 0x00, 0x02], Range12K, SlotSuffix));

        public static readonly SteelSeriesProfile Rival3Gen2 = Profile([0x2B], Presets([0x34], SteelSeriesSensors.TrueMoveCore, 0, SteelSeriesPresetLayout.XyInterleaved));

        public static readonly SteelSeriesProfile Rival3Wireless = Profile([0x17], Presets([0x20], SteelSeriesSensors.TrueMoveAir.WithByteLength(2), 1), ZeroBasedRates)
            with { Battery = SteelSeriesBattery.Percent, BatteryCommand = PercentBattery };

        public static readonly SteelSeriesProfile Rival3WirelessGen2 = Profile([0x17], Presets([0x2C], SteelSeriesSensors.TrueMoveAir, 1, SteelSeriesPresetLayout.XThenY), ZeroBasedRates)
            with { Battery = SteelSeriesBattery.Percent, BatteryCommand = PercentBattery };

        // Rival 500, 600 and 700/710.
        public static readonly SteelSeriesProfile Rival500 = Profile([0x04, 0x00], Slots([0x03, 0x00, 0x01], [0x03, 0x00, 0x02], Range12K, SlotSuffix));

        public static readonly SteelSeriesProfile Rival650 = Profile([0x17], Slots([0x15, 0x01], [0x15, 0x02], Range12K))
            with { Battery = SteelSeriesBattery.Percent, BatteryCommand = PercentBattery };

        public static readonly SteelSeriesProfile SenseiRaw = Profile([0x04, 0x00], Slots([0x03, 0x01], [0x03, 0x02], SteelSeriesDpiCodec.Range(90, 5670, 90, 1)));

        public static readonly SteelSeriesProfile SenseiTen = Profile([0x54, 0x00], Presets([0x55, 0x00], Range18K, 1));
    }

    private static readonly SteelSeriesModel[] All =
    [
        new(0x1836, "SteelSeries Aerox 3", 3, GearLink.Wired, Profiles.Aerox3),
        new(0x183A, "SteelSeries Aerox 3 Wireless", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x187A, "SteelSeries Aerox 3 Wireless CS2 Dragon Lore Edition", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1838, "SteelSeries Aerox 3 Wireless", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1878, "SteelSeries Aerox 3 Wireless CS2 Dragon Lore Edition", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1850, "SteelSeries Aerox 5", 3, GearLink.Wired, Profiles.Aerox5),
        new(0x1854, "SteelSeries Aerox 5 Wireless", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x185E, "SteelSeries Aerox 5 Wireless Destiny 2 Edition", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1862, "SteelSeries Aerox 5 Wireless Diablo IV Edition", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1852, "SteelSeries Aerox 5 Wireless", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x185C, "SteelSeries Aerox 5 Wireless Destiny 2 Edition", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1860, "SteelSeries Aerox 5 Wireless Diablo IV Edition", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x185A, "SteelSeries Aerox 9 Wireless", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1876, "SteelSeries Aerox 9 Wireless WOW Edition", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1858, "SteelSeries Aerox 9 Wireless", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1874, "SteelSeries Aerox 9 Wireless WOW Edition", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x137A, "SteelSeries Kana v2", 0, GearLink.Wired, Profiles.Kanav2),
        new(0x1366, "SteelSeries Kinzu v2", 0, GearLink.Wired, Profiles.Kinzuv2),
        new(0x1378, "SteelSeries Kinzu v2", 0, GearLink.Wired, Profiles.Kinzuv2),
        new(0x182E, "SteelSeries Prime", 0, GearLink.Wired, Profiles.Prime),
        new(0x182A, "SteelSeries Prime Rainbow 6 Siege Black Ice Edition", 0, GearLink.Wired, Profiles.Prime),
        new(0x1856, "SteelSeries Prime CS:GO Neo Noir Edition", 0, GearLink.Wired, Profiles.Prime),
        new(0x184D, "SteelSeries Prime Mini", 3, GearLink.Wired, Profiles.PrimeMini),
        new(0x182C, "SteelSeries Prime+", 0, GearLink.Wired, Profiles.Prime),
        new(0x1842, "SteelSeries Prime Wireless", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x184A, "SteelSeries Prime Mini Wireless", 3, GearLink.Wired, Profiles.WirelessFamilyWired),
        new(0x1840, "SteelSeries Prime Wireless", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1848, "SteelSeries Prime Mini Wireless", 3, GearLink.Wireless1K, Profiles.WirelessFamilyDongle),
        new(0x1702, "SteelSeries Rival 100", 0, GearLink.Wired, Profiles.Rival100),
        new(0x170A, "SteelSeries Rival 100 (Dell China)", 0, GearLink.Wired, Profiles.Rival100),
        new(0x170B, "SteelSeries Rival 100 Dota 2 Edition (retail)", 0, GearLink.Wired, Profiles.Rival100),
        new(0x170C, "SteelSeries Rival 100 Dota 2 Edition (Lenovo)", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1814, "SteelSeries Rival 105", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1729, "SteelSeries Rival 110", 0, GearLink.Wired, Profiles.Rival110),
        new(0x1816, "SteelSeries Rival 106", 0, GearLink.Wired, Profiles.Rival110),
        new(0x1824, "SteelSeries Rival 3", 3, GearLink.Wired, Profiles.Rival3),
        new(0x184C, "SteelSeries Rival 3 (firmware v0.37.0.0)", 3, GearLink.Wired, Profiles.Rival3),
        new(0x1384, "SteelSeries Rival", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1392, "SteelSeries Rival Dota 2 Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1710, "SteelSeries Rival 300", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1712, "SteelSeries Rival 300 Fallout 4 Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x171C, "SteelSeries Rival 300 Evil Geniuses Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1394, "SteelSeries Rival 300 CS:GO Fade Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x171A, "SteelSeries Rival 300 CS:GO Hyper Beast Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1716, "SteelSeries Rival 300 CS:GO Fade Edition (stm32)", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1714, "SteelSeries Rival 300 Acer Predator Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1718, "SteelSeries Rival 300 HP OMEN Edition", 0, GearLink.Wired, Profiles.Rival300),
        new(0x1810, "SteelSeries Rival 300S", 0, GearLink.Wired, Profiles.Rival110),
        new(0x1720, "SteelSeries Rival 310", 0, GearLink.Wired, Profiles.Rival310),
        new(0x171E, "SteelSeries Rival 310 CS:GO Howl Edition", 0, GearLink.Wired, Profiles.Rival310),
        new(0x1736, "SteelSeries Rival 310 PUBG Edition", 0, GearLink.Wired, Profiles.Rival310),
        new(0x1870, "SteelSeries Rival 3 Gen 2", 3, GearLink.Wired, Profiles.Rival3Gen2),
        new(0x1830, "SteelSeries Rival 3 Wireless", 3, GearLink.Wireless1K, Profiles.Rival3Wireless),
        new(0x1872, "SteelSeries Rival 3 Wireless Gen 2", 3, GearLink.Wireless1K, Profiles.Rival3WirelessGen2),
        new(0x183C, "SteelSeries Rival 5", 0, GearLink.Wired, Profiles.Aerox5),
        new(0x183E, "SteelSeries Rival 5 Destiny Edition", 0, GearLink.Wired, Profiles.Aerox5),
        new(0x170E, "SteelSeries Rival 500", 0, GearLink.Wired, Profiles.Rival500),
        new(0x1724, "SteelSeries Rival 600", 0, GearLink.Wired, Profiles.Rival500),
        new(0x172E, "SteelSeries Rival 600 Dota 2 Edition", 0, GearLink.Wired, Profiles.Rival500),
        new(0x172B, "SteelSeries Rival 650 Wireless", 0, GearLink.Wired, Profiles.Rival650, Tested: true),
        new(0x1726, "SteelSeries Rival 650 Wireless", 0, GearLink.Wireless1K, Profiles.Rival650),
        new(0x1700, "SteelSeries Rival 700", 0, GearLink.Wired, Profiles.Rival500),
        new(0x1730, "SteelSeries Rival 710", 0, GearLink.Wired, Profiles.Rival500),
        new(0x1706, "SteelSeries Rival 95", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1707, "SteelSeries Rival 95 MSI Edition", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1704, "SteelSeries Rival 95 PC Bang", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1708, "SteelSeries Rival 100 PC Bang", 0, GearLink.Wired, Profiles.Rival100),
        new(0x1722, "SteelSeries Sensei 310", 0, GearLink.Wired, Profiles.Rival310),
        new(0x1369, "SteelSeries Sensei [RAW]", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x1362, "SteelSeries Sensei [RAW] Diablo III Edition", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x136D, "SteelSeries Sensei [RAW] Guild Wars 2 Edition", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x136F, "SteelSeries Sensei [RAW] CoD Black Ops II Edition", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x1380, "SteelSeries Sensei [RAW] World of Tanks Edition", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x1390, "SteelSeries Sensei [RAW] Heroes of the Storm Edition", 0, GearLink.Wired, Profiles.SenseiRaw),
        new(0x1832, "SteelSeries Sensei TEN", 0, GearLink.Wired, Profiles.SenseiTen),
        new(0x1834, "SteelSeries Sensei TEN CS:GO Neon Rider Edition", 0, GearLink.Wired, Profiles.SenseiTen),
    ];

    private static readonly Dictionary<ushort, SteelSeriesModel> ByProductId = All.ToDictionary(m => m.ProductId);

    public static IReadOnlyList<SteelSeriesModel> Models => All;

    public static bool TryGet(ushort productId, out SteelSeriesModel model) => ByProductId.TryGetValue(productId, out model!);

    /// <summary>The model name without the "SteelSeries " vendor prefix.</summary>
    public static string ShortName(string name)
    {
        const string prefix = "SteelSeries ";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length ? name[prefix.Length..] : name;
    }
}
