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

    /// <summary>
    /// Returns <c>steelseries:model:172b</c>, or <c>steelseries:model:172b:&lt;sn&gt;</c> when the USB serial is not
    /// empty or all zeros (the Rival 650 reports <c>000000000000</c>).
    /// </summary>
    public static string ForSteelSeries(ushort productId, string? usbSerial)
    {
        string key = $"steelseries:model:{productId:x4}";
        return string.IsNullOrEmpty(usbSerial) || usbSerial.All(c => c == '0') ? key : $"{key}:{usbSerial.ToLowerInvariant()}";
    }

    /// <summary>Returns <c>logitech:model:4082:a1b2c3d4</c> (model id, then the unit id), both lowercase hex.</summary>
    public static string ForLogitech(ushort modelId, ReadOnlySpan<byte> unitId)
    {
        return $"logitech:model:{modelId:x4}:{Convert.ToHexStringLower(unitId)}";
    }
}
