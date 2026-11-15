using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Tests.Rendering;

public sealed class GearMenuTests
{
    private sealed class Source(params GearDeviceState[] devices) : IGearDeviceSource
    {
        public IReadOnlyList<GearDeviceState> Devices { get; } = devices;
    }

    private static GearDeviceState Mouse(string? key, string name = "X2", GearCapabilities caps = GearCapabilities.PollingRate | GearCapabilities.Dpi, int[]? rates = null) =>
        new(key, name, GearLink.Wireless8K, caps, true, GearAvailability.Awake, 80, false, null, 1000, null, null,
            AllowedPollingRates: rates ?? [125, 250, 500, 1000, 2000, 4000, 8000]);

    private static async Task<MenuNode> Root(params GearDeviceState[] devices)
    {
        IReadOnlyList<MenuNode> nodes = await new GearMenu(new Source(devices), t => t).GetMenuNodes(ButtonTargets.TouchButton);
        return Assert.Single(nodes);
    }

    private static IEnumerable<MenuNode> Leaves(MenuNode node) =>
        node.CommandName is not null ? [node] : node.Children.SelectMany(Leaves);

    [Fact]
    public async Task OtherTargets_ReturnNothing()
    {
        Assert.Empty(await new GearMenu(new Source(Mouse("a")), t => t).GetMenuNodes(ButtonTargets.RotaryEncoder));
    }

    [Fact]
    public async Task DeviceGroup_PrefillsDeviceKey()
    {
        MenuNode root = await Root(Mouse("pulsar:model:060a"));
        MenuNode group = root.Children.Single(n => n.Name == "X2");

        List<MenuNode> leaves = Leaves(group).ToList();
        Assert.All(leaves, l => Assert.Equal("pulsar:model:060a", l.Parameters[l.CommandName == "Gear.Pages" ? "Pages" : "Device"]));
        Assert.Contains(leaves, l => l.CommandName == "Gear.PollingRate" && l.Parameters["Rate"] == "next");
        Assert.Contains(leaves, l => l.CommandName == "Gear.PollingRate" && l.Parameters["Rate"] == "8000");
        Assert.Contains(leaves, l => l.CommandName == "Gear.Dpi" && l.Parameters["Dpi"] == "previous");
    }

    [Fact]
    public async Task DeviceWithoutDpi_HasNoDpiEntries()
    {
        MenuNode root = await Root(Mouse("a", caps: GearCapabilities.PollingRate));

        Assert.DoesNotContain(Leaves(root.Children.Single(n => n.Name == "X2")), l => l.CommandName == "Gear.Dpi");
    }

    [Fact]
    public async Task DeviceWithoutKey_IsSkipped_AndDeviceIndependentEntriesRemain()
    {
        MenuNode root = await Root(Mouse(null));

        Assert.DoesNotContain(root.Children, n => n.Name == "X2");
        Assert.Contains(Leaves(root), l => l.CommandName == "Gear.PollingRate" && !l.Parameters.ContainsKey("Device"));
        Assert.Contains(Leaves(root), l => l.CommandName == "Gear.Dpi" && l.Parameters["Dpi"] == "next" && !l.Parameters.ContainsKey("Device"));
    }

    [Fact]
    public async Task TwoMiceOfOneModel_GetDistinctGroupNames()
    {
        MenuNode root = await Root(Mouse("a"), Mouse("b"));

        Assert.Equal(["X2 1", "X2 2"], root.Children.Where(n => n.Name.StartsWith("X2")).Select(n => n.Name));
    }

    [Fact]
    public async Task DeviceOnA1KLink_OffersOnlyItsAllowedRates()
    {
        MenuNode root = await Root(Mouse("a", rates: [125, 250, 500, 1000]));

        List<string> rates = Leaves(root.Children.Single(n => n.Name == "X2"))
            .Where(l => l.CommandName == "Gear.PollingRate").Select(l => l.Parameters["Rate"]).ToList();
        Assert.Equal(["next", "125", "250", "500", "1000"], rates);
    }

    [Fact]
    public async Task DeviceIndependentGroup_OffersNextOnly()
    {
        MenuNode root = await Root(Mouse("a"));

        List<string> rates = root.Children.Where(n => n.Name != "X2").SelectMany(Leaves)
            .Where(l => l.CommandName == "Gear.PollingRate").Select(l => l.Parameters["Rate"]).ToList();
        Assert.Equal(["next"], rates);
    }
}
