using System.Globalization;
using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear;

/// <summary>
/// Builds the editor menu on demand: one paging tile over all mice, then one group per detected mouse (its page,
/// polling rate and DPI tiles, prefilled with the mouse's key). The commands themselves are hidden from the static
/// list. The SDK in use has no menu refresh request, so a mouse that appears later shows up the next time the menu is
/// opened.
/// </summary>
internal sealed class GearMenu(IGearDeviceSource devices, Func<string, string> tr) : IMenuContributor
{
    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        // The Gear commands are touch-button tiles.
        if (target != ButtonTargets.TouchButton)
        {
            return Task.FromResult<IReadOnlyList<MenuNode>>([]);
        }

        List<MenuNode> children =
        [
            Command(tr("All mice (press to cycle)"), GearPagesCommand.CommandName, ("Pages", GearPageSelection.All)),
        ];

        List<GearDeviceState> known = devices.Devices.Where(d => !string.IsNullOrEmpty(d.Key)).ToList();
        foreach (GearDeviceState device in known)
        {
            children.Add(new MenuNode { Name = GroupName(device, known), Children = DeviceEntries(device) });
        }

        if (known.Count == 0)
        {
            children.Add(new MenuNode { Name = tr("No mouse detected") });
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = tr("Gear"), Children = children }];
        return Task.FromResult(result);
    }

    /// <summary>The mouse's name; two mice of one model get a running number so the entries stay apart.</summary>
    private static string GroupName(GearDeviceState device, List<GearDeviceState> known)
    {
        List<GearDeviceState> sameName = known.Where(d => d.DisplayName == device.DisplayName).ToList();
        return sameName.Count > 1 ? $"{device.DisplayName} {sameName.IndexOf(device) + 1}" : device.DisplayName;
    }

    /// <summary>The entries of one mouse, all prefilled with its key.</summary>
    private List<MenuNode> DeviceEntries(GearDeviceState device)
    {
        string key = device.Key!;
        List<MenuNode> entries =
        [
            Command(tr("Device page"), GearPagesCommand.CommandName, ("Pages", key)),
        ];

        if (device.Capabilities.HasFlag(GearCapabilities.PollingRate))
        {
            List<MenuNode> rates = [Command(tr("Next"), PollingRateCommand.CommandName, Request("Rate", GearCommandParameters.Next, key))];
            // Only the rates the mouse accepts on its current link.
            rates.AddRange((device.AllowedPollingRates ?? []).Select(hz =>
                Command($"{hz} {tr("HZ")}", PollingRateCommand.CommandName,
                    Request("Rate", hz.ToString(CultureInfo.InvariantCulture), key))));
            entries.Add(new MenuNode { Name = tr("Polling rate"), Children = rates });
        }

        if (device.Capabilities.HasFlag(GearCapabilities.Dpi))
        {
            List<MenuNode> dpis =
            [
                Command(tr("Next"), DpiCommand.CommandName, Request("Dpi", GearCommandParameters.Next, key)),
                Command(tr("Previous"), DpiCommand.CommandName, Request("Dpi", GearCommandParameters.Previous, key)),
            ];
            // Fixed values: the mouse's DPI stages as last read (Pulsar: stored stages; Logitech: the ladder).
            dpis.AddRange((device.Dpi?.Stages ?? []).Distinct().Order().Select(dpi =>
                Command($"{dpi} {tr("DPI")}", DpiCommand.CommandName,
                    Request("Dpi", dpi.ToString(CultureInfo.InvariantCulture), key))));
            entries.Add(new MenuNode { Name = tr("DPI"), Children = dpis });
        }

        return entries;
    }

    private static (string, string)[] Request(string name, string value, string key) => [(name, value), ("Device", key)];

    private static MenuNode Command(string name, string commandName, params (string Name, string Value)[] parameters) => new()
    {
        Name = name,
        CommandName = commandName,
        Parameters = parameters.ToDictionary(p => p.Name, p => p.Value),
    };
}
