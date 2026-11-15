using System.Collections.Concurrent;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

internal sealed class PulsarProvider : IGearProvider
{
    // Parsed Info replies per USB interface: a sleeping mouse may not answer, but its identity does not change.
    private readonly ConcurrentDictionary<string, PulsarInfo> _identities = new();

    public IReadOnlyList<IGearDevice> Match(IReadOnlyList<HidDeviceInfo> devices, IHidTransport transport)
    {
        List<IGearDevice> result = [];
        foreach (HidDeviceInfo info in devices)
        {
            if (info.UsagePage != PulsarModels.VendorUsagePage
                || info.InputLength != PulsarModels.VendorReportLength
                || info.OutputLength != PulsarModels.VendorReportLength
                || !PulsarModels.TryGetReceiver(info.VendorId, info.ProductId, out string name, out bool tested))
            {
                continue;
            }

            result.Add(new PulsarDevice(info, transport, name, tested, _identities));
        }

        return result;
    }
}
