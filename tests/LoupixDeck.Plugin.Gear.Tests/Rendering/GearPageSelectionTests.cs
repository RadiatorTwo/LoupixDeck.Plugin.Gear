using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Tests.Rendering;

public sealed class GearPageSelectionTests
{
    private static GearDeviceState Device(string? key) =>
        new(key, key ?? "unknown", GearLink.Wired, GearCapabilities.Battery, true, GearAvailability.Awake, 50, false, null, 1000, null, null);

    private static readonly IReadOnlyList<GearDeviceState> Devices = [Device("a"), Device(null), Device("b")];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void EmptyOrAll_ListsEveryDeviceThenOverview(string? selection)
    {
        IReadOnlyList<GearPage> pages = GearPageSelection.Resolve([selection], Devices);

        Assert.Equal(4, pages.Count);
        Assert.Equal(Devices, pages.Take(3).Select(p => p.Device));
        Assert.Null(pages[3].Device);
    }

    [Fact]
    public void Tokens_AreResolvedInOrderAndUnknownKeysSkipped()
    {
        IReadOnlyList<GearPage> pages = GearPageSelection.Resolve(["b|missing|overview|A"], Devices);

        Assert.Equal(["b", null, "a"], pages.Select(p => p.Device?.Key));
    }

    [Fact]
    public void ChainedSelections_AddUp()
    {
        IReadOnlyList<GearPage> pages = GearPageSelection.Resolve(["a", "overview"], Devices);

        Assert.Equal(2, pages.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("overview")]
    [InlineData("overview|a")]
    public void NoDevices_ResolvesToNoPages(string? selection)
    {
        Assert.Empty(GearPageSelection.Resolve([selection], []));
    }
}
