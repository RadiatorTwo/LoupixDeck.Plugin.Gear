namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>Builds the stable per-device keys used to store settings and match pages.</summary>
internal static class DeviceKey
{
    /// <summary>Returns <c>pulsar:model:0057</c>, or <c>pulsar:model:0057:&lt;sn&gt;</c> when the mouse reports a serial.</summary>
    public static string ForPulsar(ushort modelId, string? mouseSerial)
    {
        string key = $"pulsar:model:{modelId:x4}";
        return string.IsNullOrEmpty(mouseSerial) ? key : $"{key}:{mouseSerial}";
    }
}
