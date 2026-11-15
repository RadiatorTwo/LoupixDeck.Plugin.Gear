// Payload layouts and parsing logic ported from:
//  - openlogi-net (MIT, Copyright (c) 2026 LoxSmoke): AdjustableDpiFeature.ParseDpiListPayload,
//    DeviceInformation.Parse, BatteryVoltageFeature (payload layout), UnifiedBatteryFeature, DeviceTypeAndNameFeature.
//  - openlogi-hidpp (0BSD, fork of lus/logy): report_rate.rs (0x8060), extended_report_rate.rs (0x8061),
//    battery_status.rs (0x1000).
// The DPI ladder, the key and the link rules are this plugin's own.
using System.Buffers.Binary;
using System.Text;
using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>The identity read from DeviceInformation (0x0003, function 0).</summary>
/// <param name="UnitId">Four bytes that identify the physical unit; the same over receiver and cable.</param>
/// <param name="ModelIds">The three model ids (big-endian words); the first one is used for the key.</param>
internal sealed record LogitechIdentity(byte[] UnitId, ushort[] ModelIds)
{
    public ushort ModelId => ModelIds[0];
}

/// <summary>Pure decoders for the replies of the HID++ 2.0 features the provider reads. All take the 16 payload bytes.</summary>
internal static class LogitechDecoding
{
    /// <summary>Device types of DeviceTypeAndName (0x0005, function 2) that count as a pointing device.</summary>
    private const byte TypeMouse = 3;
    private const byte TypeTrackpad = 4;
    private const byte TypeTrackball = 5;

    private static readonly int[] ExtendedRates = [125, 250, 500, 1000, 2000, 4000, 8000];

    // DPI values tried for the stages of Next/Previous, in order.
    private static readonly int[] DpiLadder = [400, 800, 1600, 3200, 6400, 12800, 25600];

    // Generic single-cell li-ion discharge curve (mV, percent), descending voltage; interpolated linearly.
    // A rough approximation, unverified against G HUB. It is our own table, not taken from Solaar or any other project.
    private static readonly (int Millivolts, int Percent)[] VoltageCurve =
    [
        (4200, 100), (4100, 90), (4000, 78), (3900, 62), (3800, 45),
        (3700, 25), (3600, 10), (3500, 3), (3400, 0),
    ];

    // DeviceInformation

