using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

public sealed class PulsarBatteryTests
{
    [Theory]
    [InlineData(4101, 98)]
    [InlineData(4105, 99)]
    [InlineData(4110, 100)]
    [InlineData(5000, 100)]
    [InlineData(3050, 0)]
    [InlineData(2000, 0)]
    [InlineData(3600, 20)]
    [InlineData(3630, 22)]
    public void PercentFromMillivolts_MatchesPulsarApp(int millivolts, int expectedPercent)
    {
        Assert.Equal(expectedPercent, PulsarBattery.PercentFromMillivolts(millivolts));
    }
}
