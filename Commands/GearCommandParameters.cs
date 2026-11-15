using System.Globalization;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;

namespace LoupixDeck.Plugin.Gear.Commands;

internal enum GearRequestKind
{
    Invalid,
    Next,
    Previous,
    Value,
}

/// <summary>A parsed Rate / Dpi parameter: a step (next, previous) or an absolute value.</summary>
internal readonly record struct GearRequest(GearRequestKind Kind, int Value = 0)
{
    public static GearRequest Invalid => new(GearRequestKind.Invalid);
}

/// <summary>Parameter grammar of Gear.PollingRate (<c>next</c> or a rate in Hz) and Gear.Dpi (<c>next</c>, <c>previous</c> or a DPI value).</summary>
internal static class GearCommandParameters
{
    public const string Next = "next";
    public const string Previous = "previous";

    /// <summary>The rates the command accepts; whether a device accepts one on its current link is up to the device.</summary>
    public static readonly IReadOnlyList<int> KnownRates = [125, 250, 500, 1000, 2000, 4000, 8000];

    public static GearRequest ParseRate(string? text)
    {
        string token = (text ?? string.Empty).Trim();
        if (token.Equals(Next, StringComparison.OrdinalIgnoreCase))
        {
            return new GearRequest(GearRequestKind.Next);
        }

        return TryNumber(token, out int hz) && KnownRates.Contains(hz) ? new GearRequest(GearRequestKind.Value, hz) : GearRequest.Invalid;
    }

    public static GearRequest ParseDpi(string? text)
    {
        string token = (text ?? string.Empty).Trim();
        if (token.Equals(Next, StringComparison.OrdinalIgnoreCase))
        {
            return new GearRequest(GearRequestKind.Next);
        }

        if (token.Equals(Previous, StringComparison.OrdinalIgnoreCase))
        {
            return new GearRequest(GearRequestKind.Previous);
        }

        return TryNumber(token, out int dpi) && dpi is >= PulsarDpi.MinDpi and <= PulsarDpi.MaxDpi ? new GearRequest(GearRequestKind.Value, dpi) : GearRequest.Invalid;
    }

    private static bool TryNumber(string token, out int value) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
