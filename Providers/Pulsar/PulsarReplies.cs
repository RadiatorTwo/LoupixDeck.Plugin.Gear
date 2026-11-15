namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

internal readonly record struct PulsarPower(byte RawPercent, bool Charging, ushort Millivolts);

internal readonly record struct PulsarStatus(bool MouseAwake);

internal readonly record struct PulsarInfo(ushort ModelId, byte Link);

/// <summary>
/// Parsers for validated 17-byte replies. Offsets are absolute indices into the report
/// (byte 0 = report id, byte 1 = command echo); callers check <see cref="PulsarProtocol.IsValidReply"/> first.
/// </summary>
internal static class PulsarReplies
{
    public static PulsarPower ParsePower(ReadOnlySpan<byte> report)
    {
        return new PulsarPower(report[6], report[7] == 1, (ushort)((report[8] << 8) | report[9]));
    }

    public static PulsarStatus ParseStatus(ReadOnlySpan<byte> report)
    {
        return new PulsarStatus(report[6] == 1);
    }

    public static PulsarInfo ParseInfo(ReadOnlySpan<byte> report)
    {
        return new PulsarInfo((ushort)((report[10] << 8) | report[11]), report[12]);
    }
}
