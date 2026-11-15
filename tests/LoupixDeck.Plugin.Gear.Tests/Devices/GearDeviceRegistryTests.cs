using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.PluginSdk;
using Microsoft.Extensions.Time.Testing;

namespace LoupixDeck.Plugin.Gear.Tests.Devices;

public sealed class GearDeviceRegistryTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeTransport _transport = new();
    private readonly FakeProvider _provider = new();
    private readonly FakeLogger _logger = new();
    private readonly GearDeviceRegistry _registry;

    public GearDeviceRegistryTests()
    {
        _registry = new GearDeviceRegistry(_transport, [_provider], _logger, _time);
    }

    public void Dispose() => _registry.Dispose();

    private async Task<FakeDevice> PlugInAsync(string path = "p1", Action<FakeDevice>? configure = null)
    {
        _transport.Paths.Add(path);
        FakeDevice device = new() { Key = "key-" + path };
        configure?.Invoke(device);
        _provider.Devices[path] = device;
        await _registry.TickAsync();
        return device;
    }

    [Fact]
    public async Task Registry_NewDevice_ReadsImmediately()
    {
        FakeDevice device = await PlugInAsync();

        GearDeviceState state = Assert.Single(_registry.Devices);
        Assert.Equal(1, device.IdentityCalls);
        Assert.Equal(1, device.PowerCalls);
        Assert.Equal(GearAvailability.Awake, state.Availability);
        Assert.Equal(80, state.Percent);
        Assert.Equal(1000, state.PollingRate);
        Assert.Equal(1, state.Dpi?.ActiveStage);
        Assert.Equal("key-p1", state.Key);
        Assert.Equal(_time.GetUtcNow(), state.LastContact);
    }

    [Fact]
    public async Task Registry_Awake_PollsPowerEvery5Min()
    {
        FakeDevice device = await PlugInAsync();

        _time.Advance(TimeSpan.FromMinutes(4));
        await _registry.TickAsync();
        Assert.Equal(1, device.PowerCalls);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _registry.TickAsync();
        Assert.Equal(2, device.PowerCalls);
        Assert.Equal(1, device.IdentityCalls);
    }

    [Fact]
    public async Task Registry_AsleepAtStart_NoBatteryQuery()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Awake = false);

        Assert.Equal(GearAvailability.Asleep, Assert.Single(_registry.Devices).Availability);
        _time.Advance(TimeSpan.FromSeconds(60));
        await _registry.TickAsync();
        Assert.Equal(0, device.PowerCalls);
        Assert.Equal(2, device.AwakeCalls);
    }

    [Fact]
    public async Task Registry_UnchangedAsleepDevice_RaisesNoChangedWhenRatesAreRebuiltEachCall()
    {
        await PlugInAsync(configure: d => d.Awake = false);
        int changes = 0;
        _registry.Changed += () => changes++;

        for (int i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(60));
            await _registry.TickAsync();
        }

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Registry_Asleep_ChecksStatusOnlyEvery60Seconds()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Awake = false);

        _time.Advance(TimeSpan.FromSeconds(30));
        await _registry.TickAsync();

        Assert.Equal(1, device.AwakeCalls);
    }

    [Fact]
    public async Task Registry_WakesUp_ReadsPowerImmediately()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Awake = false);
        device.Awake = true;

        _time.Advance(TimeSpan.FromSeconds(60));
        await _registry.TickAsync();

        Assert.Equal(1, device.PowerCalls);
        Assert.Equal(GearAvailability.Awake, Assert.Single(_registry.Devices).Availability);
    }

    [Fact]
    public async Task Registry_TransportThrows_RemovesDeviceWithoutCrash()
    {
        FakeDevice device = await PlugInAsync();
        device.Failure = new IOException("boom");
        _time.Advance(TimeSpan.FromMinutes(5));

        await _registry.TickAsync();

        Assert.Equal(GearAvailability.Error, Assert.Single(_registry.Devices).Availability);
        Assert.Single(_logger.Errors);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _registry.TickAsync();
        Assert.Single(_logger.Errors);

        _transport.Paths.Clear();
        await _registry.TickAsync();
        Assert.Empty(_registry.Devices);
        Assert.True(device.Disposed);
    }

    [Fact]
    public async Task Registry_NoAccess_ReportsNoAccessAndLogsOnce()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Failure = new HidAccessException("denied"));

        Assert.Equal(GearAvailability.NoAccess, Assert.Single(_registry.Devices).Availability);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _registry.TickAsync();
        Assert.Single(_logger.Warnings);
        Assert.Empty(_logger.Errors);
        Assert.Equal(1, device.IdentityCalls);
        Assert.Equal(1, device.AwakeCalls);
    }

    [Fact]
    public async Task Registry_Unplugged_DeviceDisappears()
    {
        FakeDevice device = await PlugInAsync();
        int changes = 0;
        _registry.Changed += () => changes++;

        _transport.Paths.Clear();
        await _registry.TickAsync();

        Assert.Empty(_registry.Devices);
        Assert.True(device.Disposed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Registry_SamePath_KeepsInstanceAndNeverMatchesAgain()
    {
        FakeDevice first = await PlugInAsync();
        _provider.Devices["p1"] = new FakeDevice();

        _time.Advance(TimeSpan.FromMinutes(5));
        await _registry.TickAsync();

        Assert.Equal(1, _provider.MatchCalls);
        Assert.False(first.Disposed);
        Assert.Equal(2, first.PowerCalls);
    }

    [Fact]
    public async Task Registry_ChangedHandlerThrows_LoopSurvives()
    {
        _registry.Changed += () => throw new InvalidOperationException("handler");
        int second = 0;
        _registry.Changed += () => second++;

        await PlugInAsync();

        Assert.Equal(1, second);
        Assert.Single(_logger.Errors);
    }

    [Fact]
    public async Task Find_NullKey_ReturnsFirst()
    {
        await PlugInAsync("a");
        await PlugInAsync("b");

        Assert.Equal("key-a", _registry.Find(null)?.Key);
        Assert.Equal("key-a", _registry.Find("")?.Key);
        Assert.Equal("key-b", _registry.Find("key-b")?.Key);
        Assert.Null(_registry.Find("nope"));
    }

    [Fact]
    public async Task Find_NullKeyDevice_IsOnlyFoundAsFirst()
    {
        await PlugInAsync(configure: d => { d.Key = null; d.Awake = false; });

        Assert.NotNull(_registry.Find(null));
        Assert.Null(_registry.Find("anything"));
        Assert.Null(Assert.Single(_registry.Devices).Key);
    }

    [Fact]
    public async Task CyclePollingRate_WrapsWithinAllowed()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Rate = 4000);

        Assert.True(await _registry.CyclePollingRateAsync(null));
        Assert.Equal(8000, device.Rate);
        Assert.Equal(8000, _registry.Devices[0].PollingRate);

        Assert.True(await _registry.CyclePollingRateAsync("key-p1"));
        Assert.Equal(500, device.Rate);
    }

    [Fact]
    public async Task CyclePollingRate_UnknownCurrentRate_WritesNothing()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.RateUnknown = true);

        Assert.False(await _registry.CyclePollingRateAsync(null));

        Assert.Equal(0, device.WriteCalls);
        Assert.Equal(GearAvailability.Awake, _registry.Devices[0].Availability);
    }

    [Fact]
    public async Task SetPollingRate_RejectsNotAllowedAndMissing()
    {
        FakeDevice device = await PlugInAsync();

        Assert.False(await _registry.SetPollingRateAsync(null, 123));
        Assert.False(await _registry.SetPollingRateAsync("nope", 500));
        Assert.True(await _registry.SetPollingRateAsync(null, 500));
        Assert.Equal(500, device.Rate);
        Assert.Equal(500, _registry.Devices[0].PollingRate);
    }

    [Fact]
    public async Task WriteCommands_NotCapable_ReturnFalse()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.Capabilities = GearCapabilities.Battery);

        Assert.False(await _registry.SetPollingRateAsync(null, 500));
        Assert.False(await _registry.CyclePollingRateAsync(null));
        Assert.False(await _registry.SetDpiAsync(null, 800));
        Assert.False(await _registry.StepDpiAsync(null, 1));
        Assert.Equal(0, device.WriteCalls);
    }

    [Fact]
    public async Task WriteCommand_Throws_ReturnsFalseAndMarksError()
    {
        FakeDevice device = await PlugInAsync();
        device.WriteFailure = new IOException("write");

        Assert.False(await _registry.SetDpiAsync(null, 800));

        Assert.Equal(GearAvailability.Error, _registry.Devices[0].Availability);
    }

    [Fact]
    public async Task WriteCommand_ThrowsThenSucceeds_ReturnsToAwake()
    {
        FakeDevice device = await PlugInAsync();
        device.WriteFailure = new IOException("write");
        Assert.False(await _registry.SetDpiAsync(null, 800));
        device.WriteFailure = null;
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(await _registry.SetDpiAsync(null, 1600));

        GearDeviceState state = _registry.Devices[0];
        Assert.Equal(GearAvailability.Awake, state.Availability);
        Assert.Equal(_time.GetUtcNow(), state.LastContact);
    }

    [Fact]
    public async Task WriteCommand_Throws_DevicePolledAgainSoon()
    {
        FakeDevice device = await PlugInAsync();
        device.WriteFailure = new IOException("write");
        Assert.False(await _registry.SetDpiAsync(null, 800));
        int before = device.AwakeCalls;

        _time.Advance(GearDeviceRegistry.AsleepInterval);
        await _registry.TickAsync();

        Assert.Equal(before + 1, device.AwakeCalls);
        Assert.Equal(GearAvailability.Awake, _registry.Devices[0].Availability);
    }

    [Fact]
    public async Task StepDpi_WrapsAndRefreshesState()
    {
        FakeDevice device = await PlugInAsync(configure: d => d.ActiveStage = 2);

        Assert.True(await _registry.StepDpiAsync(null, 1));
        Assert.Equal(0, device.ActiveStage);
        Assert.Equal(0, _registry.Devices[0].Dpi?.ActiveStage);

        Assert.True(await _registry.StepDpiAsync(null, -1));
        Assert.Equal(2, device.ActiveStage);
        Assert.False(await _registry.StepDpiAsync(null, 0));
    }

    [Fact]
    public async Task SetDpi_WritesValueAndRefreshesState()
    {
        FakeDevice device = await PlugInAsync();

        Assert.True(await _registry.SetDpiAsync(null, 1600));

        Assert.Equal(1600, device.Stages[device.ActiveStage]);
        Assert.Equal(1600, _registry.Devices[0].Dpi?.Stages[1]);
    }

    [Fact]
    public async Task Dispose_DisposesDevicesAndRejectsCommands()
    {
        FakeDevice device = await PlugInAsync();

        _registry.Dispose();

        Assert.True(device.Disposed);
        Assert.False(await _registry.SetDpiAsync(null, 800));
    }

    [Fact]
    public async Task Start_RunsScansOnTheClock()
    {
        _transport.Paths.Add("p1");
        FakeDevice device = new() { Key = "k" };
        _provider.Devices["p1"] = device;

        _registry.Start();
        await WaitUntilAsync(() => _registry.Devices.Count == 1);

        FakeDevice late = new() { Key = "late" };
        _transport.Paths.Add("p2");
        _provider.Devices["p2"] = late;
        await WaitUntilAsync(() =>
        {
            _time.Advance(TimeSpan.FromSeconds(5));
            return _registry.Devices.Count == 2;
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class FakeTransport : IHidTransport
    {
        public List<string> Paths { get; } = [];

        public IReadOnlyList<HidDeviceInfo> Enumerate()
        {
            return Paths.Select(p => new HidDeviceInfo(p, 1, 2, 0, 0xFF02, 2, 17, 17, null, "Fake", "c-" + p)).ToList();
        }

        public IHidConnection Open(HidDeviceInfo device) => throw new NotSupportedException("The registry must not open devices itself.");
    }

    private sealed class FakeProvider : IGearProvider
    {
        public Dictionary<string, FakeDevice> Devices { get; } = [];

        public int MatchCalls;

        public IReadOnlyList<IGearDevice> Match(IReadOnlyList<HidDeviceInfo> devices, IHidTransport transport)
        {
            MatchCalls++;
            return devices.Where(d => Devices.ContainsKey(d.Path)).Select(d => (IGearDevice)Devices[d.Path]).ToList();
        }
    }

    private sealed class FakeLogger : IPluginLogger
    {
        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }

    private sealed class FakeDevice : IGearDevice
    {
        public int IdentityCalls;
        public int AwakeCalls;
        public int PowerCalls;
        public int WriteCalls;
        public bool Disposed;
        public bool Awake = true;
        public Exception? Failure;
        public Exception? WriteFailure;
        public int Rate = 1000;
        public bool RateUnknown;
        public int ActiveStage = 1;
        public int[] Stages = [400, 800, 1600];

        public string ContainerKey => "c";

        public string? Key { get; set; }

        public string Name => "Fake mouse";

        public GearLink Link => GearLink.Wired;

        public GearCapabilities Capabilities { get; set; } = GearCapabilities.Battery | GearCapabilities.PollingRate | GearCapabilities.Dpi;

        public bool Tested => true;

        // A new list with the same contents on every call, like a provider that builds it on demand.
        public IReadOnlyList<int> AllowedPollingRates => [500, 1000, 2000, 4000, 8000];

        public Task<bool> RefreshIdentityAsync(CancellationToken ct)
        {
            IdentityCalls++;
            Check();
            return Task.FromResult(Key != null);
        }

        public Task<bool> IsAwakeAsync(CancellationToken ct)
        {
            AwakeCalls++;
            Check();
            return Task.FromResult(Awake);
        }

        public Task<GearPowerReading?> ReadPowerAsync(CancellationToken ct)
        {
            PowerCalls++;
            Check();
            return Task.FromResult<GearPowerReading?>(new GearPowerReading(80, false, 4000));
        }

        public Task<int?> ReadPollingRateAsync(CancellationToken ct) => Task.FromResult<int?>(RateUnknown ? null : Rate);

        public Task SetPollingRateAsync(int hz, CancellationToken ct)
        {
            Write();
            Rate = hz;
            return Task.CompletedTask;
        }

        public Task<GearDpiState?> ReadDpiAsync(CancellationToken ct) => Task.FromResult<GearDpiState?>(new GearDpiState([.. Stages], ActiveStage));

        public Task SetDpiStageAsync(int stage, CancellationToken ct)
        {
            Write();
            ActiveStage = stage;
            return Task.CompletedTask;
        }

        public Task SetDpiValueAsync(int dpi, CancellationToken ct)
        {
            Write();
            Stages[ActiveStage] = dpi;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        private void Check()
        {
            if (Failure != null)
            {
                throw Failure;
            }
        }

        private void Write()
        {
            WriteCalls++;
            if (WriteFailure != null)
            {
                throw WriteFailure;
            }
        }
    }
}
