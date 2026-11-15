using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Commands;

/// <summary>
/// Shows the polling rate of a mouse; a press sets it to <c>next</c> (cycles the rates its link allows) or to a rate in Hz.
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
        Icon = "\U000F0379",
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

    protected override (string? Value, string? Unit, string? Detail) Describe(GearDeviceState device) =>
        (device.PollingRate?.ToString(), device.PollingRate is null ? null : Tr("HZ"), null);
}
