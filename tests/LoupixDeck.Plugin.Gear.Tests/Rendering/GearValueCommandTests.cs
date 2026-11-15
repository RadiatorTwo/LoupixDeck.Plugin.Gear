using LoupixDeck.Plugin.Gear.Commands;
using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.PluginSdk;
using LoupixDeck.RenderKit.Pixel;

namespace LoupixDeck.Plugin.Gear.Tests.Rendering;

public sealed class GearValueCommandTests
{
    private sealed class FakeControl : IGearDeviceControl
    {
        public List<string> Calls { get; } = [];

        public bool Result { get; set; } = true;

        public bool Throw { get; set; }

        public IReadOnlyList<GearDeviceState> Devices { get; set; } = [];

        /// <summary>Replaces the snapshot while a write is in flight, like a registry refresh after a failed write.</summary>
        public bool SwapDuringWrite { get; set; }

        public GearDeviceState? Find(string? key) =>
            string.IsNullOrEmpty(key) ? Devices.FirstOrDefault() : Devices.FirstOrDefault(d => d.Key == key);

        public Task<bool> SetPollingRateAsync(string? key, int hz) => Record($"set-rate:{key}:{hz}");

        public Task<bool> CyclePollingRateAsync(string? key) => Record($"cycle-rate:{key}");

        public Task<bool> SetDpiAsync(string? key, int dpi) => Record($"set-dpi:{key}:{dpi}");

        public Task<bool> StepDpiAsync(string? key, int direction) => Record($"step-dpi:{key}:{direction}");

        private Task<bool> Record(string call)
        {
            Calls.Add(call);
            if (SwapDuringWrite)
            {
                Devices = [Mouse()];
            }

            return Throw ? throw new InvalidOperationException("boom") : Task.FromResult(Result);
        }
    }

    private sealed class FakeLogger : IPluginLogger
    {
        public List<string> Warnings { get; } = [];

