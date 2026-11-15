using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Logitech;
using Xunit.Abstractions;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

/// <summary>Read-only smoke test (ping, feature lookups and getters); never calls a setter.</summary>
public sealed class LogitechHardwareSmokeTests(ITestOutputHelper output)
{
    [SkippableFact]
    [Trait("Category", "Hardware")]
    public async Task Reads_identity_rate_dpi_and_power()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("GEAR_HW") == "1");
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

        IHidTransport transport = HidTransports.Create();
        IReadOnlyList<HidDeviceInfo> infos = transport.Enumerate();
        foreach (HidDeviceInfo info in infos.Where(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00))
        {
            output.WriteLine($"collection {info.Path} pid={info.ProductId:x4} usage={info.Usage} in={info.InputLength} out={info.OutputLength} container={info.ContainerKey}");
        }

        List<IGearDevice> devices = [.. new LogitechProvider().Match(infos, transport)];
        Skip.If(devices.Count == 0, "No Logitech HID++ device plugged in.");

        try
        {
            CancellationToken ct = CancellationToken.None;
            foreach (IGearDevice device in devices)
            {
                bool awake = await device.IsAwakeAsync(ct);
                if (!awake)
                {
                    output.WriteLine($"{device.Name}: not answering (present={device.IsPresent})");
                    continue;
                }

                bool identity = await device.RefreshIdentityAsync(ct);
                output.WriteLine($"identity={identity} present={device.IsPresent} key={device.Key} name={device.Name} short={device.ShortName}");
                if (!identity)
                {
                    continue;
                }

                output.WriteLine($"awake={await device.IsAwakeAsync(ct)} link={device.Link} capabilities={device.Capabilities}");
                output.WriteLine($"allowedRates=[{string.Join(", ", device.AllowedPollingRates)}] rate={await device.ReadPollingRateAsync(ct)}");
                GearDpiState? dpi = await device.ReadDpiAsync(ct);
                output.WriteLine(dpi is { } state
                    ? $"dpi stages=[{string.Join(", ", state.Stages)}] current={state.Stages[state.ActiveStage]}"
                    : "dpi=null");
                output.WriteLine($"power={await device.ReadPowerAsync(ct)}");
                Assert.NotNull(device.Key);
            }
        }
        finally
        {
            foreach (IGearDevice device in devices)
            {
                device.Dispose();
            }
        }
    }
}
