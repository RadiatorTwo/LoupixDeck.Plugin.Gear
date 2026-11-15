using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;

namespace LoupixDeck.Plugin.Gear.Providers.SteelSeries;

/// <summary>SteelSeries mice: the vendor collection on the USB interface rivalcfg names for the model.</summary>
internal sealed class SteelSeriesProvider : IGearProvider
{
    private const ushort VendorUsagePageStart = 0xFF00;

    public IReadOnlyList<IGearDevice> Match(IReadOnlyList<HidDeviceInfo> devices, IHidTransport transport)
    {
        List<IGearDevice> result = [];
        foreach (HidDeviceInfo info in devices)
        {
            if (info.VendorId != SteelSeriesModels.VendorId
                || !SteelSeriesModels.TryGet(info.ProductId, out SteelSeriesModel model)
                || info.InterfaceNumber != model.Interface
                || info.UsagePage < VendorUsagePageStart
                || info.InputLength == 0
                || info.OutputLength == 0)
            {
                continue;
            }

            result.Add(new SteelSeriesDevice(info, transport, model));
        }

        return result;
    }
}
