using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;
using Xunit.Abstractions;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Pulsar;

/// <summary>Read-only smoke test (Status, Power, Info, MemGet); never sends a write command.</summary>
public sealed class PulsarHardwareSmokeTests(ITestOutputHelper output)
{
    [SkippableFact]
    [Trait("Category", "Hardware")]
    public async Task Reads_status_power_info_and_polling_rate()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("GEAR_HW") == "1");
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

        IHidTransport transport = HidTransports.Create();
        IGearDevice? found = new PulsarProvider().Match(transport.Enumerate(), transport).FirstOrDefault(d => d.Tested);
        Skip.If(found is null, "No tested Pulsar device plugged in.");

        using PulsarDevice device = Assert.IsType<PulsarDevice>(found);
        CancellationToken ct = CancellationToken.None;

        output.WriteLine($"awake={await device.IsAwakeAsync(ct)}");

        bool info = await device.RefreshInfoAsync(ct);
        output.WriteLine($"info={info} key={device.Key} name={device.Name} link={device.Link} nonceEchoed={device.InfoNonceEchoed}");
        output.WriteLine($"infoReply={Convert.ToHexString(device.LastInfoReply ?? [])}");

        GearPowerReading? power = await device.ReadPowerAsync(ct);
        if (power is null)
        {
            output.WriteLine("power: no reply, retrying once");
            power = await device.ReadPowerAsync(ct);
        }

        output.WriteLine($"power={power}");
        output.WriteLine($"pollingRate={await device.ReadPollingRateAsync(ct)}");
        Assert.True(info);
    }
}
