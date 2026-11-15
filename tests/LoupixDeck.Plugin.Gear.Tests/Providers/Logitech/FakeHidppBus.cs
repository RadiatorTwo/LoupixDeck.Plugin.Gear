using System.Text;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Logitech;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

/// <summary>A HID++ 2.0 error reply the scripted device sends instead of a payload.</summary>
internal sealed class FakeHidppError(byte code) : Exception
{
    public byte Code { get; } = code;
}

/// <summary>A scripted HID++ 2.0 device: a feature table plus a handler per (feature, function).</summary>
internal sealed class FakeHidppDevice
{
    private readonly Dictionary<(byte Index, byte Function), Func<byte[], byte[]>> _handlers = [];
    private byte _nextIndex = 1;

    public Dictionary<ushort, byte> Features { get; } = [];

    /// <summary>Every call that reached a feature (Root calls are counted in <see cref="RootLookups"/>).</summary>
    public List<(ushort Feature, byte Function, byte[] Parameters)> Calls { get; } = [];

    public int RootLookups { get; private set; }

    /// <summary>When set the device does not answer at all (asleep).</summary>
    public bool Silent { get; set; }

    public byte Add(ushort feature)
    {
        byte index = _nextIndex++;
        Features[feature] = index;
        return index;
    }

    public FakeHidppDevice On(ushort feature, byte function, Func<byte[], byte[]> handler)
    {
        if (!Features.TryGetValue(feature, out byte index))
        {
            index = Add(feature);
        }

        _handlers[(index, function)] = handler;
        return this;
    }

    /// <summary>The 16 payload bytes (zero-padded) for a request, or null if the device stays silent.</summary>
    public byte[]? Respond(byte featureIndex, byte function, byte[] parameters, out byte? errorCode)
    {
        errorCode = null;
        if (Silent)
        {
            return null;
        }

        if (featureIndex == 0)
        {
            if (function == 0)
            {
                RootLookups++;
                ushort id = (ushort)((parameters[0] << 8) | parameters[1]);
                return Pad(Features.TryGetValue(id, out byte index) ? index : (byte)0, 0, 1);
            }

            return Pad(4, 2, parameters[2]); // ping echo
        }

        ushort feature = Features.First(f => f.Value == featureIndex).Key;
        Calls.Add((feature, function, parameters));
        try
        {
            return Pad(_handlers[(featureIndex, function)](parameters));
        }
        catch (FakeHidppError ex)
        {
            errorCode = ex.Code;
            return null;
        }
    }

    public static byte[] Pad(params byte[] bytes) => [.. bytes, .. new byte[16 - bytes.Length]];

    // Feature scripts

    public static FakeHidppDevice Mouse(string name = "G502 TEST", byte[]? unitId = null, ushort modelId = 0x4082, byte type = 3)
    {
        byte[] unit = unitId ?? [0xA1, 0xB2, 0xC3, 0xD4];
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        FakeHidppDevice device = new();
        device.On(0x0003, 0, _ => Pad([3, .. unit, 0, 0, (byte)(modelId >> 8), (byte)modelId, 0, 0, 0, 0]));
        device.On(0x0005, 0, _ => Pad((byte)nameBytes.Length));
        device.On(0x0005, 1, p => Pad([.. nameBytes.Skip(p[0]).Take(16)]));
        device.On(0x0005, 2, _ => Pad(type));
        return device;
    }

    public static FakeHidppDevice Keyboard() => Mouse("K860", [1, 2, 3, 4], 0x4101, type: 0);

    public FakeHidppDevice WithUnifiedBattery(byte percent, byte level, byte status, bool reportsPercentage = true)
    {
        On(0x1004, 0, _ => Pad(0x0F, (byte)(reportsPercentage ? 0b11 : 0b01)));
        return On(0x1004, 1, _ => Pad(percent, level, status));
    }

    public FakeHidppDevice WithVoltageBattery(int millivolts, byte flags)
    {
        return On(0x1001, 0, _ => Pad((byte)(millivolts >> 8), (byte)millivolts, flags));
    }

    public FakeHidppDevice WithLegacyBattery(byte level, byte status)
    {
        return On(0x1000, 0, _ => Pad(level, 0, status));
    }

    /// <summary>0x8061 with the given mask; <paramref name="code"/> is the current rate code per connection type.</summary>
    public FakeHidppDevice WithExtendedRate(byte mask, byte code, Action<byte>? onSet = null, byte? ignoredSetKeepsCode = null)
    {
        byte current = code;
        On(0x8061, 1, _ => Pad(0, mask));
        On(0x8061, 2, _ => Pad(current));
        return On(0x8061, 3, p =>
        {
            onSet?.Invoke(p[0]);
            current = ignoredSetKeepsCode ?? p[0];
            return Pad(p[0]);
        });
    }

