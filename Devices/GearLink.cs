namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>How the mouse is currently connected, as far as it affects the usable polling rates.</summary>
internal enum GearLink
{
    Unknown,
    Wired,
    Wireless1K,
    Wireless2K,
    Wireless4K,
    Wireless8K,
}

internal static class GearLinkExtensions
{
    /// <summary>Short display text such as <c>8K 2.4G</c> or <c>USB</c>; empty for <see cref="GearLink.Unknown"/>.</summary>
    public static string ToDisplayText(this GearLink link)
    {
        return link switch
        {
            GearLink.Wired => "USB",
            GearLink.Wireless1K => "1K 2.4G",
            GearLink.Wireless2K => "2K 2.4G",
            GearLink.Wireless4K => "4K 2.4G",
            GearLink.Wireless8K => "8K 2.4G",
            _ => string.Empty,
        };
    }
}
