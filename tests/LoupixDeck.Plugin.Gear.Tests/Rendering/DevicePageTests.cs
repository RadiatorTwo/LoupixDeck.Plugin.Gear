using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Rendering;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Tests.Rendering;

public sealed class DevicePageTests
{
    private static readonly PixelPalette P = PixelPalette.Default;

    private static GearDeviceState Device(
        GearAvailability availability = GearAvailability.Awake,
        int? percent = 98,
        bool charging = false,
        string? key = "pulsar:model:060a",
        string name = "X2 CrazyLight",
        int? rate = 4000,
        GearDpiState? dpi = null,
        GearLink link = GearLink.Wireless8K) =>
        new(key, name, link, GearCapabilities.Battery, true, availability, percent, charging, null, rate,
            dpi ?? new GearDpiState([800, 1600, 3200], 1), DateTimeOffset.UnixEpoch);

    private static PixelSurface Render(GearDeviceState device, string? index = "1/3", bool blinkOn = true)
    {
        PixelSurface surface = new(90, 90, P);
        DevicePage.Draw(surface, device, index, blinkOn, text => text);
        return surface;
    }

    private static uint Pixel(PixelSurface s, int x, int y) => s.Pixels[(y * s.Width) + x];

    private static GearDeviceState[] AllStates() =>
    [
        Device(),
        Device(GearAvailability.Asleep),
        Device(GearAvailability.NoAccess, percent: null, rate: null, link: GearLink.Unknown),
        Device(GearAvailability.Error, percent: null),
        Device(percent: null, rate: null, link: GearLink.Unknown) with { Dpi = null },
        Device(charging: true, percent: 5),
        Device(key: null, name: "A very long mouse name that does not fit"),
        Device(percent: 100),
        Device(percent: 0),
    ];

    [Fact]
    public void DevicePage_AllStates_DoNotThrow()
    {
        foreach (GearDeviceState device in AllStates())
        {
            PixelSurface first = Render(device);
            PixelSurface second = Render(device);

            Assert.Equal(first.ContentHash(), second.ContentHash());
            Assert.NotEqual(new PixelSurface(90, 90, P).ContentHash(), first.ContentHash());
        }
    }

    [Fact]
    public void DevicePage_LowBattery_UsesCriticalHeader()
    {
        PixelSurface surface = Render(Device(percent: 8), blinkOn: true);

        Assert.Equal(P.Critical, Pixel(surface, TileDrawing.HeaderX, TileDrawing.HeaderY));
    }

    [Fact]
    public void DevicePage_Charging_OverridesLowBatteryHeader()
    {
        PixelSurface surface = Render(Device(percent: 8, charging: true));

        Assert.Equal(P.Track, Pixel(surface, TileDrawing.HeaderX, TileDrawing.HeaderY));
    }

    [Fact]
    public void DevicePage_Charging_DrawsBolt()
    {
        long idle = Render(Device(percent: 50)).ContentHash();
        long charging = Render(Device(percent: 50, charging: true)).ContentHash();

        Assert.NotEqual(idle, charging);
    }

    [Fact]
    public void DevicePage_MidBattery_UsesWarnHeader()
    {
        Assert.Equal(P.Warn, Pixel(Render(Device(percent: 20)), TileDrawing.HeaderX, TileDrawing.HeaderY));
        Assert.Equal(P.Track, Pixel(Render(Device(percent: 21)), TileDrawing.HeaderX, TileDrawing.HeaderY));
    }

    [Fact]
    public void DevicePage_Asleep_ChangesLinkRowAndDimsHero()
    {
        Assert.NotEqual(Render(Device()).ContentHash(), Render(Device(GearAvailability.Asleep)).ContentHash());
    }

    [Fact]
    public void DevicePage_TextsGoThroughTranslation()
    {
        PixelSurface plain = Render(Device(GearAvailability.Asleep));
        PixelSurface translated = new(90, 90, P);
        DevicePage.Draw(translated, Device(GearAvailability.Asleep), "1/3", true, text => text == "SLEEP" ? "RUHE" : text);

        Assert.NotEqual(plain.ContentHash(), translated.ContentHash());
    }

    [Fact]
    public void Overview_ManyDevices_ClipsRows()
    {
        List<GearDeviceState> many = Enumerable.Range(0, 12).Select(i => Device(key: $"k{i}", name: $"Mouse {i}")).ToList();
        PixelSurface all = new(90, 90, P);
        OverviewPage.Draw(all, many, "1/2", true, text => text);
        PixelSurface six = new(90, 90, P);
        OverviewPage.Draw(six, many.Take(6).ToList(), "1/2", true, text => text);

        Assert.Equal(six.ContentHash(), all.ContentHash());
        Assert.NotEqual(new PixelSurface(90, 90, P).ContentHash(), all.ContentHash());
        for (int y = PixelSurface.Bottom + 1; y < 90; y++)
        {
            for (int x = 0; x < 90; x++)
            {
                Assert.Equal(P.Background, Pixel(all, x, y));
            }
        }
    }

    [Fact]
    public void Overview_NoDevices_DoesNotThrow()
    {
        PixelSurface surface = new(90, 90, P);
        OverviewPage.Draw(surface, [], null, true, text => text);
    }
}
