using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Tests.Hid;

/// <summary>Read-only hardware smoke test; runs only with GEAR_HW=1 and a plugged-in X2 dongle.</summary>
public sealed class WindowsHidSmokeTests
{
    [SkippableFact]
    [Trait("Category", "Hardware")]
    public void Status_command_gets_a_valid_reply_from_the_dongle()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("GEAR_HW") == "1");
        Skip.IfNot(OperatingSystem.IsWindows());

        IHidTransport transport = HidTransports.Create();
        HidDeviceInfo? device = transport.Enumerate().FirstOrDefault(d => d.VendorId == 0x3710 && d.ProductId == 0x5406 && d.InterfaceNumber == 1 && d.UsagePage == 0xFF02);

        Assert.NotNull(device);
        Assert.Equal(17, device.InputLength);
        Assert.Equal(17, device.OutputLength);

        using IHidConnection connection = transport.Open(device);
        connection.SendOutputReport(PulsarProtocol.Frame(PulsarProtocol.Status));

        byte[] reply = new byte[17];
        int count = connection.Read(reply, TimeSpan.FromSeconds(2.5));

        Assert.Equal(17, count);
        Assert.True(PulsarProtocol.IsValidReply(reply, PulsarProtocol.Status));
    }
}
