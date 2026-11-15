namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>HID++ 2.0 feature ids the Logitech provider uses.</summary>
internal static class LogitechFeatures
{
    public const ushort Root = 0x0000;
    public const ushort DeviceInformation = 0x0003;
    public const ushort DeviceTypeAndName = 0x0005;
    public const ushort BatteryStatus = 0x1000;
    public const ushort BatteryVoltage = 0x1001;
    public const ushort UnifiedBattery = 0x1004;
    public const ushort AdjustableDpi = 0x2201;
    public const ushort ReportRate = 0x8060;
    public const ushort ExtendedReportRate = 0x8061;
    public const ushort OnboardProfiles = 0x8100;
}
