namespace LoupixDeck.Plugin.Gear.Providers.Logitech;

/// <summary>
/// A HID++ error reply. HID++ 2.0 errors come from the device (error feature 0xFF), HID++ 1.0 errors from the
/// receiver (sub-id 0x8F), for example when the addressed slot is empty or the mouse is asleep.
/// </summary>
internal sealed class HidppException(byte code, bool isV10)
    : Exception($"HID++ {(isV10 ? "1.0" : "2.0")} error 0x{code:x2}")
{
    // HID++ 1.0 error codes (receiver).
    private const byte V10InvalidSubId = 0x01;
    private const byte V10ConnectFail = 0x04;
    private const byte V10UnknownDevice = 0x08;
    private const byte V10ResourceError = 0x09;

    // HID++ 2.0 error codes.
    private const byte V20InvalidArgument = 0x02;
    private const byte V20OutOfRange = 0x03;
    private const byte V20Unsupported = 0x09;

    public byte Code { get; } = code;

    /// <summary>True for a HID++ 1.0 error (from the receiver), false for a HID++ 2.0 error (from the device).</summary>
    public bool IsV10 { get; } = isV10;

    /// <summary>The receiver slot is not paired with a device.</summary>
    public bool IsSlotEmpty => IsV10 && Code == V10UnknownDevice;

    /// <summary>The device is paired but did not answer (asleep or out of range).</summary>
    public bool IsOffline => IsV10 && Code is V10ResourceError or V10ConnectFail;

    /// <summary>The device only speaks HID++ 1.0 (it does not know the HID++ 2.0 sub-id).</summary>
    public bool IsV10Only => IsV10 && Code == V10InvalidSubId;

    /// <summary>The device refused the request itself: invalid argument, out of range or unsupported.</summary>
    public bool IsRejection => !IsV10 && Code is V20InvalidArgument or V20OutOfRange or V20Unsupported;
}