    public static LogitechIdentity ParseDeviceInformation(ReadOnlySpan<byte> payload)
    {
        return new LogitechIdentity(
            payload.Slice(1, 4).ToArray(),
            [
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(7, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(9, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(11, 2)),
            ]);
    }

    // DeviceTypeAndName

    public static bool IsPointingDevice(byte deviceType) => deviceType is TypeMouse or TypeTrackpad or TypeTrackball;

    /// <summary>Joins the name chunks (function 1) and cuts them to the announced length (function 0).</summary>
    public static string AssembleName(int length, IEnumerable<byte[]> chunks)
    {
        List<byte> bytes = [];
        foreach (byte[] chunk in chunks)
        {
            bytes.AddRange(chunk);
        }

        int count = Math.Min(length, bytes.Count);
        return Encoding.ASCII.GetString([.. bytes.Take(count)]).TrimEnd('\0').Trim();
    }

    // AdjustableDpi (0x2201)

    /// <summary>
    /// Parses the DPI list of function 1 (<paramref name="bytes"/> follow the echoed sensor index): big-endian words,
    /// a <c>0xE000|step</c> marker expands the range between the previous and the next word, 0 ends the list. The
    /// result is sorted and distinct. Returns false for a malformed list.
    /// </summary>
    public static bool TryParseDpiList(ReadOnlySpan<byte> bytes, out int[] values)
    {
        List<int> list = [];
        int offset = 0;
        while (offset + 1 < bytes.Length)
        {
            int value = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (value == 0)
            {
                break;
            }

            if (value >> 13 == 0b111)
            {
                int step = value & 0x1FFF;
                if (step == 0 || offset + 3 >= bytes.Length || list.Count == 0)
                {
                    values = [];
                    return false;
                }

                int start = list[^1];
                int last = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
                if (last < start)
                {
                    values = [];
                    return false;
                }

                for (int next = start + step; next < last; next += step)
                {
                    list.Add(next);
                }

                list.Add(last); // the high endpoint is always supported, even off the grid
                offset += 4;
            }
            else
            {
                list.Add(value);
                offset += 2;
            }
        }

        values = [.. list.Distinct().Order()];
        return values.Length > 0;
    }

    /// <summary>The current DPI of function 2 (byte 0 is the sensor index).</summary>
    public static int ParseDpi(ReadOnlySpan<byte> payload) => BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));

    /// <summary>
    /// The value to set: <paramref name="dpi"/> snapped to the nearest supported value (the lower one on a tie), or
    /// false if it is outside the sensor's range.
    /// </summary>
    public static bool TrySnapDpi(IReadOnlyList<int> supported, int dpi, out int snapped)
    {
        snapped = 0;
        if (supported.Count == 0 || dpi < supported[0] || dpi > supported[^1])
        {
            return false;
        }

        snapped = supported.OrderBy(v => Math.Abs(v - dpi)).ThenBy(v => v).First();
        return true;
    }

    /// <summary>
    /// The stages the mouse reports for Next/Previous: the fixed ladder inside the sensor range, each rung snapped to a
    /// supported value and distinct, plus the current DPI at its place if it is not a rung.
    /// </summary>
    public static GearDpiState BuildDpiState(IReadOnlyList<int> supported, int current)
    {
        SortedSet<int> stages = [];
        foreach (int rung in DpiLadder)
        {
            if (TrySnapDpi(supported, rung, out int snapped))
            {
                stages.Add(snapped);
            }
        }

        stages.Add(current);
        List<int> list = [.. stages];
        return new GearDpiState(list, list.IndexOf(current));
    }

    // ExtendedReportRate (0x8061)

    /// <summary>The supported rates of function 1 (the mask is byte 1 alone), ascending.</summary>
    public static int[] ParseExtendedRates(ReadOnlySpan<byte> payload)
    {
        int mask = payload[1];
        return [.. ExtendedRates.Where((_, bit) => (mask & (1 << bit)) != 0)];
    }

    public static bool TryExtendedHzForCode(byte code, out int hz)
    {
        hz = code < ExtendedRates.Length ? ExtendedRates[code] : 0;
        return hz != 0;
    }

    public static bool TryExtendedCodeForHz(int hz, out byte code)
    {
        int index = Array.IndexOf(ExtendedRates, hz);
        code = (byte)Math.Max(index, 0);
        return index >= 0;
    }

    // ReportRate (0x8060): bit n of the list means an interval of n + 1 ms. Only 1, 2, 4 and 8 ms map to a standard rate.

    /// <summary>The supported rates of function 0, ascending; intervals that are no standard rate are left out.</summary>
    public static int[] ParseLegacyRates(ReadOnlySpan<byte> payload)
    {
        List<int> rates = [];
        for (int bit = 0; bit < 8; bit++)
        {
            if ((payload[0] & (1 << bit)) != 0 && TryLegacyHzForMs(bit + 1, out int hz))
            {
                rates.Add(hz);
            }
        }

        rates.Sort();
        return [.. rates];
    }

    public static bool TryLegacyHzForMs(int ms, out int hz)
    {
        hz = ms is 1 or 2 or 4 or 8 ? 1000 / ms : 0;
        return hz != 0;
    }

    public static bool TryLegacyMsForHz(int hz, out byte ms)
    {
        ms = hz switch { 1000 => 1, 500 => 2, 250 => 4, 125 => 8, _ => (byte)0 };
        return ms != 0;
    }

    // Battery

    /// <summary>Whether UnifiedBattery (0x1004) reports a percentage (function 0, capability bit 1).</summary>
    public static bool UnifiedBatteryReportsPercentage(ReadOnlySpan<byte> capabilities) => (capabilities[1] & 0b10) != 0;

    /// <summary>UnifiedBattery function 1: percentage, level flag and status; the level stands in for a missing percentage.</summary>
    public static GearPowerReading? DecodeUnifiedBattery(ReadOnlySpan<byte> payload, bool reportsPercentage)
    {
        byte status = payload[2];
        if (status > 3)
        {
            return null;
        }

        int percent;
        if (reportsPercentage)
        {
            percent = payload[0];
        }
        else
        {
            // Critical, low, good, full.
            percent = payload[1] switch { 1 => 5, 2 => 20, 4 => 50, 8 => 90, _ => -1 };
        }

        if (percent is < 0 or > 100)
        {
            return null;
        }

        // Status: 0 discharging, 1 charging, 2 charging slowly, 3 full, 4 error (treated as no reading).
        return status switch
        {
            3 => new GearPowerReading(100, false, null),
            1 or 2 => new GearPowerReading(percent, true, null),
            _ => new GearPowerReading(percent, false, null),
        };
    }

    /// <summary>BatteryVoltage (0x1001, function 0): millivolts, then a flag byte (bit 7 charging, bit 6 charge complete).</summary>
    public static GearPowerReading DecodeBatteryVoltage(ReadOnlySpan<byte> payload)
    {
        int millivolts = BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
        bool charging = (payload[2] & 0x80) != 0;
        bool full = (payload[2] & 0x40) != 0;
        if (charging && full)
        {
            return new GearPowerReading(100, false, millivolts);
        }

        return new GearPowerReading(PercentFromMillivolts(millivolts), charging, millivolts);
    }

    public static int PercentFromMillivolts(int millivolts)
    {
        if (millivolts >= VoltageCurve[0].Millivolts)
        {
            return 100;
        }

        if (millivolts <= VoltageCurve[^1].Millivolts)
        {
            return 0;
        }

        for (int i = 1; i < VoltageCurve.Length; i++)
        {
            (int highMv, int highPercent) = VoltageCurve[i - 1];
            (int lowMv, int lowPercent) = VoltageCurve[i];
            if (millivolts <= highMv && millivolts >= lowMv)
            {
                double fraction = (double)(millivolts - lowMv) / (highMv - lowMv);
                return (int)Math.Round(lowPercent + (fraction * (highPercent - lowPercent)));
            }
        }

        return 0;
    }

    /// <summary>
    /// Legacy BatteryStatus (0x1000, function 0): level in percent (only meaningful while discharging), next level,
    /// status (0 discharging, 1 recharging, 2 almost full, 3 full, 4 slow recharge).
    /// </summary>
    public static GearPowerReading? DecodeLegacyBattery(ReadOnlySpan<byte> payload)
    {
        byte level = payload[0];
        if (level > 100)
        {
            return null;
        }

        return payload[2] switch
        {
            0 => new GearPowerReading(level, false, null),
            1 or 4 => new GearPowerReading(level, true, null),
            2 => new GearPowerReading(level > 0 ? level : 90, true, null),
            3 => new GearPowerReading(100, false, null),
            _ => null,
        };
    }

    // Link

    /// <summary>The link of a mouse behind a receiver, from the highest polling rate it allows.</summary>
    public static GearLink LinkFromRates(IReadOnlyList<int> allowedRates)
    {
        int highest = allowedRates.Count == 0 ? 0 : allowedRates.Max();
        return highest switch
        {
            >= 8000 => GearLink.Wireless8K,
            >= 4000 => GearLink.Wireless4K,
            >= 2000 => GearLink.Wireless2K,
            _ => GearLink.Wireless1K,
        };
    }
}