        public void Info(string message) { }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) { }
    }

    private static GearDeviceState Mouse(
        GearCapabilities caps = GearCapabilities.Battery | GearCapabilities.PollingRate | GearCapabilities.Dpi,
        GearAvailability availability = GearAvailability.Awake) =>
        new("pulsar:model:060a", "X2 CrazyLight", GearLink.Wireless8K, caps, true, availability, 80, false, null, 1000,
            new GearDpiState([800, 1600, 3200], 1), DateTimeOffset.UnixEpoch);

    private static CommandContext Context(params string[] parameters) => new()
    {
        Parameters = parameters,
        Target = ButtonTargets.TouchButton,
        Host = null!,
    };

    private static long Hash(Action<PixelSurface> draw)
    {
        PixelSurface surface = new(90, 90, PixelPalette.Default);
        draw(surface);
        return surface.ContentHash();
    }

    [Fact]
    public void ParseRate_Next() => Assert.Equal(new GearRequest(GearRequestKind.Next), GearCommandParameters.ParseRate(" Next "));

    [Fact]
    public void ParseRate_Number() => Assert.Equal(new GearRequest(GearRequestKind.Value, 4000), GearCommandParameters.ParseRate("4000"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("fast")]
    [InlineData("1234")]
    [InlineData("-1000")]
    [InlineData("1000hz")]
    public void ParseRate_Garbage_Invalid(string? text) => Assert.Equal(GearRequestKind.Invalid, GearCommandParameters.ParseRate(text).Kind);

    [Fact]
    public void ParseDpi_Previous() => Assert.Equal(new GearRequest(GearRequestKind.Previous), GearCommandParameters.ParseDpi("previous"));

    [Fact]
    public void ParseDpi_NextAndNumber()
    {
        Assert.Equal(GearRequestKind.Next, GearCommandParameters.ParseDpi("next").Kind);
        Assert.Equal(new GearRequest(GearRequestKind.Value, 1600), GearCommandParameters.ParseDpi("1600"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public void ParseDpi_Garbage_Invalid(string text) => Assert.Equal(GearRequestKind.Invalid, GearCommandParameters.ParseDpi(text).Kind);

    [Fact]
    public async Task Execute_InvalidRate_SendsNothing()
    {
        FakeControl control = new() { Devices = [Mouse()] };
        FakeLogger logger = new();
        PollingRateCommand command = new(control, t => t, logger);

        await command.Execute(Context("fast"));

        Assert.Empty(control.Calls);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Execute_Rate_CallsRegistryWithDevice()
    {
        FakeControl control = new() { Devices = [Mouse()] };
        PollingRateCommand command = new(control, t => t, new FakeLogger());

        await command.Execute(Context("next", "pulsar:model:060a"));
        await command.Execute(Context("2000"));

        Assert.Equal(["cycle-rate:pulsar:model:060a", "set-rate::2000"], control.Calls);
    }

    [Fact]
    public async Task Execute_RateRejectedByDevice_WarnsAndShowsQuestionMarkUntilStateChanges()
    {
        FakeControl control = new() { Devices = [Mouse()], Result = false };
        FakeLogger logger = new();
        PollingRateCommand command = new(control, t => t, logger);
        CommandContext ctx = Context("8000");
        long before = Hash(s => command.Draw(ctx, s, true));

        await command.Execute(ctx);
        long invalid = Hash(s => command.Draw(ctx, s, true));
        control.Devices = [Mouse()];
        long after = Hash(s => command.Draw(ctx, s, true));

        Assert.Single(logger.Warnings);
        Assert.NotEqual(before, invalid);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Execute_NullParameters_DoNotThrowAndSendNothing()
    {
        FakeControl control = new() { Devices = [Mouse()] };

        await new PollingRateCommand(control, t => t, new FakeLogger()).Execute(Context(null!));
        await new DpiCommand(control, t => t, new FakeLogger()).Execute(Context(null!));

        Assert.Empty(control.Calls);
    }

    [Fact]
    public async Task Execute_ThrowingRegistry_DoesNotThrow()
    {
        FakeControl control = new() { Devices = [Mouse()], Throw = true };

        await new PollingRateCommand(control, t => t, new FakeLogger()).Execute(Context("next"));
        await new DpiCommand(control, t => t, new FakeLogger()).Execute(Context("next"));
    }

    [Fact]
    public async Task Execute_Dpi_StepsAndSets()
    {
        FakeControl control = new() { Devices = [Mouse()] };
        DpiCommand command = new(control, t => t, new FakeLogger());

        await command.Execute(Context("next"));
        await command.Execute(Context("previous", "pulsar:model:060a"));
        await command.Execute(Context("3200"));

        Assert.Equal(["step-dpi::1", "step-dpi:pulsar:model:060a:-1", "set-dpi::3200"], control.Calls);
    }

    [Fact]
    public async Task Execute_DpiWithoutCapabilityOrDevice_DoesNothing()
    {
        FakeControl control = new() { Devices = [Mouse(GearCapabilities.Battery)] };
        DpiCommand command = new(control, t => t, new FakeLogger());

        await command.Execute(Context("next"));
        control.Devices = [];
        await command.Execute(Context("next"));

        Assert.Empty(control.Calls);
    }

    [Fact]
    public void Descriptors_MatchPublicApi()
    {
        PollingRateCommand rate = new(new FakeControl(), t => t, new FakeLogger());
        DpiCommand dpi = new(new FakeControl(), t => t, new FakeLogger());

        Assert.Equal("Gear.PollingRate", rate.Descriptor.CommandName);
        Assert.Equal("({Rate},{Device})", rate.Descriptor.ParameterTemplate);
        Assert.Equal("Gear.Dpi", dpi.Descriptor.CommandName);
        Assert.Equal("({Dpi},{Device})", dpi.Descriptor.ParameterTemplate);
    }

    [Fact]
    public void Draw_StatesDifferFromEachOtherAndNeverThrow()
    {
        GearDeviceState[][] states =
        [
            [],
            [Mouse()],
            [Mouse(availability: GearAvailability.Asleep)],
            [Mouse(GearCapabilities.Battery)],
            [Mouse() with { Dpi = null, PollingRate = null }],
        ];

        List<long> rate = [];
        List<long> dpi = [];
        foreach (GearDeviceState[] devices in states)
        {
            FakeControl control = new() { Devices = devices };
            PollingRateCommand pollingCommand = new(control, t => t, new FakeLogger());
            DpiCommand dpiCommand = new(control, t => t, new FakeLogger());
            rate.Add(Hash(s => pollingCommand.Draw(Context("next"), s, true)));
            dpi.Add(Hash(s => dpiCommand.Draw(Context("next"), s, true)));
        }

        Assert.DoesNotContain(new PixelSurface(90, 90, PixelPalette.Default).ContentHash(), rate.Concat(dpi));
        // no device, awake and asleep each look different; a mouse without data shows dashes
        Assert.Equal(3, rate.Take(3).Distinct().Count());
        Assert.Equal(3, dpi.Take(3).Distinct().Count());
    }

    [Fact]
    public async Task Execute_Rejection_MarksOnlyThePressedButton()
    {
        FakeControl control = new() { Devices = [Mouse()], Result = false };
        PollingRateCommand command = new(control, t => t, new FakeLogger());
        CommandContext pressed = Context("8000");
        CommandContext other = Context("2000");
        long otherBefore = Hash(s => command.Draw(other, s, true));
        long pressedBefore = Hash(s => command.Draw(pressed, s, true));

        await command.Execute(pressed);

        Assert.NotEqual(pressedBefore, Hash(s => command.Draw(pressed, s, true)));
        Assert.Equal(otherBefore, Hash(s => command.Draw(other, s, true)));
    }

    [Fact]
    public async Task Execute_RejectionWhileRegistryRefreshesDuringWrite_StillShowsQuestionMark()
    {
        FakeControl control = new() { Devices = [Mouse()], Result = false, SwapDuringWrite = true };
        PollingRateCommand command = new(control, t => t, new FakeLogger());
        CommandContext ctx = Context("8000");
        control.SwapDuringWrite = false;
        long before = Hash(s => command.Draw(ctx, s, true));
        control.SwapDuringWrite = true;

        await command.Execute(ctx);

        Assert.NotEqual(before, Hash(s => command.Draw(ctx, s, true)));
    }
}
