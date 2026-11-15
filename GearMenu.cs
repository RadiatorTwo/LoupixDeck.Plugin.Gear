using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear;

/// <summary>
/// Builds the editor menu on demand: one group per detected mouse (Pages, Polling rate, DPI, prefilled with the mouse's
/// key) plus device-independent entries that act on the first mouse. The SDK in use has no menu refresh request, so a
/// mouse that appears later shows up the next time the menu is opened.
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

        List<MenuNode> children = [];
        List<GearDeviceState> known = devices.Devices.Where(d => !string.IsNullOrEmpty(d.Key)).ToList();
        foreach (GearDeviceState device in known)
        {
            children.Add(new MenuNode { Name = GroupName(device, known), Children = DeviceEntries(device) });
        }

        if (known.Count == 0)
        {
            children.Add(new MenuNode { Name = tr("No mouse detected") });
        }

        children.AddRange(DeviceEntries(null));

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = tr("Gear"), Children = children }];
        return Task.FromResult(result);
    }

    /// <summary>The mouse's name; two mice of one model get a running number so the entries stay apart.</summary>
    private static string GroupName(GearDeviceState device, List<GearDeviceState> known)
    {
        List<GearDeviceState> sameName = known.Where(d => d.DisplayName == device.DisplayName).ToList();
        return sameName.Count > 1 ? $"{device.DisplayName} {sameName.IndexOf(device) + 1}" : device.DisplayName;
    }

    /// <summary>The entries of one mouse, or the device-independent ones (first mouse) when <paramref name="device"/> is null.</summary>
    private List<MenuNode> DeviceEntries(GearDeviceState? device)
    {
        string? key = device?.Key;
        List<MenuNode> entries =
        [
            Command(tr("Pages"), GearPagesCommand.CommandName, ("Pages", key ?? GearPageSelection.All)),
        ];

        if (device is null || device.Capabilities.HasFlag(GearCapabilities.PollingRate))
        {
            List<MenuNode> rates = [Command(tr("Next"), PollingRateCommand.CommandName, Request("Rate", GearCommandParameters.Next, key))];
            // Only the rates the mouse accepts on its current link; the device-independent entries offer none (next only).
            rates.AddRange((device?.AllowedPollingRates ?? []).Select(hz =>
                Command($"{hz} {tr("HZ")}", PollingRateCommand.CommandName, Request("Rate", hz.ToString(), key))));
            entries.Add(new MenuNode { Name = tr("Polling rate"), Children = rates });
        }

        if (device is null || device.Capabilities.HasFlag(GearCapabilities.Dpi))
        {
            entries.Add(new MenuNode
            {
                Name = tr("DPI"),
                Children =
                [
                    Command(tr("Next"), DpiCommand.CommandName, Request("Dpi", GearCommandParameters.Next, key)),
                    Command(tr("Previous"), DpiCommand.CommandName, Request("Dpi", GearCommandParameters.Previous, key)),
                ],
            });
        }

        return entries;
    }

    private static (string, string)[] Request(string name, string value, string? key) =>
        key is null ? [(name, value)] : [(name, value), ("Device", key)];

    private static MenuNode Command(string name, string commandName, params (string Name, string Value)[] parameters) => new()
    {
        Name = name,
        CommandName = commandName,
        Parameters = parameters.ToDictionary(p => p.Name, p => p.Value),
    };
}
