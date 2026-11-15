using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Gear.Tests.Rendering;

public sealed class GearPagesCommandTests
{
    private sealed class Source : IGearDeviceSource
    {
        public IReadOnlyList<GearDeviceState> Devices { get; } = [];
    }

    private static CommandContext Context(string[] parameters) => new()
    {
        Parameters = parameters,
        Target = ButtonTargets.TouchButton,
        Host = null!,
    };

    [Fact]
    public async Task Execute_NullParameters_DoesNotThrow()
    {
        GearPagesCommand command = new(new Source(), text => text);

        await command.Execute(Context(null!));
        await command.Execute(Context([]));
    }

    [Fact]
    public void Descriptor_MatchesPublicApi()
    {
        GearPagesCommand command = new(new Source(), text => text);

        Assert.Equal("Gear.Pages", command.Descriptor.CommandName);
        Assert.Equal("({Pages})", command.Descriptor.ParameterTemplate);
    }
}
