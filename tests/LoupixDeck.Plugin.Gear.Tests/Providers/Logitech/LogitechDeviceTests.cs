using LoupixDeck.Plugin.Gear.Devices;
using LoupixDeck.Plugin.Gear.Hid;
using LoupixDeck.Plugin.Gear.Providers.Logitech;

namespace LoupixDeck.Plugin.Gear.Tests.Providers.Logitech;

public sealed class LogitechDeviceTests
{
    private const byte Wired = HidppProtocol.DirectDeviceIndex;
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static FakeHidppDevice GamingMouse()
    {
        return FakeHidppDevice.Mouse()
            .WithUnifiedBattery(80, 8, 0)
            .WithExtendedRate(0b0100_1111, code: 3)
            .WithDpi(800);
    }

    // Provider

    [Fact]
    public void Provider_ReceiverYieldsSixSlotDevices()
    {
        FakeHidppBus bus = new(receiver: true);

        IReadOnlyList<IGearDevice> devices = new LogitechProvider().Match([bus.ShortInfo], bus.Transport);

        Assert.Equal(6, devices.Count);
        Assert.All(devices, d => Assert.IsType<LogitechDevice>(d));
        Assert.All(devices, d => d.Dispose());
    }

    [Fact]
    public void Provider_DirectDeviceYieldsOneDevice()
    {
        FakeHidppBus bus = new(receiver: false);

        IReadOnlyList<IGearDevice> devices = new LogitechProvider().Match([bus.ShortInfo], bus.Transport);

        Assert.Single(devices);
        devices[0].Dispose();
    }

    [Fact]
    public void Provider_IgnoresLongCollectionsAndForeignDevices()
    {
        FakeHidppBus bus = new(receiver: false);
        HidDeviceInfo otherVendor = bus.ShortInfo with { VendorId = 0x1234 };
        HidDeviceInfo otherPage = bus.ShortInfo with { UsagePage = 0x0001 };

        Assert.Empty(new LogitechProvider().Match([bus.LongInfo, otherVendor, otherPage], bus.Transport));
    }

    // Identity

    [Fact]
    public async Task Receiver_Slot1MouseIsIdentified()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Plug(1, GamingMouse()).Plug(2, FakeHidppDevice.Keyboard());
        for (byte slot = 3; slot <= 6; slot++)
        {
            bus.Empty(slot);
        }

        using LogitechDevice mouse = bus.Device(1);

