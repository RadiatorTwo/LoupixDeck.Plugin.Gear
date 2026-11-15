using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// Shows the polling rate a press applies, with the current rate below; a press sets it to <c>next</c> (cycles the rates its link allows) or to a rate in Hz.
/// </summary>
internal sealed class PollingRateCommand(IGearDeviceControl devices, Func<string, string> tr, IPluginLogger logger)
    : GearValueCommand(devices, tr, logger)
{
    public const string CommandName = "Gear.PollingRate";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = CommandName,
        DisplayName = "Gear Polling Rate",
        Group = "Gear",
        Icon = "\U000F037D", // mdi-mouse
        // Reached through the dynamic menu (GearMenu), prefilled per mouse.
        HiddenFromMenu = true,
        Description = "Show the polling rate of a mouse and switch it: next or a rate in Hz",
        ParameterTemplate = "({Rate},{Device})",
        Parameters = [new CommandParameter("Rate", typeof(string)), new CommandParameter("Device", typeof(string))],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    protected override string Header => "POLLING";

    protected override GearCapabilities Capability => GearCapabilities.PollingRate;

    protected override Task<bool> Send(string? request, string? deviceKey)
    {
        GearRequest parsed = GearCommandParameters.ParseRate(request);
        return parsed.Kind switch
        {
            GearRequestKind.Next => Devices.CyclePollingRateAsync(deviceKey),
            GearRequestKind.Value => Devices.SetPollingRateAsync(deviceKey, parsed.Value),
            _ => Task.FromResult(false),
        };
    }

    protected override (string? Text, string? Unit) Target(string? request)
    {
        GearRequest parsed = GearCommandParameters.ParseRate(request);
        return parsed.Kind switch
        {
            GearRequestKind.Next => (Tr("NEXT"), null),
            GearRequestKind.Value => (parsed.Value.ToString(), Tr("HZ")),
            _ => (null, null),
        };
    }

    protected override (string? Current, string? Detail) Describe(GearDeviceState device) =>
        (device.PollingRate is { } hz ? $"{hz} {Tr("HZ")}" : null, null);
}
