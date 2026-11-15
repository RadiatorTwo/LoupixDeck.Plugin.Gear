using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// Shows the active DPI value and stage of a mouse; a press selects the <c>next</c> or <c>previous</c> stage or sets a DPI value.
/// </summary>
internal sealed class DpiCommand(IGearDeviceControl devices, Func<string, string> tr, IPluginLogger logger)
    : GearValueCommand(devices, tr, logger)
{
    public const string CommandName = "Gear.Dpi";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = CommandName,
        DisplayName = "Gear DPI",
        Group = "Gear",
        Icon = "\U000F0379",
        Description = "Show the DPI of a mouse and switch it: next, previous or a DPI value",
        ParameterTemplate = "({Dpi},{Device})",
        Parameters = [new CommandParameter("Dpi", typeof(string)), new CommandParameter("Device", typeof(string))],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    protected override string Header => "DPI";

    protected override GearCapabilities Capability => GearCapabilities.Dpi;

    protected override Task<bool> Send(string? request, string? deviceKey)
    {
        GearRequest parsed = GearCommandParameters.ParseDpi(request);
        return parsed.Kind switch
        {
            GearRequestKind.Next => Devices.StepDpiAsync(deviceKey, 1),
            GearRequestKind.Previous => Devices.StepDpiAsync(deviceKey, -1),
            GearRequestKind.Value => Devices.SetDpiAsync(deviceKey, parsed.Value),
            _ => Task.FromResult(false),
        };
    }

    protected override (string? Value, string? Unit, string? Detail) Describe(GearDeviceState device)
    {
        if (device.Dpi is not { } dpi || dpi.ActiveStage < 0 || dpi.ActiveStage >= dpi.Stages.Count)
        {
            return (null, null, null);
        }

        return (dpi.Stages[dpi.ActiveStage].ToString(), null, $"{Tr("STAGE")} {dpi.ActiveStage + 1}/{dpi.Stages.Count}");
    }
}