    public FakeHidppDevice WithLegacyRate(byte mask, byte ms)
    {
        byte current = ms;
        On(0x8060, 0, _ => Pad(mask));
        On(0x8060, 1, _ => Pad(current));
        return On(0x8060, 2, p =>
        {
            current = p[0];
            return Pad(p[0]);
        });
    }

    /// <summary>0x2201 with the range 100..25600 in steps of 50.</summary>
    public FakeHidppDevice WithDpi(int current, bool rejectSet = false)
    {
        int dpi = current;
        On(0x2201, 1, _ => Pad(0, 0x00, 0x64, 0xE0, 0x32, 0x64, 0x00));
        On(0x2201, 2, _ => Pad(0, (byte)(dpi >> 8), (byte)dpi));
        return On(0x2201, 3, p =>
        {
            if (rejectSet)
            {
                throw new FakeHidppError(0x03);
            }

            dpi = (p[1] << 8) | p[2];
            return Pad(0, p[1], p[2]);
        });
    }
}

/// <summary>
/// A fake receiver (slots 1..6) or a directly attached device (index 0xFF): a short and a long HID collection that
/// answer like HID++ hardware. Replies to short requests arrive as long reports on the long collection.
/// </summary>
internal sealed class FakeHidppBus
{
    public static readonly HidppLinkOptions Fast = new(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));

    private readonly Dictionary<byte, Func<byte[], byte[]?>> _slots = [];

    public FakeHidppBus(bool receiver)
    {
        ushort pid = receiver ? (ushort)0xC539 : (ushort)0xC332;
        ShortInfo = new HidDeviceInfo("short", 0x046D, pid, 2, 0xFF00, 0x0001, 7, 7, null, null, "c1");
        LongInfo = new HidDeviceInfo("long", 0x046D, pid, 2, 0xFF00, 0x0002, 20, 20, null, null, "c1");
        Transport = new FakeHidppTransport([ShortInfo, LongInfo]);
        Short = new FakeHidppConnection();
        Long = new FakeHidppConnection();
        Transport.Add("short", Short);
        Transport.Add("long", Long);
        Short.OnWrite = Handle;
        Long.OnWrite = Handle;
        Link = new HidppLink(ShortInfo, Transport, Fast);
    }

    public HidDeviceInfo ShortInfo { get; }

    public HidDeviceInfo LongInfo { get; }

    public FakeHidppTransport Transport { get; }

    public FakeHidppConnection Short { get; }

    public FakeHidppConnection Long { get; }

    public HidppLink Link { get; }

    /// <summary>Puts a scripted device into a slot (or index 0xFF for a direct device).</summary>
    public FakeHidppBus Plug(byte deviceIndex, FakeHidppDevice device)
    {
        _slots[deviceIndex] = request =>
        {
            byte featureIndex = request[2];
            byte function = (byte)(request[3] >> 4);
            byte[] parameters = request.AsSpan(4).ToArray();
            byte[]? payload = device.Respond(featureIndex, function, parameters, out byte? error);
            if (error is { } code)
            {
                return Frame(0x11, request[1], 0xFF, featureIndex, request[3], code);
            }

            return payload is null ? null : Frame(0x11, request[1], featureIndex, request[3], payload);
        };
        return this;
    }

    /// <summary>The receiver answers 1.0 error 0x08 (no device paired in this slot).</summary>
    public FakeHidppBus Empty(byte deviceIndex) => ReceiverError(deviceIndex, 0x08);

    /// <summary>The receiver answers 1.0 error 0x09 (paired but asleep or out of range).</summary>
    public FakeHidppBus Offline(byte deviceIndex) => ReceiverError(deviceIndex, 0x09);

    /// <summary>The device answers 1.0 error 0x01 (it does not know HID++ 2.0).</summary>
    public FakeHidppBus V10Only(byte deviceIndex) => ReceiverError(deviceIndex, 0x01);

    public LogitechDevice Device(byte deviceIndex, bool? viaReceiver = null)
    {
        return new LogitechDevice(ShortInfo, Link, deviceIndex, viaReceiver ?? deviceIndex != HidppProtocol.DirectDeviceIndex);
    }

    private FakeHidppBus ReceiverError(byte deviceIndex, byte code)
    {
        _slots[deviceIndex] = request => Frame(0x10, request[1], 0x8F, request[2], request[3], code);
        return this;
    }

    private void Handle(byte[] request)
    {
        if (!_slots.TryGetValue(request[1], out Func<byte[], byte[]?>? slot) || slot(request) is not { } reply)
        {
            return;
        }

        (reply[0] == 0x10 ? Short : Long).Enqueue(reply);
    }

    private static byte[] Frame(byte id, params byte[] bytes)
    {
        byte[] frame = new byte[id == 0x10 ? 7 : 20];
        frame[0] = id;
        bytes.CopyTo(frame, 1);
        return frame;
    }

    private static byte[] Frame(byte id, byte deviceIndex, byte featureIndex, byte address, byte[] payload)
    {
        return Frame(id, [deviceIndex, featureIndex, address, .. payload]);
    }
}
