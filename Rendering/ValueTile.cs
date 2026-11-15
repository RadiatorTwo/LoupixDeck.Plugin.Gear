using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Rendering;

/// <summary>
/// A tile showing one setting of a mouse (polling rate, DPI): header with the setting's name, the value as hero with an
/// optional unit, an optional detail row and the mouse's name. Anatomy follows <see cref="DevicePage"/>.
/// </summary>
internal static class ValueTile
{
    private const int HeroY = 21;
    private const int DetailY = 52;
    private const int NameY = 62;

    /// <param name="header">Translated header text (POLLING, DPI).</param>
    /// <param name="device">The mouse shown; null draws the "no device" placeholder.</param>
    /// <param name="value">The current value; null while unknown or unsupported, drawn as dashes.</param>
    /// <param name="unit">Unit beside the value, or null.</param>
    /// <param name="detail">Row below the value (STAGE n/N), or null.</param>
    /// <param name="invalid">The last command was rejected: the value is drawn as <c>?</c>.</param>
    public static void Draw(PixelSurface surface, Func<string, string> tr, string header, GearDeviceState? device,
        string? value, string? unit, string? detail, bool invalid, bool blinkOn)
    {
        if (device is null)
        {
            TileDrawing.Placeholder(surface, header, tr("NO DEVICE"), string.Empty);
            return;
        }

        PixelPalette p = surface.Palette;
        TileDrawing.Header(surface, header, null, TileState.Ok, blinkOn);

        bool asleep = device.Availability == GearAvailability.Asleep;
        if (invalid)
        {
            surface.Text("?", TileDrawing.L, HeroY, p.Warn, 3);
        }
        else if (value is null)
        {
            surface.Text("--", TileDrawing.L, HeroY, p.Dim, 3);
        }
        else
        {
            uint color = asleep ? p.Dim : p.Text;
            (int sideX, _) = TileDrawing.HeroValue(surface, value, HeroY, color, 3, unit is null ? 0 : PixelFont.Measure(unit));
            if (unit is not null)
            {
                surface.Text(unit, sideX, HeroY, p.Dim);
            }
        }

        string? row = asleep ? tr("SLEEP") : detail;
        if (row is not null)
        {
            surface.Text(TileDrawing.Fit(row, TileDrawing.W), TileDrawing.L, DetailY, p.Dim);
        }

        surface.Text(TileDrawing.Fit(device.DisplayName, TileDrawing.W), TileDrawing.L, NameY, p.Dim);
    }
}
