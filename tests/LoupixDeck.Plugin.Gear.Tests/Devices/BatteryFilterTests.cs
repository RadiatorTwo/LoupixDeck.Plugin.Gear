using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Tests.Devices;

public sealed class BatteryFilterTests
{
    [Fact]
    public void Add_RejectsSingleOutlierWithMedianOfLastThree()
    {
        BatteryFilter filter = new();

        filter.Add(98);
        filter.Add(99);

        Assert.Equal(98, filter.Add(50));
    }

    [Fact]
    public void Add_WithOneValue_ReturnsThatValue()
    {
        BatteryFilter filter = new();

        Assert.Equal(42, filter.Add(42));
    }

    [Fact]
    public void Add_WithTwoValues_ReturnsLowerOfTheTwo()
    {
        BatteryFilter filter = new();

        filter.Add(90);

        Assert.Equal(80, filter.Add(80));
    }

    [Fact]
    public void Add_OnlyKeepsLastThreeValues()
    {
        BatteryFilter filter = new();

        filter.Add(10);
        filter.Add(20);
        filter.Add(30);

        Assert.Equal(30, filter.Add(31));
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        BatteryFilter filter = new();
        filter.Add(10);
        filter.Add(20);
        filter.Add(30);

        filter.Reset();

        Assert.Equal(55, filter.Add(55));
    }
}
