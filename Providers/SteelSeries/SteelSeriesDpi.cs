namespace LoupixDeck.Plugin.Gear.Providers.SteelSeries;

/// <summary>
/// Maps a DPI value to the code a SteelSeries sensor expects. Ported from the <c>range</c>, <c>range_choice</c> and
/// <c>choice</c> handlers of flozz/rivalcfg (WTFPL); see THIRD-PARTY-NOTICES.md.
/// </summary>
internal abstract class SteelSeriesDpiCodec(int byteLength)
{
    /// <summary>Bytes per code in the packet (little-endian).</summary>
    public int ByteLength { get; } = byteLength;

    /// <summary>The code for <paramref name="dpi"/> and the DPI it stands for; out-of-range values snap to the nearest end.</summary>
    public abstract (int Code, int Dpi) Encode(int dpi);

    /// <summary>
    /// A linear sensor: <paramref name="first"/>..<paramref name="last"/> DPI in <paramref name="step"/> steps, coded from
    /// <paramref name="firstCode"/> upwards. A value between two steps rounds half up, as in rivalcfg.
    /// </summary>
    public static SteelSeriesDpiCodec Range(int first, int last, int step, int firstCode, int byteLength = 1) =>
        new RangeCodec(first, last, step, firstCode, byteLength);

    /// <summary>A sensor with one code per step from <paramref name="first"/> DPI upwards.</summary>
    public static SteelSeriesDpiCodec Table(int first, int step, IReadOnlyList<byte> codes) =>
        new ChoiceCodec(codes.Select((code, i) => (first + (i * step), (int)code)).ToArray(), 1);

    /// <summary>A sensor with a fixed set of DPI values.</summary>
    public static SteelSeriesDpiCodec Choices(params (int Dpi, int Code)[] choices) => new ChoiceCodec(choices, 1);

    /// <summary>The same table with codes of <paramref name="byteLength"/> bytes.</summary>
    public SteelSeriesDpiCodec WithByteLength(int byteLength) => this is ChoiceCodec c
        ? new ChoiceCodec(c.Values, byteLength)
        : throw new NotSupportedException("Only table codecs can be widened.");

    /// <summary>Appends the code of <paramref name="dpi"/> to <paramref name="packet"/>; returns the DPI it stands for.</summary>
    public int AppendTo(List<byte> packet, int dpi)
    {
        (int code, int actual) = Encode(dpi);
        for (int i = 0; i < ByteLength; i++)
        {
            packet.Add((byte)(code >> (8 * i)));
        }

        return actual;
    }

    private sealed class RangeCodec(int first, int last, int step, int firstCode, int byteLength) : SteelSeriesDpiCodec(byteLength)
    {
        public override (int Code, int Dpi) Encode(int dpi)
        {
            int snapped;
            if (dpi <= first)
            {
                snapped = first;
            }
            else if (dpi >= last)
            {
                snapped = last;
            }
            else
            {
                int delta = (dpi - first) % step;
                snapped = delta * 2 < step ? dpi - delta : dpi - delta + step;
            }

            return (firstCode + ((snapped - first) / step), snapped);
        }
    }

    private sealed class ChoiceCodec((int Dpi, int Code)[] values, int byteLength) : SteelSeriesDpiCodec(byteLength)
    {
        private readonly (int Dpi, int Code)[] _sorted = values.OrderBy(v => v.Dpi).ToArray();

        public (int Dpi, int Code)[] Values => _sorted;

        // Nearest value; a tie goes to the lower one, as in rivalcfg.
        public override (int Code, int Dpi) Encode(int dpi)
        {
            (int Dpi, int Code) best = _sorted[0];
            foreach ((int Dpi, int Code) value in _sorted)
            {
                if (Math.Abs(value.Dpi - dpi) < Math.Abs(best.Dpi - dpi))
                {
                    best = value;
                }
            }

            return (best.Code, best.Dpi);
        }
    }
}

/// <summary>Builds the packets that set the DPI of a SteelSeries mouse. Every packet starts after the report id.</summary>
internal abstract class SteelSeriesDpiWriter
{
    /// <summary>The packets that make the mouse run at <paramref name="dpi"/>, and the DPI it will actually use.</summary>
    public abstract (IReadOnlyList<byte[]> Packets, int Dpi) Build(int dpi);
}

/// <summary>
/// Older mice with two DPI slots that the DPI button toggles between (rivalcfg <c>sensitivity1</c> /
/// <c>sensitivity2</c>). Which slot is active cannot be read, so both get the same value.
/// </summary>
internal sealed class SteelSeriesSlotDpiWriter(byte[] slot1Command, byte[] slot2Command, SteelSeriesDpiCodec codec, byte[]? suffix = null)
    : SteelSeriesDpiWriter
{
    public override (IReadOnlyList<byte[]> Packets, int Dpi) Build(int dpi)
    {
        int actual = 0;
        List<byte[]> packets = [];
        foreach (byte[] command in new[] { slot1Command, slot2Command })
        {
            List<byte> packet = [.. command];
            actual = codec.AppendTo(packet, dpi);
            packet.AddRange(suffix ?? []);
            packets.Add([.. packet]);
        }

        return (packets, actual);
    }
}

internal enum SteelSeriesPresetLayout
{
    /// <summary>One code per preset (rivalcfg <c>multidpi_range</c> / <c>multidpi_range_choice</c>).</summary>
    Single,

    /// <summary>X and Y code per preset, interleaved (rivalcfg <c>xy_mapping = "xyxy"</c>).</summary>
    XyInterleaved,

    /// <summary>All X codes of the five presets, then all Y codes (rivalcfg <c>xy_mapping = "xxyy"</c>).</summary>
    XThenY,
}

/// <summary>
/// Newer mice that take the whole DPI preset list in one packet:
/// <c>command, preset count, selected preset, codes</c>. Gear writes one preset, so the mouse runs at that DPI until
/// the next write or a replug.
/// </summary>
/// <remarks>
/// Some models send the count as a bit mask (rivalcfg <c>count_mode = "flag"</c>); for a single preset the mask and
/// the number are both 1.
/// </remarks>
/// <param name="firstPreset">Number of the first preset on the wire (0 or 1).</param>
internal sealed class SteelSeriesPresetDpiWriter(
    byte[] command,
    SteelSeriesDpiCodec codec,
    int firstPreset,
    SteelSeriesPresetLayout layout = SteelSeriesPresetLayout.Single) : SteelSeriesDpiWriter
{
    private const int MaxPresets = 5;

    public override (IReadOnlyList<byte[]> Packets, int Dpi) Build(int dpi)
    {
        List<byte> packet = [.. command, 0x01, (byte)firstPreset];
        int actual = codec.AppendTo(packet, dpi);
        switch (layout)
        {
            case SteelSeriesPresetLayout.XyInterleaved:
                codec.AppendTo(packet, dpi);
                break;
            case SteelSeriesPresetLayout.XThenY:
                // X of preset 1, X of the unused presets 2..5, then the same for Y.
                packet.AddRange(new byte[(MaxPresets - 1) * codec.ByteLength]);
                codec.AppendTo(packet, dpi);
                packet.AddRange(new byte[(MaxPresets - 1) * codec.ByteLength]);
                break;
        }

        return ([packet.ToArray()], actual);
    }
}
