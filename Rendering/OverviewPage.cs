using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Rendering;

/// <summary>All mice on one tile: a name and the battery percentage per row, as many rows as fit.</summary>
internal static class OverviewPage
{
    private const int FirstRowY = 21;
    private const int RowPitch = 10;

    /// <summary>Rows that fit between the header and the bottom of the content grid.</summary>
    public static readonly int MaxRows = (PixelSurface.Bottom + 1 - FirstRowY) / RowPitch;

    /// <param name="pageIndex">"n/N" for the header, or null on a tile that shows a single page.</param>
    /// <param name="tr">Translates the pixel-font texts (English keys).</param>
    public static void Draw(PixelSurface surface, IReadOnlyList<GearDeviceState> devices, string? pageIndex, bool blinkOn, Func<string, string> tr)
    {
        PixelPalette p = surface.Palette;
        TileDrawing.Header(surface, tr("DEVICES"), pageIndex, TileState.Ok, blinkOn);

        for (int i = 0; i < Math.Min(devices.Count, MaxRows); i++)
        {
            GearDeviceState device = devices[i];
            int y = FirstRowY + (RowPitch * i);
            bool asleep = device.Availability == GearAvailability.Asleep;
            // A mouse without battery shows its DPI instead of a percentage.
            bool hasBattery = device.Capabilities.HasFlag(GearCapabilities.Battery);
            string? shown = hasBattery ? (device.Percent is { } value ? $"{value}%" : null) : DevicePage.ActiveDpi(device);
            string percent = shown ?? "--";
            uint color = shown is null || asleep ? p.Dim : hasBattery ? TileDrawing.StateColor(p, DevicePage.StateOf(device)) : p.Text;

            surface.Text(TileDrawing.Fit(device.DisplayName, TileDrawing.W - PixelFont.Measure(percent) - 3), TileDrawing.L, y, asleep ? p.Dim : p.Text);
            surface.TextRight(percent, TileDrawing.R, y, color);
        }
    }
}
