namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>Stage cycling shared by all providers.</summary>
internal static class DpiStages
{
    /// <summary>The stage after <paramref name="active"/>, wrapping from the last to the first; the first when <paramref name="active"/> is -1 (unknown).</summary>
    public static int NextStage(int active, int count) => (active + 1) % count;

    /// <summary>The stage before <paramref name="active"/>, wrapping from the first to the last; the last when <paramref name="active"/> is -1 (unknown).</summary>
    public static int PreviousStage(int active, int count) => active < 0 ? count - 1 : (active + count - 1) % count;
}
