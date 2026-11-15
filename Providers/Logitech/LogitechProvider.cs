using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>
/// Logitech mice over HID++ 2.0: through a Unifying, Bolt or LIGHTSPEED receiver (slots 1..6) or directly on a cable.
/// The short HID++ collection (report 0x10) of an interface is the anchor; the long one is found by the link.
/// </summary>
internal sealed class LogitechProvider : IGearProvider
{
    private const ushort LogitechVendorId = 0x046D;
    private const ushort VendorUsagePage = 0xFF00;
    private const byte ReceiverSlots = 6;

    // Unifying, Bolt and LIGHTSPEED receivers: the device is addressed by its slot.
    private static readonly HashSet<ushort> ReceiverProductIds =
    [
        0xC52B, 0xC532, // Unifying
        0xC548, // Bolt
        0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC54D, // LIGHTSPEED
    ];

    public IReadOnlyList<IGearDevice> Match(IReadOnlyList<HidDeviceInfo> devices, IHidTransport transport)
    {
        List<IGearDevice> result = [];
        foreach (HidDeviceInfo info in devices)
        {
            if (info.VendorId != LogitechVendorId
                || info.UsagePage != VendorUsagePage
                || info.InputLength != HidppProtocol.ShortLength)
            {
                continue;
            }

            HidppLink link = new(info, transport);
            if (ReceiverProductIds.Contains(info.ProductId))
            {
                for (byte slot = 1; slot <= ReceiverSlots; slot++)
                {
                    result.Add(new LogitechDevice(info, link, slot, viaReceiver: true));
                }
            }
            else
            {
                result.Add(new LogitechDevice(info, link, HidppProtocol.DirectDeviceIndex, viaReceiver: false));
            }
        }

        return result;
    }
}
