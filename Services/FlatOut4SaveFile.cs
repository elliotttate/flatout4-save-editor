using System.Text.RegularExpressions;

namespace FlatOut4SaveEditor.Services;

public static class FlatOut4SaveFile
{
    private const uint InvalidControlId = uint.MaxValue;
    private const uint ClearedMenuControlId = uint.MaxValue - 1;
    private const uint KeyboardMediaSelectControlId = 0xED;
    private const uint GamepadPointer2YControlId = 37;
    private const uint GenericDevicePovUpControlId = 143;

    private static readonly SaveLayout[] SaveLayouts =
    [
        new(82, FlatOut4SaveSchema.V82GameplayOptionsSize, FlatOut4SaveSchema.V82BoundActionCount, 0),
        new(83, FlatOut4SaveSchema.V83GameplayOptionsSize, FlatOut4SaveSchema.V82BoundActionCount, 0),
        new(84, FlatOut4SaveSchema.V84GameplayOptionsSize, FlatOut4SaveSchema.V82BoundActionCount, 0),
        new(85, FlatOut4SaveSchema.V85GameplayOptionsSize, FlatOut4SaveSchema.V82BoundActionCount, 0),
        new(86, FlatOut4SaveSchema.V86GameplayOptionsSize, FlatOut4SaveSchema.V90BoundActionCount, 0),
        new(87, FlatOut4SaveSchema.V86GameplayOptionsSize, FlatOut4SaveSchema.V90BoundActionCount, 0),
        new(88, FlatOut4SaveSchema.V92GameplayOptionsSize, FlatOut4SaveSchema.V90BoundActionCount, 0),
        new(89, FlatOut4SaveSchema.V92GameplayOptionsSize, FlatOut4SaveSchema.V90BoundActionCount, FlatOut4SaveSchema.V90MenuBindingCount),
        new(90, FlatOut4SaveSchema.V92GameplayOptionsSize, FlatOut4SaveSchema.V90BoundActionCount, FlatOut4SaveSchema.V90MenuBindingCount),
        new(91, FlatOut4SaveSchema.V92GameplayOptionsSize, FlatOut4SaveSchema.V91BoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount),
        new(92, FlatOut4SaveSchema.V92GameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount),
        new(93, FlatOut4SaveSchema.V93GameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount),
        new(94, FlatOut4SaveSchema.V94GameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount),
        new(95, FlatOut4SaveSchema.CurrentGameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount)
    ];

