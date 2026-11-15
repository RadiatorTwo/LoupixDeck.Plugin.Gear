namespace LoupixDeck.Plugin.Gear.Hid;

/// <summary>One HID top-level collection of a device.</summary>
/// <param name="ContainerKey">
/// Groups the collections of the same USB interface (Windows: instance id without the
/// <c>&amp;COLnn</c> part; Linux: sysfs USB path).
/// </param>
internal sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    int InterfaceNumber,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    string? SerialNumber,
    string? ProductName,
    string ContainerKey);
