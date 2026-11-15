namespace LoupixDeck.Plugin.Gear.Tests;

public sealed class GearPluginTests
{
    [Fact]
    public void Metadata_MatchesManifest()
    {
        GearPlugin plugin = new();

        Assert.Equal("gear", plugin.Metadata.Id);
        Assert.Equal(new Version(1, 0, 0), plugin.Metadata.Version);
        Assert.NotNull(plugin.Metadata.Icon);
        Assert.Empty(plugin.GetCommands());
    }

    [Fact]
    public void CommandGroup_HasTheMouseIcon()
    {
        LoupixDeck.PluginSdk.CommandGroupDescriptor group = Assert.Single(new GearPlugin().GetCommandGroups());

        Assert.Equal("Gear", group.Group);
        Assert.Equal("\U000F037D", group.Icon);
    }

    [Fact]
    public void Commands_AreOnlyReachableThroughTheMenu()
    {
        LoupixDeck.PluginSdk.CommandDescriptor[] descriptors =
        [
            new Commands.GearPagesCommand(new NoDevices(), t => t).Descriptor,
            new Commands.PollingRateCommand(new NoDevices(), t => t, null!).Descriptor,
            new Commands.DpiCommand(new NoDevices(), t => t, null!).Descriptor,
        ];

        Assert.All(descriptors, d => Assert.True(d.HiddenFromMenu));
        Assert.All(descriptors, d => Assert.Equal("\U000F037D", d.Icon));
    }

    private sealed class NoDevices : LoupixDeck.Plugin.Gear.Devices.IGearDeviceControl
    {
        public IReadOnlyList<LoupixDeck.Plugin.Gear.Devices.GearDeviceState> Devices { get; } = [];
        public LoupixDeck.Plugin.Gear.Devices.GearDeviceState? Find(string? key) => null;
        public Task<bool> SetPollingRateAsync(string? key, int hz) => Task.FromResult(false);
        public Task<bool> CyclePollingRateAsync(string? key) => Task.FromResult(false);
        public Task<bool> SetDpiAsync(string? key, int dpi) => Task.FromResult(false);
        public Task<bool> StepDpiAsync(string? key, int direction) => Task.FromResult(false);
    }
}
