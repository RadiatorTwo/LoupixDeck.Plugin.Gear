using LoupixDeck.Plugin.Gear.Hid.Windows;

namespace LoupixDeck.Plugin.Gear.Hid;

/// <summary>Picks the HID transport for the running platform.</summary>
internal static class HidTransports
{
    public static IHidTransport Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsHidTransport();
        }

        return new UnsupportedHidTransport();
    }
}

/// <summary>Placeholder for platforms without a transport yet.</summary>
internal sealed class UnsupportedHidTransport : IHidTransport
{
    public IReadOnlyList<HidDeviceInfo> Enumerate() => [];

    public IHidConnection Open(HidDeviceInfo device)
    {
        throw new PlatformNotSupportedException("HID access is not supported on this platform.");
    }
}
