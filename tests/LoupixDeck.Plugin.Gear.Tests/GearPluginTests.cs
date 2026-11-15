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
}
