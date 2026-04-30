namespace FlatOut4SaveEditor.Services;

public static class FlatOut4SaveFile
{
    private const int V87VrMotionOptionsSize = 28;
    private const int V87VrMotionOptionsOffset = FlatOut4SaveSchema.GameOptionsOffset + 64;

    private static readonly string[] SteamAppIds =
    [
        "3844750", // internal Project Fox / VR app id used by this repo
        "402130"  // public FlatOut 4 app id present in the legacy Steam scripts
    ];

    public static FlatOut4SaveDocument Load(string path, FlatOut4SaveSchema schema)
    {
        byte[] source = File.ReadAllBytes(path);
        if (source.Length < 8)
        {
            throw new InvalidDataException("This file is too small to be a FlatOut 4 save.");
        }

        uint head = BitConverter.ToUInt32(source, 0);
        if (head != FlatOut4SaveSchema.FooterValue)
        {
            throw new InvalidDataException("The save does not start with the FlatOut footer marker \"FOO \".");
        }

        uint version = BitConverter.ToUInt32(source, 4);
        bool migrated = false;
        string warning = string.Empty;
        byte[] bytes;

        if (source.Length == schema.SerializableSize)
        {
            bytes = source;
            if (version == 89)
            {
                WriteUInt32(bytes, 4, FlatOut4SaveSchema.CurrentVersion);
                migrated = true;
                warning = "Loaded a V89 save into the V90 layout. The app preserves byte layout and stamps version 90 on save.";
            }
        }
        else if (version == 88 && source.Length == schema.SerializableSize - 120)
        {
            bytes = MigrateV88(source, schema);
            migrated = true;
            warning = "Migrated V88 bytes to the V90 layout by adding default menu bindings.";
        }
        else if (version == 87 && source.Length == schema.SerializableSize - 120 - V87VrMotionOptionsSize)
        {
            bytes = MigrateV87(source, schema);
            migrated = true;
            warning = "Migrated V87 bytes to the V90 layout by adding default VR motion steering options and menu bindings.";
        }
        else
        {
            throw new InvalidDataException(
                $"Unsupported save size/version. File has version {version} and {source.Length:N0} bytes; this editor supports V87/V88/V89/V90 saves and expects V90 size {schema.SerializableSize:N0} bytes.");
        }

        uint tail = BitConverter.ToUInt32(bytes, schema.SerializableSize - 4);
        if (tail != FlatOut4SaveSchema.FooterValue)
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? "Tail footer marker is not valid. You can inspect values, but saving may preserve a corrupt file."
                : $"{warning} Tail footer marker is not valid.";
        }

