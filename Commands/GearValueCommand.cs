using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Rendering;
using LoupixDeck.PluginSdk;
using LoupixDeck.RenderKit.Pixel;
using LoupixDeck.RenderKit.Tiles;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// Shared shell of Gear.PollingRate and Gear.Dpi: a touch tile showing one setting of a mouse; a press changes it.
/// Parameters are the request and an optional device key (empty: the first device). A rejected request is logged,
/// nothing is sent and the tile shows "?" until the device list changes (the registry replaces its snapshot on
/// every state change).
/// </summary>
internal abstract class GearValueCommand(IGearDeviceControl devices, Func<string, string> tr, IPluginLogger logger)
    : IAnimatedDisplayCommand, IDisplayImageCommand
{
    private volatile IReadOnlyList<GearDeviceState>? _rejectedAt;

    public abstract CommandDescriptor Descriptor { get; }

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public int TargetFps => PixelTile.TargetFps;

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(500);

    protected IGearDeviceControl Devices => devices;

    protected Func<string, string> Tr => tr;

    protected abstract string Header { get; }

    protected abstract GearCapabilities Capability { get; }

    /// <summary>Sends the request; false: it was rejected (or failed) and the tile shows "?".</summary>
    protected abstract Task<bool> Send(string? request, string? deviceKey);

    protected abstract (string? Value, string? Unit, string? Detail) Describe(GearDeviceState device);

    public async Task Execute(CommandContext ctx)
    {
        try
        {
            IReadOnlyList<GearDeviceState> snapshot = devices.Devices;
            string? request = Parameter(ctx, 0);
            string? deviceKey = Parameter(ctx, 1);
            if (devices.Find(deviceKey) is not { } device || !device.Capabilities.HasFlag(Capability))
            {
                return;
            }

            if (await Send(request, deviceKey))
            {
                _rejectedAt = null;
            }
            else
            {
                logger.Warn($"Gear: {Descriptor.CommandName} did not apply '{request}' to '{device.DisplayName}'.");
                _rejectedAt = snapshot;
            }

            ctx.Host?.RequestButtonRefresh(Descriptor.CommandName);
        }
        catch (Exception ex)
        {
            // A key press must never throw into the host.
            try
            {
                logger.Error($"Gear: {Descriptor.CommandName} failed.", ex);
            }
            catch (Exception)
            {
                // The logger is best effort as well.
            }
        }
    }

    public AnimationFrameInfo RenderAnimatedFrame(CommandContext ctx, IRenderCanvas canvas, AnimationFrameContext frame) =>
        PixelTile.Render(ctx, canvas, false, surface => Draw(ctx, surface, TileDrawing.BlinkOn(frame.Elapsed)));

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        PixelTile.Render(ctx, canvas, false, surface => Draw(ctx, surface, PixelTile.WallClockBlink()));
        return true;
    }

    internal void Draw(CommandContext ctx, PixelSurface surface, bool blinkOn)
    {
        IReadOnlyList<GearDeviceState> snapshot = devices.Devices;
        GearDeviceState? device = devices.Find(Parameter(ctx, 1));
        bool invalid = ReferenceEquals(_rejectedAt, snapshot);
        (string? value, string? unit, string? detail) = device is not null && device.Capabilities.HasFlag(Capability)
            ? Describe(device)
            : (null, null, null);
        ValueTile.Draw(surface, tr, tr(Header), device, value, unit, detail, invalid, blinkOn);
    }

    /// <summary>A parameter by position; null when absent or empty. The host may pass no parameter array at all.</summary>
    private static string? Parameter(CommandContext ctx, int index) =>
        ctx.Parameters is { } parameters && index < parameters.Length && !string.IsNullOrWhiteSpace(parameters[index])
            ? parameters[index].Trim()
            : null;
}
