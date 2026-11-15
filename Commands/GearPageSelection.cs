using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// Resolves the "Pages" parameter of Gear.Pages: empty or <c>all</c> means every device page plus the overview;
/// otherwise '|'-separated tokens, each a device key or <c>overview</c>.
/// </summary>
internal static class GearPageSelection
{
    public const string All = "all";
    public const string Overview = "overview";

    /// <summary>The pages of the given selections (one per chained command) in order; tokens without a device are skipped.</summary>
    public static IReadOnlyList<GearPage> Resolve(IEnumerable<string?> selections, IReadOnlyList<GearDeviceState> devices)
    {
        List<GearPage> pages = [];
        foreach (string? selection in selections)
        {
            string[] tokens = (selection ?? string.Empty)
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Any(t => t.Equals(All, StringComparison.OrdinalIgnoreCase)))
            {
                pages.AddRange(devices.Select(d => new GearPage(d)));
                pages.Add(GearPage.OverviewPage);
                continue;
            }

            foreach (string token in tokens)
            {
                if (token.Equals(Overview, StringComparison.OrdinalIgnoreCase))
                {
                    pages.Add(GearPage.OverviewPage);
                }
                else if (devices.FirstOrDefault(d => d.Key != null && string.Equals(d.Key, token, StringComparison.OrdinalIgnoreCase)) is { } device)
                {
                    pages.Add(new GearPage(device));
                }
            }
        }

        // Without a device there is nothing to show; the overview alone would be an empty header.
        return devices.Count == 0 ? [] : pages;
    }
}

/// <summary>One page of the tile: a device, or the overview when <see cref="Device"/> is null.</summary>
internal readonly record struct GearPage(GearDeviceState? Device)
{
    public static GearPage OverviewPage => new(null);
}
