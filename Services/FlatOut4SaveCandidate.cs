namespace FlatOut4SaveEditor.Services;

public sealed record FlatOut4SaveCandidate(
    string Path,
    string Source,
    string? AppId,
    string? SteamUserId,
    uint Version,
    long Size,
    DateTime LastWriteTimeUtc,
    bool IsPrimary,
    int Priority)
{
    public DateTime LastWriteTimeLocal => LastWriteTimeUtc.ToLocalTime();

    public string Title => SteamUserId is null
        ? Source
        : $"{Source} - Steam user {SteamUserId}";

    public string Details => $"Version {Version} | {Size:N0} bytes | Modified {LastWriteTimeLocal:g}";
}
