namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

/// <summary>
/// Frame builders for the Pulsar "Nordic" 17-byte HID protocol. Pure byte handling, no I/O.
/// </summary>
/// <remarks>
/// Frame layout: <c>08 &lt;cmd&gt; &lt;14 data bytes&gt; &lt;chk&gt;</c> with
/// <c>chk = (0x55 - sum(bytes 0..15)) &amp; 0xFF</c>. Re-implemented from the MIT-licensed
/// bluesceada/pulsar-mouse-linux (src/pulsar_mouse/drivers/nordic.py) and ryanlewis/mousebatt
/// (src/protocol.rs); see THIRD-PARTY-NOTICES.md. Both sources agree on the memory command layout:
/// byte 2 = 0, byte 3/4 = address (big-endian), byte 5 = length, bytes 6.. = data
/// (nordic.py writes at most 10 data bytes per MemSet). Info request: byte 5 = 8, bytes 6..9 = nonce.
/// </remarks>
internal static class PulsarProtocol
{
    public const byte ReportId = 0x08;
    public const int ReportLength = 17;

    public const byte Info = 0x01;
    public const byte Status = 0x03;
    public const byte Power = 0x04;
    public const byte MemSet = 0x07;
    public const byte MemGet = 0x08;

    private const int PayloadOffset = 2;
    private const int MaxPayloadLength = ReportLength - 1 - PayloadOffset;
    private const byte ChecksumTarget = 0x55;

    /// <summary>Builds a report; the payload is copied starting at byte 2.</summary>
    public static byte[] Frame(byte cmd, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException($"Payload exceeds {MaxPayloadLength} bytes.", nameof(payload));
        }

        byte[] frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = cmd;
        payload.CopyTo(frame.AsSpan(PayloadOffset));
        frame[ReportLength - 1] = Checksum(frame.AsSpan(0, ReportLength - 1));
        return frame;
    }

    /// <summary>Checksum byte for the first 16 bytes of a report.</summary>
    public static byte Checksum(ReadOnlySpan<byte> first16)
    {
        int sum = 0;
        foreach (byte b in first16)
        {
            sum += b;
        }

        return (byte)((ChecksumTarget - sum) & 0xFF);
    }

    /// <summary>True if the report is a well-formed reply to <paramref name="cmd"/>.</summary>
    public static bool IsValidReply(ReadOnlySpan<byte> report, byte cmd)
    {
        return report.Length == ReportLength
            && report[0] == ReportId
            && report[1] == cmd
            && report[ReportLength - 1] == Checksum(report[..(ReportLength - 1)]);
    }

    public static byte[] InfoRequest(uint nonce)
    {
        // Payload starts at byte 2: bytes 2..4 = 0, byte 5 = 8, bytes 6..9 = nonce.
        Span<byte> payload = stackalloc byte[8];
        payload[3] = 8;
        payload[4] = (byte)(nonce >> 24);
        payload[5] = (byte)(nonce >> 16);
        payload[6] = (byte)(nonce >> 8);
        payload[7] = (byte)nonce;
        return Frame(Info, payload);
    }

    public static byte[] MemGetRequest(ushort address, byte length)
    {
        return Frame(MemGet, [0, (byte)(address >> 8), (byte)address, length]);
    }

    /// <summary>
    /// Builds (never sends) the write of a polling-rate code to address 0x0000, stored as the
    /// pair <c>(code, 0x55 - code)</c>.
    /// </summary>
    public static byte[] PollingRateWrite(byte code)
    {
        return Frame(MemSet, [0, 0x00, 0x00, 2, code, (byte)(ChecksumTarget - code)]);
    }
}
