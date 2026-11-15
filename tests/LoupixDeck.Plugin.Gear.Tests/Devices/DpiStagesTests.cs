using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Tests.Devices;

public sealed class DpiStagesTests
{
    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(3, 4, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 4, 0)]
    public void NextStage_Wraps(int active, int count, int expected)
    {
        Assert.Equal(expected, DpiStages.NextStage(active, count));
    }

    [Theory]
    [InlineData(3, 4, 2)]
    [InlineData(0, 4, 3)]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 4, 3)]
    public void PreviousStage_Wraps(int active, int count, int expected)
    {
        Assert.Equal(expected, DpiStages.PreviousStage(active, count));
    }
}
