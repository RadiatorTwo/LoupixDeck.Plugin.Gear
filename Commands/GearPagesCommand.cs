using System.Collections.Concurrent;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Rendering;
using LoupixDeck.PluginSdk;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// A paging tile: one page per mouse plus an overview, and a key press moves that button to the next page. The
/// "Pages" parameter is empty or "all" (every device and the overview) or '|'-separated tokens of device keys and
/// "overview" ("pulsar:model:5717|overview"). Several Gear.Pages commands chained on one button add up to one
/// cycle in sequence order. Pages without a device are skipped and the header n/N counts only the pages shown.
/// The current page is kept per button and resets on a restart.
/// </summary>
internal sealed class GearPagesCommand(IGearDeviceSource devices, Func<string, string> tr) : IAnimatedDisplayCommand, IDisplayImageCommand
{
    public const string CommandName = "Gear.Pages";

    private readonly ConcurrentDictionary<string, Cycle> _cycles = new(StringComparer.Ordinal);

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = CommandName,
        DisplayName = "Gear Pages",
        Group = "Gear",
        Icon = "\U000F0379",
        Description = "Show battery, polling rate and DPI of your mice on a touch button; press it for the next page",
        ParameterTemplate = "({Pages})",
        Parameters = [new CommandParameter("Pages", typeof(string))],
        // The tile fills the whole key; no host icon or caption on top of it.
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public int TargetFps => PixelTile.TargetFps;

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The host runs every command of a button's chain on a press, so a button with N chained
    /// Gear.Pages commands calls this N times per press; the page advances once per N calls. N is
    /// learned from the render call, which sees the whole chain.
    /// </summary>
    public Task Execute(CommandContext ctx)
    {
        try
        {
            _cycles.GetOrAdd(PositionKey(ctx), _ => new Cycle()).Press();
        }
        catch (Exception)
        {
            // A key press must never throw into the host; the page simply stays.
        }

        return Task.CompletedTask;
    }

    public AnimationFrameInfo RenderAnimatedFrame(CommandContext ctx, IRenderCanvas canvas, AnimationFrameContext frame) =>
        PixelTile.Render(ctx, canvas, false, surface => Draw(ctx, surface, TileDrawing.BlinkOn(frame.Elapsed)));

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        PixelTile.Render(ctx, canvas, false, surface => Draw(ctx, surface, PixelTile.WallClockBlink()));
        return true;
    }

    private void Draw(CommandContext ctx, PixelSurface surface, bool blinkOn)
    {
        IReadOnlyList<GearDeviceState> snapshot = devices.Devices;
        (IReadOnlyList<string?> selections, int commandsPerPress) = Selections(ctx);
        IReadOnlyList<GearPage> pages = GearPageSelection.Resolve(selections, snapshot);
        if (pages.Count == 0)
        {
            TileDrawing.Placeholder(surface, tr("GEAR"), tr("NO DEVICE"), string.Empty);
            return;
        }

        Cycle cycle = _cycles.GetOrAdd(PositionKey(ctx), _ => new Cycle());
        cycle.CommandsPerPress = commandsPerPress;
        int index = cycle.Position % pages.Count;
        string? pageIndex = pages.Count > 1 ? $"{index + 1}/{pages.Count}" : null;
        if (pages[index].Device is { } device)
        {
            DevicePage.Draw(surface, device, pageIndex, blinkOn, tr);
        }
        else
        {
            OverviewPage.Draw(surface, snapshot, pageIndex, blinkOn, tr);
        }
    }

    /// <summary>
    /// The page lists of this button in chain order, and how many Gear.Pages commands a press
    /// executes. A single-command button reports no sequence, so its own parameters are the list.
    /// Without a button key the position is keyed by the first command's own list, which only that
    /// command's Execute advances, so one call per press counts there.
    /// </summary>
    private static (IReadOnlyList<string?> Selections, int CommandsPerPress) Selections(CommandContext ctx)
    {
        List<SequenceCommand> chained = ctx.SequenceCommands.Where(c => c.Name == CommandName).ToList();
        if (chained.Count == 0)
        {
            return (ctx.Parameters.Length == 0 ? new string?[] { null } : ctx.Parameters, 1);
        }

        List<string?> selections = chained.Select(c => c.Parameters.Length > 0 ? (string?)c.Parameters[0] : null).ToList();
        bool keyed = ButtonKeys.For(ctx, string.Empty).Length > 0;
        return (selections, keyed ? chained.Count : 1);
    }

    /// <summary>The pressed/rendered button; on a host without button keys, every button whose
    /// (first) command carries the same page list shares one position.</summary>
    private static string PositionKey(CommandContext ctx) =>
        ButtonKeys.For(ctx, "pages:" + string.Join("|", ctx.Parameters ?? []));

    /// <summary>Page position of one button, advanced once per press.</summary>
    private sealed class Cycle
    {
        private readonly Lock _gate = new();
        private int _calls;

        public int Position { get; private set; }

        public int CommandsPerPress { get; set; } = 1;

        public void Press()
        {
            lock (_gate)
            {
                if (++_calls < Math.Max(1, CommandsPerPress))
                {
                    return;
                }

                _calls = 0;
                Position++;
            }
        }
    }
}
