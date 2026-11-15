using System.Globalization;

namespace LoupixDeck.Plugin.Gear.Hid.Linux;

/// <summary>HID transport on top of the kernel hidraw nodes.</summary>
internal sealed class LinuxHidrawTransport : IHidTransport
{
    private const string ClassPath = "/sys/class/hidraw";
    private const string DevPath = "/dev";

    private readonly IHidrawSysfs _sysfs;
    private readonly Func<string, IHidConnection> _openNode;

    public LinuxHidrawTransport()
        : this(new HidrawSysfs(), LinuxHidrawConnection.Open)
    {
    }

    public LinuxHidrawTransport(IHidrawSysfs sysfs, Func<string, IHidConnection> openNode)
    {
        _sysfs = sysfs;
        _openNode = openNode;
    }

    /// <summary>
    /// One entry per top-level collection of every hidraw node. A node carries a whole USB
    /// interface, so all its entries share the path and the interface's container key.
    /// </summary>
    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        List<HidDeviceInfo> devices = [];
        foreach (string name in _sysfs.ListHidrawNames())
        {
            try
            {
                devices.AddRange(Probe(name));
            }
            catch (Exception)
            {
                // One unreadable or odd node must not hide the others.
            }
        }

        return devices;
    }

    public IHidConnection Open(HidDeviceInfo device)
    {
        try
        {
            return _openNode(device.Path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HidAccessException($"Access to HID device '{device.Path}' was denied.", ex);
        }
    }

    private List<HidDeviceInfo> Probe(string name)
    {
        string deviceDir = $"{ClassPath}/{name}/device";
        string? uevent = _sysfs.ReadText($"{deviceDir}/uevent");
        byte[]? descriptor = _sysfs.ReadBytes($"{deviceDir}/report_descriptor");
        if (uevent is null || descriptor is null)
        {
            return [];
        }

        Dictionary<string, string> properties = ParseUevent(uevent);
        if (!properties.TryGetValue("HID_ID", out string? hidId) || !TryParseHidId(hidId, out ushort vendorId, out ushort productId))
        {
            return [];
        }

        properties.TryGetValue("HID_UNIQ", out string? serial);
        properties.TryGetValue("HID_NAME", out string? productName);

        // The HID device sits below the USB interface directory, which holds bInterfaceNumber.
        string? hidDevice = _sysfs.RealPath(deviceDir);
        string? usbInterface = hidDevice is null ? null : ParentOf(hidDevice);
        string? interfaceText = usbInterface is null ? null : _sysfs.ReadText($"{usbInterface}/bInterfaceNumber");
        int interfaceNumber = -1;
        if (interfaceText is not null
            && int.TryParse(interfaceText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed))
        {
            interfaceNumber = parsed;
        }

        string containerKey = interfaceText is not null && usbInterface is not null
            ? usbInterface
            : hidDevice ?? $"{ClassPath}/{name}";

        List<HidDeviceInfo> infos = [];
        foreach (ReportDescriptor.Collection collection in ReportDescriptor.Collections(descriptor))
        {
            infos.Add(new HidDeviceInfo(
                $"{DevPath}/{name}",
                vendorId,
                productId,
                interfaceNumber,
                collection.UsagePage,
                collection.Usage,
                collection.InputLength,
                collection.OutputLength,
                string.IsNullOrEmpty(serial) ? null : serial,
                string.IsNullOrEmpty(productName) ? null : productName,
                containerKey));
        }

        return infos;
    }

    private static string? ParentOf(string path)
    {
        int slash = path.TrimEnd('/').LastIndexOf('/');
        return slash > 0 ? path[..slash] : null;
    }

    private static Dictionary<string, string> ParseUevent(string text)
    {
        Dictionary<string, string> result = [];
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                result[line[..eq]] = line[(eq + 1)..].Trim();
            }
        }

        return result;
    }

    /// <summary>Parses <c>HID_ID</c>, formatted as <c>bus:vendor:product</c> in hex (e.g. 0003:00003710:00005406).</summary>
    private static bool TryParseHidId(string value, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        string[] parts = value.Split(':');
        return parts.Length == 3
            && ushort.TryParse(parts[1].TrimStart('0').PadLeft(1, '0'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId)
            && ushort.TryParse(parts[2].TrimStart('0').PadLeft(1, '0'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId);
    }
}
