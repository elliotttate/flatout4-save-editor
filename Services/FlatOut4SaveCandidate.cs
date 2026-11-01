namespace FlatOut4SaveEditor.Services;

public enum SaveLocationKind
{
    SteamCloud,
    Local,
    TestSlot,
    Other
}

public sealed record FlatOut4SaveCandidate(
    string Path,
    string Source,
    string? AppId,
    string? SteamUserId,
    uint Version,
    long Size,
    DateTime LastWriteTimeUtc,
    bool IsPrimary,
    int Priority,
    SaveLocationKind Kind = SaveLocationKind.Other,
    bool NeedsRecovery = false)
{
    public DateTime LastWriteTimeLocal => LastWriteTimeUtc.ToLocalTime();

    public string Folder => PathDisplay.CompactFolder(Path);

    public string Title => SteamUserId is null
        ? Source
        : $"{Source} - Steam user {SteamUserId}";

    public string VersionLabel => NeedsRecovery
        ? $"V{Version} (needs recovery)"
        : Version == FlatOut4SaveSchema.RcTelemetryVersion
            ? $"V{Version} (RC10-RC17)"
            : $"V{Version}";

    public string Details => $"{VersionLabel} | {Size:N0} bytes | Modified {LastWriteTimeLocal:g}";

    public string KindLabel => Kind switch
    {
        SaveLocationKind.SteamCloud => "Steam Cloud",
        SaveLocationKind.Local => "Local save",
        SaveLocationKind.TestSlot => "Developer test slot",
        _ => "Other"
    };
}
