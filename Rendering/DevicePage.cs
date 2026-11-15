using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Rendering;

/// <summary>
/// One mouse on a tile: header (name, n/N), battery hero with unit and charging bolt, battery bar and the
/// HZ / DPI / LINK rows. A mouse without a battery shows its DPI as the hero instead, with no bar and only the
/// HZ / LINK rows. Coordinates follow the shared page anatomy: header 8-16, hero 21, bar 45, rows at 52/62/72.
/// </summary>
internal static class DevicePage
{
    public const int WarnPercent = 20;
    public const int CriticalPercent = 10;

    private const int HeroY = 21;
    private const int BarY = 45;
    private const int BarHeight = 3;
    private const int FirstRowY = 52;
    private const int RowPitch = 10;

    // 5x7 lightning bolt, drawn when the mouse is charging.
    private static readonly string[] Bolt =
    [
        "...##",
        "..##.",
        ".##..",
        "#####",
        "..##.",
        ".##..",
        "##...",
    ];

    /// <summary>Battery state of a mouse for the tile: charging is never alarming, otherwise warn at 20 % and critical at 10 %.</summary>
    public static TileState StateOf(GearDeviceState device)
    {
        if (device.Charging || device.Percent is not { } percent)
        {
            return TileState.Ok;
        }

        return percent <= CriticalPercent ? TileState.Critical : percent <= WarnPercent ? TileState.Warn : TileState.Ok;
    }

    /// <param name="pageIndex">"n/N" for the header, or null on a tile that shows a single page.</param>
    /// <param name="tr">Translates the pixel-font texts (English keys).</param>
    public static void Draw(PixelSurface surface, GearDeviceState device, string? pageIndex, bool blinkOn, Func<string, string> tr)
    {
        PixelPalette p = surface.Palette;
        TileState state = StateOf(device);
        TileDrawing.Header(surface, device.DisplayName, pageIndex, state, blinkOn);

        bool asleep = device.Availability == GearAvailability.Asleep;
        string? dpi = ActiveDpi(device);
        if (!device.Capabilities.HasFlag(GearCapabilities.Battery))
        {
            // A wired mouse has no battery: the DPI takes the hero spot and its row is dropped.
            if (dpi != null)
            {
                string unit = tr("DPI");
                (int sideX, _) = TileDrawing.HeroValue(surface, dpi, HeroY, asleep ? p.Dim : p.Text, 3, PixelFont.Measure(unit));
                surface.Text(unit, sideX, HeroY, p.Dim);
            }
            else
            {
                surface.Text("--", TileDrawing.L, HeroY, p.Dim, 3);
            }

            DrawRow(surface, tr("HZ"), device.PollingRate?.ToString(), FirstRowY, p);
            DrawRow(surface, tr("LINK"), LinkText(device, tr), FirstRowY + RowPitch, p);
            return;
        }

        if (device.Percent is { } percent)
        {
            uint color = asleep ? p.Dim : TileDrawing.StateColor(p, state);
            (int sideX, _) = TileDrawing.HeroValue(surface, percent.ToString(), HeroY, color, 3, PixelFont.Measure("%"));
            surface.Text("%", sideX, HeroY, p.Dim);
            TileDrawing.Bar(surface, percent / 100.0, state, BarY, BarHeight);
        }
        else
        {
            surface.Text("--", TileDrawing.L, HeroY, p.Dim, 3);
        }

        if (device.Charging)
        {
            DrawBolt(surface, TileDrawing.R - 4, HeroY, p.Accent);
        }

        DrawRow(surface, tr("HZ"), device.PollingRate?.ToString(), FirstRowY, p);
        DrawRow(surface, tr("DPI"), dpi, FirstRowY + RowPitch, p);
        DrawRow(surface, tr("LINK"), LinkText(device, tr), FirstRowY + (2 * RowPitch), p);
    }

    /// <summary>The DPI of the active stage as text; null when unknown.</summary>
    public static string? ActiveDpi(GearDeviceState device) =>
        device.Dpi is { } d && d.ActiveStage >= 0 && d.ActiveStage < d.Stages.Count ? d.Stages[d.ActiveStage].ToString() : null;

    private static string? LinkText(GearDeviceState device, Func<string, string> tr) => device.Availability switch
    {
        GearAvailability.Asleep => tr("SLEEP"),
        GearAvailability.NoAccess => tr("NO ACCESS"),
        GearAvailability.Error => tr("ERROR"),
        _ => device.Link.ToDisplayText(),
    };

    // A 1x row: dim label left, value right; a missing value is "--" in dim.
    private static void DrawRow(PixelSurface surface, string label, string? value, int y, PixelPalette p)
    {
        bool known = !string.IsNullOrEmpty(value);
        string text = known ? value! : "--";
        int valueWidth = PixelFont.Measure(text);
        surface.Text(TileDrawing.Fit(label, TileDrawing.W - valueWidth - 3), TileDrawing.L, y, p.Dim);
        surface.TextRight(text, TileDrawing.R, y, known ? p.Text : p.Dim);
    }

    private static void DrawBolt(PixelSurface surface, int x, int y, uint color)
    {
        for (int row = 0; row < Bolt.Length; row++)
        {
            for (int column = 0; column < Bolt[row].Length; column++)
            {
                if (Bolt[row][column] == '#')
                {
                    surface.Fill(x + column, y + row, 1, 1, color);
                }
            }
        }
    }
}
