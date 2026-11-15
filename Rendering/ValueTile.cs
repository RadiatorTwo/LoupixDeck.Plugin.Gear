using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Rendering;

/// <summary>
/// A tile of a command that sets one setting of a mouse (polling rate, DPI): header with the setting's name, the value
/// a press sets as hero with an optional unit, the current value below it, an optional detail row and the mouse's name.
/// Anatomy follows <see cref="DevicePage"/>: header 8-16, hero 21, rows at 52/62/72.
/// </summary>
internal static class ValueTile
{
    private const int HeroY = 21;
    private const int CurrentY = 52;
    private const int DetailY = 62;
    private const int NameY = 72;

    /// <param name="header">Translated header text (POLLING, DPI).</param>
    /// <param name="device">The mouse shown; null draws the "no device" placeholder.</param>
    /// <param name="target">What a press sets (a value, NEXT, PREV); null when the request is invalid, drawn as <c>?</c>.</param>
    /// <param name="targetUnit">Unit beside the target, or null.</param>
    /// <param name="current">The current value with its unit; null while unknown or unsupported, drawn as dashes.</param>
    /// <param name="detail">Row below the current value (STAGE n/N), or null.</param>
    /// <param name="invalid">The last command was rejected: the target is drawn as <c>?</c>.</param>
    public static void Draw(PixelSurface surface, Func<string, string> tr, string header, GearDeviceState? device,
        string? target, string? targetUnit, string? current, string? detail, bool invalid, bool blinkOn)
    {
        if (device is null)
        {
            TileDrawing.Placeholder(surface, header, tr("NO DEVICE"), string.Empty);
            return;
        }

        PixelPalette p = surface.Palette;
        TileDrawing.Header(surface, header, null, TileState.Ok, blinkOn);

        bool asleep = device.Availability == GearAvailability.Asleep;
        if (invalid || target is null)
        {
            surface.Text("?", TileDrawing.L, HeroY, p.Warn, 3);
        }
        else
        {
            uint color = asleep ? p.Dim : p.Text;
            int unitWidth = targetUnit is null ? 0 : PixelFont.Measure(targetUnit);
            (int sideX, _) = TileDrawing.HeroValue(surface, target, HeroY, color, 3, unitWidth);
            if (targetUnit is not null)
            {
                surface.Text(targetUnit, sideX, HeroY, p.Dim);
            }
        }

        // A 1x row: dim label left, current value right; a missing value is "--" in dim.
        string currentText = current ?? "--";
        int currentWidth = PixelFont.Measure(currentText);
        surface.Text(TileDrawing.Fit(tr("NOW"), TileDrawing.W - currentWidth - 3), TileDrawing.L, CurrentY, p.Dim);
        surface.TextRight(currentText, TileDrawing.R, CurrentY, current is null || asleep ? p.Dim : p.Text);

        string? row = asleep ? tr("SLEEP") : detail;
        if (row is not null)
        {
            surface.Text(TileDrawing.Fit(row, TileDrawing.W), TileDrawing.L, DetailY, p.Dim);
        }

        surface.Text(TileDrawing.Fit(device.DisplayName, TileDrawing.W), TileDrawing.L, NameY, p.Dim);
    }
}
