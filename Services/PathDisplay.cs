namespace FlatOut4SaveEditor.Services;

// Save paths (Steam userdata, deep temp folders) read better as "C:\…\remote\Flatout VR";
// the full path goes in a tooltip.
public static class PathDisplay
{
    public static string CompactFolder(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            return path;
        }

        string root = Path.GetPathRoot(folder) ?? string.Empty;
        string[] parts = folder[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 3
            ? folder
            : $"{root}…{Path.DirectorySeparatorChar}{string.Join(Path.DirectorySeparatorChar, parts[^2..])}";
    }
}
