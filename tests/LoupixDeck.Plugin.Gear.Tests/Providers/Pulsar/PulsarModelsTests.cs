using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

public sealed class PulsarModelsTests
{
    [Theory]
    [InlineData(null, new[] { 125, 250, 500, 1000 })]
    [InlineData((byte)1, new[] { 125, 250, 500, 1000, 2000, 4000 })]
    public void AllowedRates_ListsRatesUpToTheLinkCeiling(byte? link, int[] expected) =>
        Assert.Equal(expected, PulsarModels.AllowedRates(link));

    [Fact]
    public void AllowedRates_ReturnsTheSameInstancePerLink() =>
        Assert.Same(PulsarModels.AllowedRates((byte)3), PulsarModels.AllowedRates((byte)3));
}
