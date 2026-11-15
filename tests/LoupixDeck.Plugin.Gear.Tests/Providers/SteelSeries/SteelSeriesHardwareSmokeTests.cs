using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.SteelSeries;
using Xunit.Abstractions;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.SteelSeries;

/// <summary>Read-only smoke test (battery query only); never sends a write command.</summary>
public sealed class SteelSeriesHardwareSmokeTests(ITestOutputHelper output)
{
    [SkippableFact]
    [Trait("Category", "Hardware")]
    public async Task Reads_battery()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("GEAR_HW") == "1");
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

        IHidTransport transport = HidTransports.Create();
        IGearDevice? found = new SteelSeriesProvider().Match(transport.Enumerate(), transport)
            .FirstOrDefault(d => d.Tested && d.Capabilities.HasFlag(GearCapabilities.Battery));
        Skip.If(found is null, "No tested SteelSeries mouse with a battery plugged in.");

        using IGearDevice device = found;
        CancellationToken ct = CancellationToken.None;

        output.WriteLine($"key={device.Key} name={device.Name} link={device.Link} awake={await device.IsAwakeAsync(ct)}");
        GearPowerReading? power = await device.ReadPowerAsync(ct);
        output.WriteLine($"power={power}");
        Assert.NotNull(power);
    }
}
