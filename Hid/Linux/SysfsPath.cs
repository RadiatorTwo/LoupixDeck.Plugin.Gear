namespace LoupixDeck.Plugin.Gear.Hid.Linux;

/// <summary>Resolves symbolic links in every path segment, as <c>realpath(3)</c> does.</summary>
internal static class SysfsPath
{
    private const int MaxLinks = 40;

    /// <param name="path">An absolute path.</param>
    /// <param name="readLink">Returns the raw target of a symbolic link, or null if the path is not a link.</param>
    public static string Canonicalize(string path, Func<string, string?> readLink)
    {
        List<string> resolved = [];
        LinkedList<string> pending = new(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        int links = 0;

        while (pending.First is { } node)
        {
            string segment = node.Value;
            pending.RemoveFirst();
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            string candidate = "/" + string.Join('/', resolved.Append(segment));
            string? target = readLink(candidate);
            if (target is null)
            {
                resolved.Add(segment);
                continue;
            }

            if (++links > MaxLinks)
            {
                throw new IOException($"Too many symbolic links resolving '{path}'.");
            }

            if (target.StartsWith('/'))
            {
                resolved.Clear();
            }

            foreach (string part in target.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                pending.AddFirst(part);
            }
        }

        return "/" + string.Join('/', resolved);
    }
}