    private static readonly SteamAppInfo[] SteamApps =
    [
        new("3844750", "FlatOut 4 VR", 0, ["Flatout VR\\Save.dat", "Save"]),
        new("402130", "FlatOut 4", 10, ["Save"])
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

        if (!TryGetSaveLayout(version, source.Length, schema, out SaveLayout? layout))
        {
            throw new InvalidDataException(
                $"Unsupported save size/version. File has version {version} and {source.Length:N0} bytes; this editor supports V{FlatOut4SaveSchema.EarliestSupportedVersion}-V{FlatOut4SaveSchema.CurrentVersion} saves and expects V{FlatOut4SaveSchema.CurrentVersion} size {schema.SerializableSize:N0} bytes.");
        }

        SaveLayout saveLayout = layout!;
        if (saveLayout.Version == FlatOut4SaveSchema.CurrentVersion)
        {
            bytes = source;
        }
        else
        {
            bytes = MigrateToCurrent(source, schema, saveLayout);
            migrated = true;
            warning = $"Migrated V{version} save bytes to the V{FlatOut4SaveSchema.CurrentVersion} layout. Saving will write V{FlatOut4SaveSchema.CurrentVersion}; keep the automatic backup if you still need the original older-layout file.";
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

    public static IReadOnlyList<FlatOut4SaveCandidate> FindSaveCandidates(FlatOut4SaveSchema schema)
    {
        var primaryCandidates = new Dictionary<string, FlatOut4SaveCandidate>(StringComparer.OrdinalIgnoreCase);
        AddSteamCandidates(primaryCandidates, schema, includeFallbackFiles: false);
        AddOfflineCandidates(primaryCandidates, schema, includeFallbackFiles: false);

        if (primaryCandidates.Count > 0)
        {
            return SortCandidates(primaryCandidates.Values);
        }

        var fallbackCandidates = new Dictionary<string, FlatOut4SaveCandidate>(StringComparer.OrdinalIgnoreCase);
        AddSteamCandidates(fallbackCandidates, schema, includeFallbackFiles: true);
        AddOfflineCandidates(fallbackCandidates, schema, includeFallbackFiles: true);
        return SortCandidates(fallbackCandidates.Values);
    }

    public static IReadOnlyList<string> FindSteamCloudSaves()
    {
        FlatOut4SaveSchema schema = FlatOut4SaveSchema.Create();
        return FindSaveCandidates(schema)
            .Select(candidate => candidate.Path)
            .ToArray();
    }

    public static IReadOnlyList<string> GetCheckedSaveLocations()
    {
        var locations = new List<string>();
        foreach (string steamRoot in GetSteamRoots())
        {
            foreach (SteamAppInfo app in SteamApps)
            {
                foreach (string relativeSavePath in app.PrimaryRelativeSavePaths)
                {
                    locations.Add(Path.Combine(steamRoot, "userdata", "<steamid>", app.AppId, "remote", relativeSavePath));
                }
            }
        }

        foreach (string directory in GetOfflineSaveDirectories())
        {
            locations.Add(Path.Combine(directory, "Save"));
            locations.Add(Path.Combine(directory, "Save.dat"));
        }

        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddSteamCandidates(
        Dictionary<string, FlatOut4SaveCandidate> candidates,
        FlatOut4SaveSchema schema,
        bool includeFallbackFiles)
    {
        foreach (string steamRoot in GetSteamRoots())
        {
            string steamUserData = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(steamUserData))
            {
                continue;
            }

            HashSet<string> recentSteamUsers = GetMostRecentSteamUserIds(steamRoot);
            foreach (string userDirectory in SafeEnumerateDirectories(steamUserData))
            {
                string steamUserId = Path.GetFileName(userDirectory);
                int userPenalty = recentSteamUsers.Count == 0 || recentSteamUsers.Contains(steamUserId) ? 0 : 5;

                foreach (SteamAppInfo app in SteamApps)
                {
                    string remoteDirectory = Path.Combine(userDirectory, app.AppId, "remote");
                    if (!Directory.Exists(remoteDirectory))
                    {
                        continue;
                    }

                    for (int pathIndex = 0; pathIndex < app.PrimaryRelativeSavePaths.Length; pathIndex++)
                    {
                        string exactSave = Path.Combine(remoteDirectory, app.PrimaryRelativeSavePaths[pathIndex]);
                        AddCandidate(
                            candidates,
                            exactSave,
                            $"{app.DisplayName} Steam Cloud",
                            app.AppId,
                            steamUserId,
                            schema,
                            isPrimary: true,
                            priority: app.Priority + userPenalty + pathIndex);
                    }

                    if (!includeFallbackFiles)
                    {
                        continue;
                    }

                    foreach (string file in SafeEnumerateFiles(remoteDirectory, SearchOption.TopDirectoryOnly))
                    {
                        if (!IsFallbackSaveFile(file))
                        {
                            continue;
                        }

                        AddCandidate(
                            candidates,
                            file,
                            $"{app.DisplayName} Steam Cloud file",
                            app.AppId,
                            steamUserId,
                            schema,
                            isPrimary: false,
                            priority: app.Priority + userPenalty + 100);
                    }
                }
            }
        }
    }

    private static void AddOfflineCandidates(
        Dictionary<string, FlatOut4SaveCandidate> candidates,
        FlatOut4SaveSchema schema,
        bool includeFallbackFiles)
    {
        foreach (string directory in GetOfflineSaveDirectories())
        {
            AddCandidate(
                candidates,
                Path.Combine(directory, "Save"),
                "Offline save folder",
                null,
                null,
                schema,
                isPrimary: true,
                priority: 20);

            AddCandidate(
                candidates,
                Path.Combine(directory, "Save.dat"),
                "Offline save folder",
                null,
                null,
                schema,
                isPrimary: true,
                priority: 21);

            if (!includeFallbackFiles || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in SafeEnumerateFiles(directory, SearchOption.TopDirectoryOnly))
            {
                if (!IsFallbackSaveFile(file))
                {
                    continue;
                }

                AddCandidate(
                    candidates,
                    file,
                    "Offline save folder file",
                    null,
                    null,
                    schema,
                    isPrimary: false,
                    priority: 120);
            }
        }
    }

    private static void AddCandidate(
        Dictionary<string, FlatOut4SaveCandidate> candidates,
        string path,
        string source,
        string? appId,
        string? steamUserId,
        FlatOut4SaveSchema schema,
        bool isPrimary,
        int priority)
    {
        if (!TryReadSupportedSaveInfo(path, schema, out SaveInfo saveInfo))
        {
            return;
        }

        string fullPath = GetFullPath(path);
        var candidate = new FlatOut4SaveCandidate(
            fullPath,
            source,
            appId,
            steamUserId,
            saveInfo.Version,
            saveInfo.Size,
            saveInfo.LastWriteTimeUtc,
            isPrimary,
            priority);

        if (!candidates.TryGetValue(fullPath, out FlatOut4SaveCandidate? existing) ||
            IsBetterCandidate(candidate, existing))
        {
            candidates[fullPath] = candidate;
        }
    }

    private static IReadOnlyList<FlatOut4SaveCandidate> SortCandidates(IEnumerable<FlatOut4SaveCandidate> candidates)
    {
        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBetterCandidate(FlatOut4SaveCandidate candidate, FlatOut4SaveCandidate existing)
    {
        if (candidate.Priority != existing.Priority)
        {
            return candidate.Priority < existing.Priority;
        }

        return candidate.LastWriteTimeUtc > existing.LastWriteTimeUtc;
    }

    private static string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static bool TryReadSupportedSaveInfo(string path, FlatOut4SaveSchema schema, out SaveInfo saveInfo)
    {
        saveInfo = default;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 12)
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
            if (footer != FlatOut4SaveSchema.FooterValue)
            {
                return false;
            }

            if (!TryGetSaveLayout(version, info.Length, schema, out _))
            {
                return false;
            }

            stream.Position = info.Length - sizeof(uint);
            Span<byte> tailBytes = stackalloc byte[sizeof(uint)];
            if (stream.Read(tailBytes) != tailBytes.Length ||
                BitConverter.ToUInt32(tailBytes) != FlatOut4SaveSchema.FooterValue)
            {
                return false;
            }

            saveInfo = new SaveInfo(version, info.Length, info.LastWriteTimeUtc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFallbackSaveFile(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Equals("Save", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Save.dat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.Contains(".bak", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("old", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.StartsWith("Save", StringComparison.OrdinalIgnoreCase) || IsLikelyFlatOutSave(path);
    }

    private static HashSet<string> GetMostRecentSteamUserIds(string steamRoot)
    {
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string loginUsersPath = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(loginUsersPath))
        {
            return users;
        }

        try
        {
            string text = File.ReadAllText(loginUsersPath);
            foreach (Match match in Regex.Matches(text, "\"(?<id>\\d+)\"\\s*\\{(?<body>.*?)\\}", RegexOptions.Singleline))
            {
                string body = match.Groups["body"].Value;
                if (Regex.IsMatch(body, "\"MostRecent\"\\s*\"1\"", RegexOptions.IgnoreCase))
                {
                    users.Add(match.Groups["id"].Value);
                }
            }
        }
        catch
        {
            return [];
        }

        return users;
    }

    private sealed record SteamAppInfo(string AppId, string DisplayName, int Priority, string[] PrimaryRelativeSavePaths);

    private sealed record SaveLayout(uint Version, int GameplayOptionsSize, int BoundActionCount, int MenuBindingCount)
    {
        public int UserBindingsOffset => GameplayOptionsSize;

        public int InputBindingSize => FlatOut4SaveSchema.BindingDeviceCount * BoundActionCount * FlatOut4SaveSchema.UInt32Size;

        public int DeviceSettingsOffset => UserBindingsOffset + InputBindingSize;

        public int AudioVolumesOffset => DeviceSettingsOffset + FlatOut4SaveSchema.DeviceSettingsSize;

        public int MenuBindingsOffset => AudioVolumesOffset + FlatOut4SaveSchema.AudioVolumesSize;

        public int MenuBindingSize => FlatOut4SaveSchema.BindingDeviceCount * MenuBindingCount * FlatOut4SaveSchema.UInt32Size;

        public int GameOptionsSize => MenuBindingsOffset + MenuBindingSize;

        public int SerializableSize(FlatOut4SaveSchema schema) =>
            schema.SerializableSize - (FlatOut4SaveSchema.GameOptionsSize - GameOptionsSize);
    }

    private readonly record struct SaveInfo(uint Version, long Size, DateTime LastWriteTimeUtc);

    private static bool TryGetSaveLayout(uint version, long size, FlatOut4SaveSchema schema, out SaveLayout? layout)
    {
        layout = SaveLayouts.FirstOrDefault(candidate =>
            candidate.Version == version &&
            candidate.SerializableSize(schema) == size);

        return layout is not null;
    }

    private static byte[] MigrateToCurrent(byte[] source, FlatOut4SaveSchema schema, SaveLayout layout)
    {
        byte[] destination = new byte[schema.SerializableSize];
        int gameOptionsStart = FlatOut4SaveSchema.GameOptionsOffset;

        Array.Copy(source, 0, destination, 0, gameOptionsStart);
        Array.Copy(source, gameOptionsStart, destination, gameOptionsStart, layout.GameplayOptionsSize);
        ApplyMissingGameplayOptionDefaults(destination, gameOptionsStart, layout.GameplayOptionsSize);

        Array.Fill(
            destination,
            (byte)0xFF,
            gameOptionsStart + FlatOut4SaveSchema.CurrentUserBindingsOffset,
            FlatOut4SaveSchema.CurrentInputBindingSize);
        CopyInputBindingsIntoCurrent(
            source,
            gameOptionsStart + layout.UserBindingsOffset,
            destination,
            gameOptionsStart + FlatOut4SaveSchema.CurrentUserBindingsOffset,
            layout);

        int sourceDeviceSettingsOffset = layout.DeviceSettingsOffset;
        int sourceAudioVolumesOffset = layout.AudioVolumesOffset;
        int sourceMenuBindingsOffset = layout.MenuBindingsOffset;
        if (layout.Version == 89)
        {
            SelectV89SourceOffsets(source, gameOptionsStart, layout, out sourceMenuBindingsOffset, out sourceDeviceSettingsOffset, out sourceAudioVolumesOffset);
        }

        Array.Copy(
            source,
            gameOptionsStart + sourceDeviceSettingsOffset,
            destination,
            gameOptionsStart + FlatOut4SaveSchema.CurrentDeviceSettingsOffset,
            FlatOut4SaveSchema.DeviceSettingsSize);

        Array.Copy(
            source,
            gameOptionsStart + sourceAudioVolumesOffset,
            destination,
            gameOptionsStart + FlatOut4SaveSchema.CurrentAudioVolumesOffset,
            FlatOut4SaveSchema.AudioVolumesSize);

        Array.Fill(
            destination,
            (byte)0xFF,
            gameOptionsStart + FlatOut4SaveSchema.CurrentMenuBindingsOffset,
            FlatOut4SaveSchema.CurrentMenuBindingSize);
        if (layout.MenuBindingCount > 0)
        {
            CopyMenuBindingsIntoCurrent(
                source,
                gameOptionsStart + sourceMenuBindingsOffset,
                destination,
                gameOptionsStart + FlatOut4SaveSchema.CurrentMenuBindingsOffset,
                layout.MenuBindingCount);
            SanitizeMenuBindings(destination, gameOptionsStart + FlatOut4SaveSchema.CurrentMenuBindingsOffset, FlatOut4SaveSchema.CurrentMenuBindingCount);
        }

        int sourceAfterGameOptions = gameOptionsStart + layout.GameOptionsSize;
        int destinationAfterGameOptions = gameOptionsStart + FlatOut4SaveSchema.GameOptionsSize;
        int trailingSize = Math.Min(source.Length - sourceAfterGameOptions, destination.Length - destinationAfterGameOptions);
        if (trailingSize > 0)
        {
            Array.Copy(source, sourceAfterGameOptions, destination, destinationAfterGameOptions, trailingSize);
        }

        WriteUInt32(destination, 4, FlatOut4SaveSchema.CurrentVersion);
        return destination;
    }

    private static void ApplyMissingGameplayOptionDefaults(byte[] bytes, int gameOptionsStart, int sourceGameplayOptionsSize)
    {
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 36, 100); // m_uFFBStrength
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 40, 0);   // m_uWheelRotationDegrees
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 44, 0);   // m_uPedalSwapOverride
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 48, 0);   // m_uPedalSwapEnabled
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 52, 0);   // m_uManualShift
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 56, 0);   // m_uManualShiftRequireClutch
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 60, 1);   // m_uAutoEngageManualShiftOnHShifter
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 64, 0);   // m_uVRMotionSteeringEnabled
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 68, 130); // m_uVRMotionLockToLockDeg
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 72, 20);  // m_uVRMotionDeadzoneDeg
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 76, 50);  // m_uVRMotionSensitivity
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 80, 0);   // m_uVRMotionAutoRecenterIdle
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 84, 1);   // m_uVRMotionDominantHand
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 88, 1);   // m_uVRMotionEnableHaptic
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 92, 0);   // m_uDisableCameraShake
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 96, 0);   // m_uComfortVignette
        WriteUInt32IfMissing(bytes, gameOptionsStart, sourceGameplayOptionsSize, 100, 0);  // m_uInputPrimaryFamily
    }

    private static void WriteUInt32IfMissing(byte[] bytes, int gameOptionsStart, int sourceGameplayOptionsSize, int fieldOffset, uint defaultValue)
    {
        if (sourceGameplayOptionsSize <= fieldOffset)
        {
            WriteUInt32(bytes, gameOptionsStart + fieldOffset, defaultValue);
        }
    }

    private static void CopyInputBindingsIntoCurrent(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, SaveLayout layout)
    {
        int sourceRowSize = layout.BoundActionCount * FlatOut4SaveSchema.UInt32Size;
        int destinationRowSize = FlatOut4SaveSchema.CurrentBoundActionCount * FlatOut4SaveSchema.UInt32Size;

        for (int device = 0; device < FlatOut4SaveSchema.BindingDeviceCount; device++)
        {
            int sourceRowOffset = sourceOffset + (device * sourceRowSize);
            int destinationRowOffset = destinationOffset + (device * destinationRowSize);
            for (int sourceAction = 0; sourceAction < layout.BoundActionCount; sourceAction++)
            {
                int destinationAction = MapSourceActionToCurrent(layout.Version, sourceAction);
                if (destinationAction < 0 || destinationAction >= FlatOut4SaveSchema.CurrentBoundActionCount)
                {
                    continue;
                }

                Array.Copy(
                    source,
                    sourceRowOffset + (sourceAction * FlatOut4SaveSchema.UInt32Size),
                    destination,
                    destinationRowOffset + (destinationAction * FlatOut4SaveSchema.UInt32Size),
                    FlatOut4SaveSchema.UInt32Size);
            }
        }
    }

    private static int MapSourceActionToCurrent(uint sourceVersion, int sourceAction)
    {
        if (sourceVersion <= 85)
        {
            if (sourceAction <= 16)
            {
                return sourceAction;
            }

            if (sourceAction <= 24)
            {
                return sourceAction + 8;
            }

            return sourceAction + 9;
        }

        if (sourceVersion <= 90)
        {
            return sourceAction <= 32 ? sourceAction : sourceAction + 1;
        }

        return sourceAction;
    }

    private static void SelectV89SourceOffsets(
        byte[] source,
        int gameOptionsStart,
        SaveLayout layout,
        out int menuBindingsOffset,
        out int deviceSettingsOffset,
        out int audioVolumesOffset)
    {
        menuBindingsOffset = layout.MenuBindingsOffset;
        deviceSettingsOffset = layout.DeviceSettingsOffset;
        audioVolumesOffset = layout.AudioVolumesOffset;

        int oldMenuBindingsOffset = layout.DeviceSettingsOffset;
        int oldDeviceSettingsOffset = oldMenuBindingsOffset + FlatOut4SaveSchema.V90MenuBindingSize;
        int oldAudioVolumesOffset = oldDeviceSettingsOffset + FlatOut4SaveSchema.DeviceSettingsSize;
        int tailInvalid = CountImplausibleMenuControlsAtOffset(source, gameOptionsStart + layout.MenuBindingsOffset, FlatOut4SaveSchema.V90MenuBindingCount);
        int oldInvalid = CountImplausibleMenuControlsAtOffset(source, gameOptionsStart + oldMenuBindingsOffset, FlatOut4SaveSchema.V90MenuBindingCount);

        if (tailInvalid != 0 && oldInvalid < tailInvalid)
        {
            menuBindingsOffset = oldMenuBindingsOffset;
            deviceSettingsOffset = oldDeviceSettingsOffset;
            audioVolumesOffset = oldAudioVolumesOffset;
        }
    }

    private static void CopyMenuBindingsIntoCurrent(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int sourceMenuBindingCount)
    {
        int sourceRowSize = sourceMenuBindingCount * FlatOut4SaveSchema.UInt32Size;
        int destinationRowSize = FlatOut4SaveSchema.CurrentMenuBindingCount * FlatOut4SaveSchema.UInt32Size;

        for (int device = 0; device < FlatOut4SaveSchema.BindingDeviceCount; device++)
        {
            Array.Copy(
                source,
                sourceOffset + (device * sourceRowSize),
                destination,
                destinationOffset + (device * destinationRowSize),
                sourceRowSize);
        }
    }

    private static int CountImplausibleMenuControlsAtOffset(byte[] bytes, int offset, int menuBindingCount)
    {
        int invalidCount = 0;
        for (int device = 0; device < FlatOut4SaveSchema.BindingDeviceCount; device++)
        {
            for (int action = 0; action < menuBindingCount; action++)
            {
                int valueOffset = offset + (((device * menuBindingCount) + action) * FlatOut4SaveSchema.UInt32Size);
                uint value = BitConverter.ToUInt32(bytes, valueOffset);
                if (!IsPlausibleMenuControl(device, value))
                {
                    invalidCount++;
                }
            }
        }

        return invalidCount;
    }

    private static void SanitizeMenuBindings(byte[] bytes, int offset, int menuBindingCount)
    {
        for (int device = 0; device < FlatOut4SaveSchema.BindingDeviceCount; device++)
        {
            for (int action = 0; action < menuBindingCount; action++)
            {
                int valueOffset = offset + (((device * menuBindingCount) + action) * FlatOut4SaveSchema.UInt32Size);
                uint value = BitConverter.ToUInt32(bytes, valueOffset);
                if (!IsPlausibleMenuControl(device, value))
                {
                    WriteUInt32(bytes, valueOffset, InvalidControlId);
                }
            }
        }
    }

    private static bool IsPlausibleMenuControl(int device, uint value)
    {
        if (value is InvalidControlId or ClearedMenuControlId)
        {
            return true;
        }

        return device switch
        {
            0 => value > 0 && value <= KeyboardMediaSelectControlId,
            1 => value <= GamepadPointer2YControlId,
            2 => value <= GenericDevicePovUpControlId,
            _ => false
        };
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

    private static IEnumerable<string> GetOfflineSaveDirectories()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(documents, "My Games", "FlatOut 4"),
            Path.Combine(documents, "My Games", "Flatout VR"),
            Path.Combine(documents, "My Games", "FlatOut4"),
            Path.Combine(localAppData, "Flatout VR"),
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

    private static IEnumerable<string> SafeEnumerateFiles(string path, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", searchOption).ToArray();
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
            return footer == FlatOut4SaveSchema.FooterValue &&
                version >= FlatOut4SaveSchema.EarliestSupportedVersion &&
                version <= FlatOut4SaveSchema.CurrentVersion;
        }
        catch
        {
            return false;
        }
    }
}
