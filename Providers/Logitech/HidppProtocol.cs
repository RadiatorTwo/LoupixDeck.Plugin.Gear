// Frame layout and error replies follow the HID++ 2.0 / 1.0 framing used by openlogi-net (MIT)
// `V20`/`V10` and the openlogi-hidpp crate (0BSD); the code is a reimplementation for this plugin.
namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>HID++ frame building and reply matching. Pure functions, no I/O.</summary>
internal static class HidppProtocol
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    public const int ShortLength = 7;
    public const int LongLength = 20;
    public const int MaxShortParameters = ShortLength - 4;
    public const int MaxLongParameters = LongLength - 4;

    /// <summary>Length of a reply payload (bytes 4..19 of a long report).</summary>
    public const int PayloadLength = LongLength - 4;

    /// <summary>Device index of a device attached directly (cable).</summary>
    public const byte DirectDeviceIndex = 0xFF;

    /// <summary>Software id of our requests; 0 is reserved for device notifications.</summary>
    public const byte SoftwareId = 0x0A;

    private const byte V20ErrorFeatureIndex = 0xFF;
    private const byte V10ErrorSubId = 0x8F;

    /// <summary>The request address byte: function in the high nibble, software id in the low nibble.</summary>
    public static byte Address(byte function, byte softwareId) => (byte)(((function & 0x0F) << 4) | (softwareId & 0x0F));

    /// <summary>Builds a short (7 bytes) or long (20 bytes) HID++ 2.0 request.</summary>
    public static byte[] BuildRequest(
        bool longReport,
        byte deviceIndex,
        byte featureIndex,
        byte function,
        byte softwareId,
        ReadOnlySpan<byte> parameters)
    {
        int max = longReport ? MaxLongParameters : MaxShortParameters;
        if (parameters.Length > max)
        {
            throw new ArgumentException($"At most {max} parameter bytes fit into this report.", nameof(parameters));
        }

        byte[] frame = new byte[longReport ? LongLength : ShortLength];
        frame[0] = longReport ? LongReportId : ShortReportId;
        frame[1] = deviceIndex;
        frame[2] = featureIndex;
        frame[3] = Address(function, softwareId);
        parameters.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>
    /// Checks whether <paramref name="report"/> answers the request (device index, feature index, address).
    /// Returns false for anything else (notifications, replies to other requests, malformed reports). On a match
    /// either <paramref name="payload"/> (16 bytes, zero-padded) or <paramref name="error"/> is set.
    /// </summary>
    public static bool TryMatch(
        ReadOnlySpan<byte> report,
        byte deviceIndex,
        byte featureIndex,
        byte address,
        out byte[] payload,
        out HidppException? error)
    {
        payload = [];
        error = null;
        if (!IsComplete(report) || report[1] != deviceIndex)
        {
            return false;
        }

        if (report[2] == featureIndex && report[3] == address)
        {
            payload = new byte[PayloadLength];
            report[4..Math.Min(report.Length, LongLength)].CopyTo(payload);
            return true;
        }

        // 2.0 error: [id, dev, 0xFF, featureIndex, address, code]; 1.0 error from the receiver: [0x10, dev, 0x8F, subId, address, code].
        if (report.Length > 5
            && report[3] == featureIndex
            && report[4] == address
            && (report[2] == V20ErrorFeatureIndex || (report[2] == V10ErrorSubId && report[0] == ShortReportId)))
        {
            error = new HidppException(report[5], report[2] == V10ErrorSubId);
            return true;
        }

        return false;
    }

    private static bool IsComplete(ReadOnlySpan<byte> report)
    {
        return report.Length > 0
            && ((report[0] == ShortReportId && report.Length >= ShortLength)
                || (report[0] == LongReportId && report.Length >= LongLength));
    }
}