        return new FlatOut4SaveDocument(path, bytes, version, migrated, warning);
    }

    public static void Save(FlatOut4SaveDocument document, string path, bool createBackup)
    {
        if (File.Exists(path) && createBackup)
        {
            string backupPath = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backupPath, overwrite: false);
        }

        WriteUInt32(document.Bytes, 4, FlatOut4SaveSchema.CurrentVersion);
        WriteUInt32(document.Bytes, document.Bytes.Length - 4, FlatOut4SaveSchema.FooterValue);
        File.WriteAllBytes(path, document.Bytes);
        document.Path = path;
    }

    public static IReadOnlyList<string> FindSteamCloudSaves()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in GetSteamRoots())
        {
            string steamUserData = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(steamUserData))
            {
                continue;
            }

            foreach (string userDirectory in SafeEnumerateDirectories(steamUserData))
            {
                foreach (string appId in SteamAppIds)
                {
                    string remoteDirectory = Path.Combine(userDirectory, appId, "remote");
                    if (!Directory.Exists(remoteDirectory))
                    {
                        continue;
                    }

                    string exactSave = Path.Combine(remoteDirectory, "Save");
                    if (File.Exists(exactSave))
                    {
                        candidates.Add(exactSave);
                    }

                    foreach (string file in SafeEnumerateFiles(remoteDirectory))
                    {
                        string name = Path.GetFileName(file);
                        if (name.Equals("Save", StringComparison.OrdinalIgnoreCase) || IsLikelyFlatOutSave(file))
                        {
                            candidates.Add(file);
                        }
                    }
                }
            }
        }

        foreach (string path in GetOfflineSaveCandidates())
        {
            if (File.Exists(path))
            {
                candidates.Add(path);
            }
        }

        return candidates
            .Where(IsLikelyFlatOutSave)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    public static IReadOnlyList<string> GetCheckedSaveLocations()
    {
        var locations = new List<string>();
        foreach (string steamRoot in GetSteamRoots())
        {
            foreach (string appId in SteamAppIds)
            {
                locations.Add(Path.Combine(steamRoot, "userdata", "<steamid>", appId, "remote", "Save"));
            }
        }

        locations.AddRange(GetOfflineSaveCandidates());
        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static byte[] MigrateV88(byte[] source, FlatOut4SaveSchema schema)
    {
        byte[] destination = new byte[schema.SerializableSize];
        int currentAfterGameOptions = FlatOut4SaveSchema.GameOptionsOffset + FlatOut4SaveSchema.GameOptionsSize;
        int v88AfterGameOptions = FlatOut4SaveSchema.GameOptionsOffset + FlatOut4SaveSchema.V88GameOptionsSize;
        int remaining = schema.SerializableSize - currentAfterGameOptions;

        Array.Copy(source, 0, destination, 0, FlatOut4SaveSchema.GameOptionsOffset);
        Array.Copy(source, FlatOut4SaveSchema.GameOptionsOffset, destination, FlatOut4SaveSchema.GameOptionsOffset, FlatOut4SaveSchema.V88GameOptionsSize);
        Array.Fill(destination, (byte)0xFF, FlatOut4SaveSchema.GameOptionsOffset + FlatOut4SaveSchema.V88GameOptionsSize, FlatOut4SaveSchema.GameOptionsSize - FlatOut4SaveSchema.V88GameOptionsSize);
        Array.Copy(source, v88AfterGameOptions, destination, currentAfterGameOptions, remaining);
        WriteUInt32(destination, 4, FlatOut4SaveSchema.CurrentVersion);
        return destination;
    }

    private static byte[] MigrateV87(byte[] source, FlatOut4SaveSchema schema)
    {
        byte[] v88 = new byte[schema.SerializableSize - 120];
        Array.Copy(source, 0, v88, 0, V87VrMotionOptionsOffset);

        WriteUInt32(v88, V87VrMotionOptionsOffset + 0, 1);  // m_uVRMotionSteeringEnabled = ON
        WriteUInt32(v88, V87VrMotionOptionsOffset + 4, 10); // m_uVRMotionLockToLockDeg
        WriteUInt32(v88, V87VrMotionOptionsOffset + 8, 0);  // m_uVRMotionDeadzoneDeg
        WriteUInt32(v88, V87VrMotionOptionsOffset + 12, 10); // m_uVRMotionSensitivity
        WriteUInt32(v88, V87VrMotionOptionsOffset + 16, 0); // m_uVRMotionAutoRecenterIdle = OFF
        WriteUInt32(v88, V87VrMotionOptionsOffset + 20, 1); // m_uVRMotionDominantHand = right
        WriteUInt32(v88, V87VrMotionOptionsOffset + 24, 1); // m_uVRMotionEnableHaptic = ON

        Array.Copy(
            source,
            V87VrMotionOptionsOffset,
            v88,
            V87VrMotionOptionsOffset + V87VrMotionOptionsSize,
            source.Length - V87VrMotionOptionsOffset);

        WriteUInt32(v88, 4, 88);
        return MigrateV88(v88, schema);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        byte[] valueBytes = BitConverter.GetBytes(value);
        Array.Copy(valueBytes, 0, bytes, offset, valueBytes.Length);
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDirectory(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddDirectory(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        AddDirectory(roots, TryReadRegistryString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"));
        AddDirectory(roots, TryReadRegistryString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "InstallPath"));
        AddDirectory(roots, TryReadRegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
        AddDirectory(roots, TryReadRegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"));

        return roots;
    }

    private static IEnumerable<string> GetOfflineSaveCandidates()
    {
        foreach (string directory in GetOfflineSaveDirectories())
        {
            string exactSave = Path.Combine(directory, "Save");
            yield return exactSave;

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in SafeEnumerateFiles(directory))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("Save", StringComparison.OrdinalIgnoreCase) || IsLikelyFlatOutSave(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> GetOfflineSaveDirectories()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(documents, "My Games", "FlatOut 4"),
            Path.Combine(documents, "My Games", "FlatOut4"),
            Path.Combine(localAppData, "FlatOut4"),
            Path.Combine(localAppData, "FlatOut 4")
        ];
    }

    private static string? TryReadRegistryString(string keyName, string valueName)
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(keyName, valueName, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void AddDirectory(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalized = path.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (Directory.Exists(normalized))
        {
            roots.Add(normalized);
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsLikelyFlatOutSave(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 8)
            {
                return false;
            }

            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) != header.Length)
            {
                return false;
            }

            uint footer = BitConverter.ToUInt32(header[..4]);
            uint version = BitConverter.ToUInt32(header[4..8]);
            return footer == FlatOut4SaveSchema.FooterValue && version is >= 80 and <= 100;
        }
        catch
        {
            return false;
        }
    }
}
