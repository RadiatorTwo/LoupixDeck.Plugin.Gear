namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

/// <summary>
/// Register layout and value encoding of the DPI stages of the X2 CrazyLight (model id 0x5717).
/// Pure byte handling, no I/O.
/// </summary>
/// <remarks>
/// Re-implemented from the MIT-licensed bluesceada/pulsar-mouse-linux (see THIRD-PARTY-NOTICES.md):
/// docs/protocol-x2-crazylight.md ("Register map", "DPI stage encoding"), src/pulsar_mouse/drivers/nordic.py
/// (ADDR_DPI_STAGE_COUNT = 0x02, ADDR_ACTIVE_DPI_STAGE = 0x04, ADDR_DPI_BASE = 0x0C, DPI_STAGE_SIZE = 4,
/// _dpi_to_raw, _raw_to_dpi, get_dpi_stages, set_dpi_stages) and src/pulsar_mouse/drivers/x2_crazylight.py
/// (dpi step 10, max 6 stages, _DPI_MODES). The source reports reads and writes of these registers as
/// confirmed on hardware in both directions.
/// <list type="bullet">
/// <item>0x0002: stage count, stored as the pair (n, 0x55 - n).</item>
/// <item>0x0004: active stage, 0-based, stored as the pair (n, 0x55 - n).</item>
/// <item>0x000C + 4 * i: stage record [x_lo, y_lo, high, checksum] with checksum = 0x55 - (x_lo + y_lo + high).
/// The high byte carries one nibble twice; rotated left by 2 it splits into [mode:2][page:2] and
/// dpi = (x_lo + base[mode] + 256 * page) * step[mode].</item>
/// </list>
/// A write takes effect with the plain MemSet; the sources use no commit command.
/// </remarks>
internal static class PulsarDpi
{
    public const ushort StageCountAddress = 0x0002;
    public const ushort ActiveStageAddress = 0x0004;
    public const ushort StageBaseAddress = 0x000C;
    public const int StageRecordSize = 4;
    public const int MaxStages = 6;
    public const int MinDpi = 10;
    public const int MaxDpi = 32000;

    private const byte ChecksumTarget = 0x55;
    private const int BaseStep = 10;

    // (mode, step multiple of the base step, base, highest DPI encoded in this mode); null = everything above.
    private static readonly (int Mode, int Multiple, int Base, int? Limit)[] Modes =
    [
        (0, 1, 1, 10240),
        (2, 5, 201, 25600),
        (3, 10, 201, null),
    ];

    /// <summary>Address of the record of the 0-based stage.</summary>
    public static ushort StageAddress(int index) => (ushort)(StageBaseAddress + (index * StageRecordSize));

    /// <summary>Builds (never sends) the write of the active stage.</summary>
    public static byte[] ActiveStageWrite(int index)
    {
        return PulsarProtocol.Frame(PulsarProtocol.MemSet, [0, (byte)(ActiveStageAddress >> 8), (byte)ActiveStageAddress, 2, (byte)index, (byte)(ChecksumTarget - index)]);
    }

    /// <summary>Builds (never sends) the write of one stage record.</summary>
    public static byte[] StageWrite(int index, int dpi)
    {
        ushort address = StageAddress(index);
        byte[] record = Encode(dpi);
        return PulsarProtocol.Frame(PulsarProtocol.MemSet, [0, (byte)(address >> 8), (byte)address, StageRecordSize, record[0], record[1], record[2], record[3]]);
    }

    /// <summary>The 4-byte stage record of a DPI value; values between two steps snap to the nearest one.</summary>
    public static byte[] Encode(int dpi)
    {
        if (dpi is < MinDpi or > MaxDpi)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, $"DPI must be {MinDpi}..{MaxDpi}.");
        }

        (int mode, int multiple, int baseValue) = Pick(dpi);
        int unit = BaseStep * multiple;
        int snapped = (int)Math.Round((double)dpi / unit, MidpointRounding.ToEven) * unit;
        if (snapped != dpi)
        {
            // Snapping can land on the limit of a finer mode (25610 -> 25600), which then owns the value.
            (mode, multiple, baseValue) = Pick(snapped);
            unit = BaseStep * multiple;
        }

        int index = (snapped / unit) - baseValue;
        if (index is < 0 or > 1023)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI cannot be encoded.");
        }

        byte low = (byte)(index % 256);
        int nibble = Rotate2((mode << 2) | (index / 256));
        byte high = (byte)((nibble << 4) | nibble);
        return [low, low, high, (byte)(ChecksumTarget - (low + low + high))];
    }

    /// <summary>Decodes a 4-byte stage record; false if the record is inconsistent or uses an unknown mode.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> record, out int dpi)
    {
        dpi = 0;
        if (record.Length < StageRecordSize
            || record[0] != record[1]
            || record[3] != (byte)(ChecksumTarget - (record[0] + record[1] + record[2])))
        {
            return false;
        }

        int high = Rotate2(record[2] & 0x0F);
        int mode = high >> 2;
        int page = high & 0x03;
        foreach ((int m, int multiple, int baseValue, _) in Modes)
        {
            if (m == mode)
            {
                dpi = (record[0] + baseValue + (256 * page)) * BaseStep * multiple;
                return true;
            }
        }

        return false;
    }

    /// <summary>The stage after <paramref name="active"/>, wrapping from the last to the first.</summary>
    public static int NextStage(int active, int count) => (active + 1) % count;

    /// <summary>The stage before <paramref name="active"/>, wrapping from the first to the last.</summary>
    public static int PreviousStage(int active, int count) => (active + count - 1) % count;

    private static (int Mode, int Multiple, int Base) Pick(int dpi)
    {
        foreach ((int mode, int multiple, int baseValue, int? limit) in Modes)
        {
            if (limit is null || dpi <= limit)
            {
                return (mode, multiple, baseValue);
            }
        }

        throw new InvalidOperationException("Unreachable: the last mode has no limit.");
    }

    private static int Rotate2(int nibble) => ((nibble << 2) | (nibble >> 2)) & 0x0F;
}
