using System.Text.Json;

namespace FlatOut4SaveEditor.Services;

public sealed record RecentSave(string Path, DateTime OpenedUtc)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string Folder => PathDisplay.CompactFolder(Path);

    public string OpenedText
    {
        get
        {
            DateTime local = OpenedUtc.ToLocalTime();
            int days = (DateTime.Now.Date - local.Date).Days;
            return days switch
            {
                <= 0 => $"Opened today at {local:t}",
                1 => $"Opened yesterday at {local:t}",
                < 7 => $"Opened {days} days ago",
                _ => $"Opened {local:d}"
            };
        }
    }
}

// The last few saves opened or written, newest first, in %LocalAppData%\FlatOut4SaveEditor\recent.json.
public static class FlatOut4RecentFiles
{
    private const int MaxEntries = 8;

    // Unattended test runs (snapshot mode) leave the user's list alone.
    public static bool Enabled { get; set; } = true;

    // Snapshot runs read a scratch list instead (--recent-store).
    public static string? StorePathOverride { get; set; }

    private static string StorePath => StorePathOverride ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlatOut4SaveEditor", "recent.json");

    // Files that no longer exist are left out.
    public static IReadOnlyList<RecentSave> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            RecentSave[] saved = JsonSerializer.Deserialize<RecentSave[]>(File.ReadAllText(StorePath)) ?? [];
            return saved.Where(entry => !string.IsNullOrWhiteSpace(entry.Path) && File.Exists(entry.Path)).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static void Add(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        Store(Load()
            .Where(entry => !string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(new RecentSave(fullPath, DateTime.UtcNow))
            .Take(MaxEntries)
            .ToArray());
    }

    public static void Clear() => Store([]);

    private static void Store(RecentSave[] entries)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // A read-only profile folder only costs the recent list.
        }
    }
}
