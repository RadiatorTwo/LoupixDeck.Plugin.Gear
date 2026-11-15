namespace LoupixDeck.Plugin.Gear.Hid;

/// <summary>Platform access to HID devices.</summary>
internal interface IHidTransport
{
    IReadOnlyList<HidDeviceInfo> Enumerate();

    /// <exception cref="HidAccessException">The process lacks permission to open the device.</exception>
    IHidConnection Open(HidDeviceInfo device);
}

/// <summary>An open HID collection. Not thread-safe: one reader at a time.</summary>
internal interface IHidConnection : IDisposable
{
    /// <summary>Sends one output report; the first byte is the report id.</summary>
    void SendOutputReport(ReadOnlySpan<byte> report);

    /// <summary>Reads one input report into <paramref name="buffer"/>; returns 0 on timeout.</summary>
    int Read(Span<byte> buffer, TimeSpan timeout);
}
