namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

/// <summary>
/// Maps the battery voltage to the percentage the Pulsar app shows.
/// </summary>
/// <remarks>
/// The voltage table is the <c>BatteryParam</c> key from the Pulsar app's
/// <c>usersetting\Config.ini</c> (<c>C:\Program Files\Pulsar\cMouse\Sys64</c>): 21 points for
/// 0, 5, ..., 100 %. Values between two points are interpolated linearly, rounded down and clamped to 0..100.
/// </remarks>
internal static class PulsarBattery
{
    private const int StepPercent = 5;

    private static readonly int[] MillivoltsByStep =
    [
        3050, 3170, 3230, 3300, 3600, 3660, 3720, 3760, 3800, 3840, 3880,
        3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110,
    ];

    public static int PercentFromMillivolts(int mv)
    {
        if (mv <= MillivoltsByStep[0])
        {
            return 0;
        }

        int last = MillivoltsByStep.Length - 1;
        if (mv >= MillivoltsByStep[last])
        {
            return 100;
        }

        int upper = 1;
        while (mv > MillivoltsByStep[upper])
        {
            upper++;
        }

        int lower = upper - 1;
        int lowerMv = MillivoltsByStep[lower];
        int upperMv = MillivoltsByStep[upper];
        int lowerPercent = lower * StepPercent;
        int span = upperMv - lowerMv;
        int offset = mv - lowerMv;

        // Integer arithmetic: floor for non-negative values is plain truncation.
        int percent = lowerPercent + (StepPercent * offset) / span;
        return Math.Clamp(percent, 0, 100);
    }
}
