namespace FlatOut4SaveEditor.Services;

public sealed class FlatOut4SaveDocument
{
    public FlatOut4SaveDocument(string? path, byte[] bytes, uint originalVersion, bool migrated, string warning)
    {
        Path = path;
        Bytes = bytes;
        OriginalVersion = originalVersion;
        Migrated = migrated;
        Warning = warning;
    }

    public string? Path { get; set; }

    public byte[] Bytes { get; }

    public uint OriginalVersion { get; }

    public bool Migrated { get; }

    public string Warning { get; }
}
