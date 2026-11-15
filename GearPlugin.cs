using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Pulsar;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear;

/// <summary>
/// Detects gaming mice and shows battery, polling rate and DPI.
/// </summary>
public sealed class GearPlugin : LoupixPlugin, IMenuContributor
{
    private static readonly string[] RefreshedCommands = [GearPagesCommand.CommandName, PollingRateCommand.CommandName, DpiCommand.CommandName];

    private readonly List<IPluginCommand> _commands = [];

    private IPluginHost? _host;
    private GearDeviceRegistry? _registry;
    private GearMenu? _menu;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "gear",
        Name = "Gear",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "RadiatorTwo",
        Description = "Detects gaming mice and shows battery, polling rate and DPI; switches polling rate and DPI.",
        Icon = LoadIcon()
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;

        try
        {
            // Without a transport the commands are still registered; they show NO DEVICE.
            IHidTransport transport;
            try
            {
                transport = HidTransports.Create();
            }
            catch (Exception ex)
            {
                host.Logger.Error("Gear: the HID transport could not be created; no mouse will be detected.", ex);
                transport = new UnsupportedHidTransport();
            }

            GearDeviceRegistry registry = new(transport, [new PulsarProvider()], host.Logger, TimeProvider.System);
            _registry = registry;

            _commands.Add(new GearPagesCommand(registry, host.Tr));
            _commands.Add(new PollingRateCommand(registry, host.Tr, host.Logger));
            _commands.Add(new DpiCommand(registry, host.Tr, host.Logger));
            _menu = new GearMenu(registry, host.Tr);

            registry.Changed += OnDevicesChanged;
            registry.Start();
        }
        catch (Exception ex)
        {
            host.Logger.Error("Gear: the plugin could not be initialized.", ex);
            Shutdown();
        }
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target) =>
        _menu?.GetMenuNodes(target) ?? Task.FromResult<IReadOnlyList<MenuNode>>([]);

    public override void Shutdown()
    {
        if (_registry is { } registry)
        {
            registry.Changed -= OnDevicesChanged;
            registry.Dispose();
        }

        _registry = null;
        _menu = null;
        _commands.Clear();
        _host = null;
    }

    private void OnDevicesChanged()
    {
        IPluginHost? host = _host;
        if (host is null) return;

        try
        {
            foreach (string commandName in RefreshedCommands)
            {
                host.RequestButtonRefresh(commandName);
            }
        }
        catch (Exception ex)
        {
            host.Logger.Error("Gear: refreshing the buttons failed.", ex);
        }
    }

    private static byte[]? LoadIcon()
    {
        using Stream? stream = typeof(GearPlugin).Assembly.GetManifestResourceStream("LoupixDeck.Plugin.Gear.icon.png");
        if (stream == null) return null;

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
