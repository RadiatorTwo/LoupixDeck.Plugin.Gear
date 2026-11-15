using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear;

/// <summary>
/// Detects gaming mice and shows battery, polling rate and DPI.
/// </summary>
public sealed class GearPlugin : LoupixPlugin
{
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
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];

    private static byte[]? LoadIcon()
    {
        using Stream? stream = typeof(GearPlugin).Assembly.GetManifestResourceStream("LoupixDeck.Plugin.Gear.icon.png");
        if (stream == null) return null;

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
