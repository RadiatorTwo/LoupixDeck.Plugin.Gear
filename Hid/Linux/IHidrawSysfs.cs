namespace LoupixDeck.Plugin.Gear.Hid.Linux;

/// <summary>The slice of the file system the hidraw enumeration reads; replaceable in tests.</summary>
internal interface IHidrawSysfs
{
    /// <summary>Node names below <c>/sys/class/hidraw</c>, e.g. <c>hidraw3</c>.</summary>
    IReadOnlyList<string> ListHidrawNames();

    /// <summary>Text content of a file, or null if it does not exist or is unreadable.</summary>
    string? ReadText(string path);

    /// <summary>Binary content of a file, or null if it does not exist or is unreadable.</summary>
    byte[]? ReadBytes(string path);

    /// <summary>The path with all symbolic links resolved, or null if it does not exist.</summary>
    string? RealPath(string path);
}

/// <summary>The real <c>/sys</c> file system.</summary>
internal sealed class HidrawSysfs : IHidrawSysfs
{
    private const string ClassPath = "/sys/class/hidraw";

    public IReadOnlyList<string> ListHidrawNames()
    {
        if (!Directory.Exists(ClassPath))
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(ClassPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
    }

    public string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? RealPath(string path)
    {
        try
        {
            DirectoryInfo directory = new(path);
            FileSystemInfo? target = directory.ResolveLinkTarget(returnFinalTarget: true);
            return (target ?? directory).FullName.TrimEnd('/');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
