namespace LoupixDeck.Plugin.Gear.Devices;

internal readonly record struct GearPowerReading(int Percent, bool Charging, int? Millivolts);

internal readonly record struct GearDpiState(IReadOnlyList<int> Stages, int ActiveStage);
