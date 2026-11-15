using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Hid.Linux;

namespace LoupixDeck.Plugin.Gear.Tests.Hid;

public sealed class LinuxHidrawTransportTests
{
    private const string Interface1 = "/sys/devices/pci0000:00/0000:00:14.0/usb1/1-3/1-3:1.1";

    private sealed class FakeSysfs : IHidrawSysfs
    {
        public Dictionary<string, string> Text { get; } = [];
        public Dictionary<string, byte[]> Bytes { get; } = [];
        public Dictionary<string, string> Real { get; } = [];
        public List<string> Nodes { get; } = [];

        public IReadOnlyList<string> ListHidrawNames() => Nodes;
        public string? ReadText(string path) => Text.GetValueOrDefault(path);
        public byte[]? ReadBytes(string path) => Bytes.GetValueOrDefault(path);
        public string? RealPath(string path) => Real.GetValueOrDefault(path);
    }

    private static FakeSysfs CreateDevice(string name, string interfacePath, string number)
    {
        FakeSysfs fs = new();
        fs.Nodes.Add(name);
        string device = $"/sys/class/hidraw/{name}/device";
        fs.Text[$"{device}/uevent"] = "DRIVER=hid-generic\nHID_ID=0003:00003710:00005406\nHID_NAME=Pulsar X2\nHID_UNIQ=ABC123\n";
        fs.Bytes[$"{device}/report_descriptor"] = ReportDescriptorTests.MousePlusVendor;
        fs.Real[device] = $"{interfacePath}/0003:3710:5406.0005";
        fs.Text[$"{interfacePath}/bInterfaceNumber"] = number + "\n";
        return fs;
    }

    [Fact]
    public void Enumerate_reports_one_entry_per_collection_with_interface_data()
    {
        FakeSysfs fs = CreateDevice("hidraw3", Interface1, "01");
        IReadOnlyList<HidDeviceInfo> devices = new LinuxHidrawTransport(fs, _ => throw new InvalidOperationException()).Enumerate();

        Assert.Equal(2, devices.Count);
        HidDeviceInfo vendor = devices[1];
        Assert.Equal("/dev/hidraw3", vendor.Path);
        Assert.Equal((ushort)0x3710, vendor.VendorId);
        Assert.Equal((ushort)0x5406, vendor.ProductId);
        Assert.Equal(1, vendor.InterfaceNumber);
        Assert.Equal((ushort)0xFF02, vendor.UsagePage);
        Assert.Equal(17, vendor.InputLength);
        Assert.Equal(17, vendor.OutputLength);
        Assert.Equal("ABC123", vendor.SerialNumber);
        Assert.Equal("Pulsar X2", vendor.ProductName);
        Assert.Equal(Interface1, vendor.ContainerKey);
        Assert.Equal(devices[0].ContainerKey, vendor.ContainerKey);
    }

    [Fact]
    public void Enumerate_skips_a_broken_node_but_keeps_the_others()
    {
        FakeSysfs fs = CreateDevice("hidraw3", Interface1, "01");
        fs.Nodes.Insert(0, "hidraw2");
        fs.Text["/sys/class/hidraw/hidraw2/device/uevent"] = "HID_ID=garbage\n";

        Assert.Equal(2, new LinuxHidrawTransport(fs, _ => throw new InvalidOperationException()).Enumerate().Count);
    }

    [Fact]
    public void LinuxEnumerate_PermissionDenied_ReportsNoAccess()
    {
        FakeSysfs fs = CreateDevice("hidraw3", Interface1, "01");
        LinuxHidrawTransport transport = new(fs, _ => throw new UnauthorizedAccessException());

        HidDeviceInfo info = Assert.Single(transport.Enumerate(), d => d.UsagePage == 0xFF02);
        Assert.Throws<HidAccessException>(() => transport.Open(info));
    }

    [Fact]
    public void Canonicalize_follows_links_in_every_segment_like_real_sysfs()
    {
        const string Hid = "/sys/devices/pci0000:00/0000:00:14.0/usb1/1-3/1-3:1.1/0003:3710:5406.0005";
        Dictionary<string, string> links = new()
        {
            ["/sys/class/hidraw/hidraw3"] = "../../devices/pci0000:00/0000:00:14.0/usb1/1-3/1-3:1.1/0003:3710:5406.0005/hidraw/hidraw3",
            [Hid + "/hidraw/hidraw3/device"] = "../../../0003:3710:5406.0005",
        };

        string result = SysfsPath.Canonicalize("/sys/class/hidraw/hidraw3/device", p => links.GetValueOrDefault(p));

        Assert.Equal(Hid, result);
    }
}
