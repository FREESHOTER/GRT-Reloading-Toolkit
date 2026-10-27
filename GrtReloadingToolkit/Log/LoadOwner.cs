namespace GrtReloadingToolkit.Log;

/// <summary>
/// Whose load a .grtload is, read from where it sits. The shooter who reloads for a club keeps each person's loads in a folder of their
/// own inside GRT's ("...\GRT-2021.2030 (W11) V1.0\Mario\N160\load.grtload": person, then powder), so the path says whose it is.
/// </summary>
public static class LoadOwner
{
    /// <summary>
    /// The person a load belongs to, or null when the path does not say. A folder named like one of the toolkit's shooters wins; failing that,
    /// the folder right under GRT's own (a folder starting with "GRT") counts when at least one more folder lies between it and the file --
    /// with only one, that folder is the powder ("N150\load.grtload"), not a person.
    /// </summary>
    public static string? FromPath(string? file, IEnumerable<string>? knownShooters = null)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        var parts = file.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var folders = parts[..^1];

        if (knownShooters != null)
            foreach (string name in knownShooters)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string? hit = folders.FirstOrDefault(f => string.Equals(f.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit != null) return name.Trim();
            }

        int root = Array.FindLastIndex(folders, f => f.StartsWith("GRT", StringComparison.OrdinalIgnoreCase));
        // folders after the root: [person, powder, ...] -> person is only that when something follows it
        if (root >= 0 && folders.Length - root - 1 >= 2) return folders[root + 1];
        return null;
    }
}
