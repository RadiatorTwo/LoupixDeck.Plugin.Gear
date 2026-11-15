namespace LoupixDeck.Plugin.Gear.Hid;

/// <summary>The HID device exists but the process is not allowed to open it.</summary>
internal sealed class HidAccessException : IOException
{
    public HidAccessException(string message)
        : base(message)
    {
    }

    public HidAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
