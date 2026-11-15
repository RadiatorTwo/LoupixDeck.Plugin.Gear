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
    /// <summary>
    /// Sends one output report as a SET_REPORT request (Windows: <c>HidD_SetOutputReport</c>); the first byte is
    /// the report id. Pulsar needs this path.
    /// </summary>
    void SendOutputReport(ReadOnlySpan<byte> report);

    /// <summary>
    /// Sends one output report through the interrupt OUT pipe (Windows: <c>WriteFile</c>); the first byte is the
    /// report id. The report is padded with zeros to the collection's output length. HID++ needs this path.
    /// </summary>
    void WriteReport(ReadOnlySpan<byte> report);

    /// <summary>Reads one input report into <paramref name="buffer"/>; returns 0 on timeout.</summary>
    int Read(Span<byte> buffer, TimeSpan timeout);
}
