namespace FlatOut4SaveEditor.Services;

public sealed class FlatOut4SaveDocument
{
    public FlatOut4SaveDocument(string? path, byte[] bytes, uint originalVersion, bool migrated, string warning, FlatOut4SaveRecoveryReport? recovery = null)
    {
        Path = path;
        Bytes = bytes;
        OriginalVersion = originalVersion;
        Migrated = migrated;
        Warning = warning;
        Recovery = recovery;
    }

    public string? Path { get; set; }

    // Always the V95 layout the editor works in, whatever the file on disk looks like.
    public byte[] Bytes { get; }

    public uint OriginalVersion { get; }

    // Saving writes something different from the file even before any edit (converted or repaired).
    public bool Migrated { get; }

    public string Warning { get; }

    // Set when the file did not match a known layout exactly and had to be recovered.
    public FlatOut4SaveRecoveryReport? Recovery { get; }
}

// How a file that did not match a known layout was read, and how it will be written back.
public sealed record FlatOut4SaveRecoveryReport(
    string Title,
    string Summary,
    IReadOnlyList<string> Notes,
    FlatOut4SaveLayoutMap? Map = null,
    byte[]? OriginalFile = null,
    int CheckedValues = 0,
    int ImplausibleValues = 0)
{
    // A newer layout: saving puts the edited values back into the original bytes and keeps the
    // file's version, so the build that wrote it can still read everything it stored.
    public bool PreservesLayout => Map is not null && OriginalFile is not null;
}