        Assert.True(await mouse.RefreshIdentityAsync(Ct));
        Assert.Equal("logitech:model:4082:a1b2c3d4", mouse.Key);
        Assert.Equal("Logitech G502 TEST", mouse.Name);
        Assert.Equal("G502 TEST", mouse.ShortName);
        Assert.Equal(GearCapabilities.Battery | GearCapabilities.PollingRate | GearCapabilities.Dpi, mouse.Capabilities);
        Assert.Equal([125, 250, 500, 1000, 8000], mouse.AllowedPollingRates);
        Assert.Equal(GearLink.Wireless8K, mouse.Link);
        Assert.False(mouse.Tested);
        Assert.True(mouse.IsPresent);
    }

    [Fact]
    public async Task Receiver_KeyboardIsHidden()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Plug(2, FakeHidppDevice.Keyboard());
        using LogitechDevice keyboard = bus.Device(2);

        Assert.False(await keyboard.RefreshIdentityAsync(Ct));

        Assert.False(keyboard.IsPresent);
        Assert.Null(keyboard.Key);
    }

    [Fact]
    public async Task Receiver_EmptySlotIsHiddenAndNotAwake()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Empty(3);
        using LogitechDevice slot = bus.Device(3);

        Assert.False(await slot.RefreshIdentityAsync(Ct));
        Assert.False(await slot.IsAwakeAsync(Ct));

        Assert.False(slot.IsPresent);
    }

    [Fact]
    public async Task EmptySlot_AppearsOncePaired()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Empty(3);
        using LogitechDevice slot = bus.Device(3);
        await slot.IsAwakeAsync(Ct);
        Assert.False(slot.IsPresent);

        bus.Plug(3, GamingMouse());

        Assert.True(await slot.RefreshIdentityAsync(Ct));
        Assert.True(slot.IsPresent);
        Assert.NotNull(slot.Key);
    }

    [Fact]
    public async Task Hidpp10Device_IsHidden()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.V10Only(1);
        using LogitechDevice device = bus.Device(1);

        Assert.False(await device.IsAwakeAsync(Ct));

        Assert.False(device.IsPresent);
    }

    [Fact]
    public async Task DirectWiredMouse_HasTheSameKeyAndWiredLink()
    {
        FakeHidppBus receiver = new(receiver: true);
        receiver.Plug(1, GamingMouse());
        FakeHidppBus cable = new(receiver: false);
        cable.Plug(Wired, GamingMouse());
        using LogitechDevice viaReceiver = receiver.Device(1);
        using LogitechDevice direct = cable.Device(Wired);

        await viaReceiver.RefreshIdentityAsync(Ct);
        await direct.RefreshIdentityAsync(Ct);

        Assert.Equal(viaReceiver.Key, direct.Key);
        Assert.Equal(GearLink.Wired, direct.Link);
    }

    [Fact]
    public void BeforeTheIdentityIsRead_NothingIsKnown()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, GamingMouse());
        using LogitechDevice mouse = bus.Device(Wired);

        Assert.Null(mouse.Key);
        Assert.Equal("Logitech mouse", mouse.Name);
        Assert.Equal(GearCapabilities.None, mouse.Capabilities);
        Assert.Equal(GearLink.Unknown, mouse.Link);
    }

    [Fact]
    public async Task OfflineSlot_IsShownAsAsleepAndWakesUp()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Offline(1);
        using LogitechDevice mouse = bus.Device(1);

        Assert.False(await mouse.RefreshIdentityAsync(Ct));
        Assert.False(await mouse.IsAwakeAsync(Ct));
        Assert.True(mouse.IsPresent);

        bus.Plug(1, GamingMouse());

        Assert.True(await mouse.IsAwakeAsync(Ct));
        Assert.True(await mouse.RefreshIdentityAsync(Ct));
        Assert.NotNull(mouse.Key);
    }

    [Fact]
    public async Task SilentDevice_IsAsleep()
    {
        FakeHidppBus bus = new(receiver: true);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(1, hardware);
        using LogitechDevice mouse = bus.Device(1);
        await mouse.RefreshIdentityAsync(Ct);

        hardware.Silent = true;
        Assert.False(await mouse.IsAwakeAsync(Ct));

        hardware.Silent = false;
        Assert.True(await mouse.IsAwakeAsync(Ct));
        Assert.True(mouse.IsPresent);
    }

    [Fact]
    public async Task FeatureIndexes_AreResolvedOncePerContact_AndAgainAfterAsleep()
    {
        FakeHidppBus bus = new(receiver: true);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(1, hardware);
        using LogitechDevice mouse = bus.Device(1);
        await mouse.RefreshIdentityAsync(Ct);
        int afterIdentity = hardware.RootLookups;

        await mouse.ReadPowerAsync(Ct);
        await mouse.ReadPollingRateAsync(Ct);
        await mouse.ReadDpiAsync(Ct);
        Assert.Equal(afterIdentity, hardware.RootLookups);

        hardware.Silent = true;
        await mouse.IsAwakeAsync(Ct);
        hardware.Silent = false;
        await mouse.IsAwakeAsync(Ct);
        await mouse.ReadPowerAsync(Ct);

        Assert.True(hardware.RootLookups > afterIdentity);
    }

    [Fact]
    public async Task DifferentMousePairedIntoTheSlot_ChangesTheKeyAfterBeingOffline()
    {
        FakeHidppBus bus = new(receiver: true);
        bus.Plug(1, GamingMouse());
        using LogitechDevice slot = bus.Device(1);
        await slot.RefreshIdentityAsync(Ct);
        string? first = slot.Key;

        bus.Offline(1);
        await slot.IsAwakeAsync(Ct);
        bus.Plug(1, FakeHidppDevice.Mouse("M720", [9, 9, 9, 9], 0x405E).WithLegacyBattery(50, 0));
        await slot.IsAwakeAsync(Ct);

        Assert.NotEqual(first, slot.Key);
        Assert.Equal("logitech:model:405e:09090909", slot.Key);
        Assert.Equal(GearCapabilities.Battery, slot.Capabilities);
    }

    // Battery

    [Fact]
    public async Task Battery_UnifiedPercentage()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithUnifiedBattery(64, 4, 1));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(new GearPowerReading(64, true, null), await mouse.ReadPowerAsync(Ct));
    }

    [Fact]
    public async Task Battery_UnifiedLevelWhenNoPercentage()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithUnifiedBattery(0, 2, 0, reportsPercentage: false));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(new GearPowerReading(20, false, null), await mouse.ReadPowerAsync(Ct));
    }

    [Fact]
    public async Task Battery_Voltage()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithVoltageBattery(3800, 0));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(new GearPowerReading(45, false, 3800), await mouse.ReadPowerAsync(Ct));
    }

    [Fact]
    public async Task Battery_VoltageChargingIsNotFiltered()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithVoltageBattery(4000, 0x80));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(new GearPowerReading(78, true, 4000), await mouse.ReadPowerAsync(Ct));
    }

    [Fact]
    public async Task Battery_Legacy()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithLegacyBattery(55, 0));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(new GearPowerReading(55, false, null), await mouse.ReadPowerAsync(Ct));
    }

    [Fact]
    public async Task Battery_PrefersUnifiedOverVoltageOverLegacy()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = FakeHidppDevice.Mouse().WithLegacyBattery(10, 0).WithVoltageBattery(3800, 0).WithUnifiedBattery(90, 8, 0);
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal(90, (await mouse.ReadPowerAsync(Ct))?.Percent);
        Assert.DoesNotContain(hardware.Calls, c => c.Feature is 0x1000 or 0x1001);
    }

    [Fact]
    public async Task NoBatteryFeature_HasNoBatteryCapability()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithExtendedRate(0b0000_1111, 3));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.False(mouse.Capabilities.HasFlag(GearCapabilities.Battery));
        Assert.Null(await mouse.ReadPowerAsync(Ct));
    }

    // Polling rate

    [Fact]
    public async Task ExtendedRate_ReadsWithWiredAndWirelessConnectionType()
    {
        FakeHidppBus cable = new(receiver: false);
        FakeHidppDevice wiredHardware = GamingMouse();
        cable.Plug(Wired, wiredHardware);
        using LogitechDevice direct = cable.Device(Wired);
        await direct.RefreshIdentityAsync(Ct);

        FakeHidppBus receiver = new(receiver: true);
        FakeHidppDevice wirelessHardware = GamingMouse();
        receiver.Plug(1, wirelessHardware);
        using LogitechDevice wireless = receiver.Device(1);
        await wireless.RefreshIdentityAsync(Ct);

        Assert.Equal(1000, await direct.ReadPollingRateAsync(Ct));
        Assert.Equal(1000, await wireless.ReadPollingRateAsync(Ct));
        Assert.Equal(0, wiredHardware.Calls.Last(c => c.Function == 2 && c.Feature == 0x8061).Parameters[0]);
        Assert.Equal(1, wirelessHardware.Calls.Last(c => c.Function == 2 && c.Feature == 0x8061).Parameters[0]);
    }

    [Fact]
    public async Task ExtendedRate_SetWritesTheCodeAndReadsBack()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await mouse.SetPollingRateAsync(8000, Ct);

        Assert.Equal(8000, await mouse.ReadPollingRateAsync(Ct));
        Assert.Equal(6, hardware.Calls.Single(c => c.Function == 3 && c.Feature == 0x8061).Parameters[0]);
    }

    [Fact]
    public async Task LegacyRate_ReadsAndSetsMilliseconds()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = FakeHidppDevice.Mouse().WithLegacyRate(0b1000_1011, ms: 2);
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal([125, 250, 500, 1000], mouse.AllowedPollingRates);
        Assert.Equal(500, await mouse.ReadPollingRateAsync(Ct));

        await mouse.SetPollingRateAsync(1000, Ct);

        Assert.Equal(1000, await mouse.ReadPollingRateAsync(Ct));
        Assert.Equal(1, hardware.Calls.Single(c => c.Function == 2 && c.Feature == 0x8060).Parameters[0]);
    }

    // A G-series mouse with OnboardProfiles: in onboard mode (1) it refuses rate writes with InvalidArgument, as the G502 does.
    private static FakeHidppDevice OnboardMouse(byte mode, List<byte> modeWrites)
    {
        byte current = 1;
        FakeHidppDevice device = FakeHidppDevice.Mouse();
        device.On(0x8100, 2, _ => FakeHidppDevice.Pad(mode));
        device.On(0x8100, 1, p =>
        {
            modeWrites.Add(p[0]);
            mode = p[0];
            return FakeHidppDevice.Pad();
        });
        device.On(0x8060, 0, _ => FakeHidppDevice.Pad(0b1000_1011));
        device.On(0x8060, 1, _ => FakeHidppDevice.Pad(current));
        return device.On(0x8060, 2, p =>
        {
            if (mode != 2)
            {
                throw new FakeHidppError(0x02);
            }

            current = p[0];
            return FakeHidppDevice.Pad();
        });
    }

    [Fact]
    public async Task SetRate_InOnboardMode_SwitchesToHostModeFirst()
    {
        List<byte> modeWrites = [];
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, OnboardMouse(mode: 1, modeWrites));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await mouse.SetPollingRateAsync(500, Ct);

        Assert.Equal([2], modeWrites);
        Assert.Equal(500, await mouse.ReadPollingRateAsync(Ct));
    }

    [Fact]
    public async Task SetRate_InHostMode_DoesNotSwitch()
    {
        List<byte> modeWrites = [];
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, OnboardMouse(mode: 2, modeWrites));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await mouse.SetPollingRateAsync(250, Ct);

        Assert.Empty(modeWrites);
    }

    [Fact]
    public async Task Reads_NeverSwitchTheMode()
    {
        List<byte> modeWrites = [];
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, OnboardMouse(mode: 1, modeWrites));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);
        await mouse.IsAwakeAsync(Ct);
        await mouse.ReadPollingRateAsync(Ct);
        await mouse.ReadDpiAsync(Ct);

        Assert.Empty(modeWrites);
    }

    [Fact]
    public async Task ExtendedRate_PreferredOverLegacy()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithLegacyRate(0b0000_0001, 1).WithExtendedRate(0b0100_1000, 3));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        Assert.Equal([1000, 8000], mouse.AllowedPollingRates);
    }

    [Fact]
    public async Task SetRate_NotAllowed_Throws()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, GamingMouse());
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mouse.SetPollingRateAsync(2000, Ct));
    }

    [Fact]
    public async Task SetRate_ReadBackDiffers_ThrowsTimeout()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithExtendedRate(0b0100_1111, 3, ignoredSetKeepsCode: 3));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await Assert.ThrowsAsync<TimeoutException>(() => mouse.SetPollingRateAsync(500, Ct));
    }

    [Fact]
    public async Task SetRate_DeviceRejects_ThrowsArgumentException()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = FakeHidppDevice.Mouse();
        hardware.On(0x8061, 1, _ => FakeHidppDevice.Pad(0, 0b0000_1111));
        hardware.On(0x8061, 2, _ => FakeHidppDevice.Pad(3));
        hardware.On(0x8061, 3, _ => throw new FakeHidppError(0x02));
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        ArgumentException ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => mouse.SetPollingRateAsync(500, Ct));

        Assert.IsType<HidppException>(ex.InnerException);
    }

    // DPI

    [Fact]
    public async Task Dpi_ReadBuildsTheLadderWithTheCurrentValue()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, GamingMouse());
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        GearDpiState? state = await mouse.ReadDpiAsync(Ct);

        Assert.NotNull(state);
        Assert.Equal([400, 800, 1600, 3200, 6400, 12800, 25600], state.Value.Stages);
        Assert.Equal(1, state.Value.ActiveStage);
    }

    [Fact]
    public async Task Dpi_CurrentOffTheLadderIsInsertedInOrder()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithDpi(1000));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        GearDpiState? state = await mouse.ReadDpiAsync(Ct);

        Assert.Equal([400, 800, 1000, 1600, 3200, 6400, 12800, 25600], state!.Value.Stages);
        Assert.Equal(2, state.Value.ActiveStage);
    }

    [Fact]
    public async Task Dpi_SetStageWritesTheLadderValue()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await mouse.SetDpiStageAsync(2, Ct);

        GearDpiState after = (await mouse.ReadDpiAsync(Ct))!.Value;
        Assert.Equal(1600, after.Stages[after.ActiveStage]);
        byte[] write = hardware.Calls.Single(c => c.Function == 3 && c.Feature == 0x2201).Parameters;
        Assert.Equal([0, 0x06, 0x40], write[..3]);
    }

    [Fact]
    public async Task Dpi_SetStageOutOfRange_Throws()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, GamingMouse());
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mouse.SetDpiStageAsync(7, Ct));
    }

    [Fact]
    public async Task Dpi_SetValueSnapsToTheNearestSupportedValue()
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await mouse.SetDpiValueAsync(1234, Ct); // 1250 is the nearest multiple of 50

        byte[] write = hardware.Calls.Single(c => c.Function == 3 && c.Feature == 0x2201).Parameters;
        Assert.Equal(1250, (write[1] << 8) | write[2]);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(30000)]
    public async Task Dpi_SetValueOutsideTheSensorRange_Throws(int dpi)
    {
        FakeHidppBus bus = new(receiver: false);
        FakeHidppDevice hardware = GamingMouse();
        bus.Plug(Wired, hardware);
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mouse.SetDpiValueAsync(dpi, Ct));

        Assert.DoesNotContain(hardware.Calls, c => c.Function == 3 && c.Feature == 0x2201);
    }

    [Fact]
    public async Task Dpi_DeviceRejectsTheWrite_ThrowsArgumentException()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, FakeHidppDevice.Mouse().WithDpi(800, rejectSet: true));
        using LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => mouse.SetDpiValueAsync(1600, Ct));
    }

    // Lifetime

    [Fact]
    public async Task Dispose_ThenCommandsThrow_AndClosesTheLink()
    {
        FakeHidppBus bus = new(receiver: false);
        bus.Plug(Wired, GamingMouse());
        LogitechDevice mouse = bus.Device(Wired);
        await mouse.RefreshIdentityAsync(Ct);

        mouse.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => mouse.IsAwakeAsync(Ct));
        Assert.Equal(1, bus.Short.DisposeCount);
        Assert.Equal(1, bus.Long.DisposeCount);
    }
}
