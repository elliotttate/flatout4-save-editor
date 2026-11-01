using System.Text.RegularExpressions;
using FlatOut4SaveEditor.Models;

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
        new(95, FlatOut4SaveSchema.CurrentGameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount),
        new(96, FlatOut4SaveSchema.V96GameplayOptionsSize, FlatOut4SaveSchema.CurrentBoundActionCount, FlatOut4SaveSchema.CurrentMenuBindingCount)
    ];

    private static readonly SteamAppInfo[] SteamApps =
    [
        new("3844750", "FlatOut 4 VR", 0, ["Flatout VR\\Save.dat", "Save"]),
        new("402130", "FlatOut 4", 10, ["Save"])
    ];

    public static FlatOut4SaveDocument Load(string path, FlatOut4SaveSchema schema) =>
        Read(File.ReadAllBytes(path), path, schema);

    // Reads any FlatOut 4 profile it can make sense of. Known layouts go through the exact
    // migrations; a known version that is cut off or padded is resized first; anything else
    // (a newer build's layout, a damaged header, a mangled version stamp) is matched value by
    // value against V95 by FlatOut4SaveRecovery. Only files too far gone to match are refused.
    public static FlatOut4SaveDocument Read(byte[] source, string? path, FlatOut4SaveSchema schema)
    {
        if (source.Length < 8)
        {
            throw new InvalidDataException("This file is too small to be a FlatOut 4 save.");
        }

        uint version = BitConverter.ToUInt32(source, 4);
        if (TryGetSaveLayout(version, source.Length, schema, out SaveLayout? layout) && !HasShiftedExtensionSlots(source, schema, layout!))
        {
            return ReadKnownLayout(source, path, schema, layout!, version);
        }

        SaveLayout? known = SaveLayouts.FirstOrDefault(candidate => candidate.Version == version);
        if (known is not null && known.Version != FlatOut4SaveSchema.CurrentVersion && source.Length != schema.SerializableSize &&
            source.Length >= FlatOut4SaveSchema.GameOptionsOffset + known.GameOptionsSize)
        {
            return ReadResized(source, path, schema, known);
        }

        return ReadByMatching(source, path, schema, version);
    }

    private static FlatOut4SaveDocument ReadKnownLayout(byte[] source, string? path, FlatOut4SaveSchema schema, SaveLayout layout, uint version)
    {
        bool migrated = false;
        string warning = string.Empty;
        byte[] bytes;
        if (layout.Version == FlatOut4SaveSchema.CurrentVersion)
        {
            bytes = source;
        }
        else if (layout.Version == FlatOut4SaveSchema.RcTelemetryVersion)
        {
            bytes = ConvertRcTelemetryToCurrent(source, schema, out bool telemetryOn);
            migrated = true;
            warning = $"Converted an RC10-RC17 V96 save to the shared V{FlatOut4SaveSchema.CurrentVersion} layout, the same way the game does. The telemetry opt-in ({(telemetryOn ? "ON" : "OFF")}) moved to the tagged extension slots. Saving writes V{FlatOut4SaveSchema.CurrentVersion}, which the live Steam build can also read.";
        }
        else
        {
            bytes = MigrateToCurrent(source, schema, layout);
            migrated = true;
            warning = $"Migrated V{version} save bytes to the V{FlatOut4SaveSchema.CurrentVersion} layout. Saving will write V{FlatOut4SaveSchema.CurrentVersion}; keep the automatic backup if you still need the original older-layout file.";
        }

        // The markers are the one thing the game checks besides the version; saving rewrites both.
        var repairs = new List<string>();
        if (BitConverter.ToUInt32(source, 0) != FlatOut4SaveSchema.FooterValue)
        {
            repairs.Add("The first four bytes were not the \"FOO \" marker the game checks for.");
        }

        if (BitConverter.ToUInt32(source, source.Length - 4) != FlatOut4SaveSchema.FooterValue)
        {
            repairs.Add("The last four bytes were not the \"FOO \" end marker the game checks for.");
        }

        if (repairs.Count == 0)
        {
            return new FlatOut4SaveDocument(path, bytes, version, migrated, warning);
        }

        string repaired = "The save's start or end marker was damaged. Every value reads normally; saving writes the markers back so the game accepts the file.";
        var report = new FlatOut4SaveRecoveryReport("Markers repaired", repaired, repairs);
        return new FlatOut4SaveDocument(path, bytes, version, migrated: true, Join(warning, repaired), report);
    }

    // A known older version with the wrong length: cut off (the tail starts as a new profile has
    // it) or padded (the extra bytes are dropped). The exact migration does the rest.
    private static FlatOut4SaveDocument ReadResized(byte[] source, string? path, FlatOut4SaveSchema schema, SaveLayout layout)
    {
        int expected = layout.SerializableSize(schema);
        byte[] resized = new byte[expected];
        Array.Copy(source, resized, Math.Min(source.Length, expected));
        byte[] bytes = layout.Version == FlatOut4SaveSchema.RcTelemetryVersion
            ? ConvertRcTelemetryToCurrent(resized, schema, out _)
            : MigrateToCurrent(resized, schema, layout);

        string note;
        if (source.Length < expected)
        {
            // Everything after the game options keeps its order, shifted by the size difference.
            int missingFrom = source.Length + (schema.SerializableSize - expected);
            byte[] fresh = FlatOut4SaveDefaults.CreateFreshProfile(schema);
            Array.Copy(fresh, missingFrom, bytes, missingFrom, bytes.Length - missingFrom);
            note = $"The file is cut off: the last {expected - source.Length:N0} bytes, from {DescribeLocation(schema, missingFrom)} to the end, are missing. Those values start as a new profile has them.";
        }
        else
        {
            note = $"{source.Length - expected:N0} extra bytes after the end of the profile were left out.";
        }

        string summary = $"This V{layout.Version} save is {source.Length:N0} bytes instead of {expected:N0}. It was read as V{layout.Version} and converted to V{FlatOut4SaveSchema.CurrentVersion}; saving writes a complete V{FlatOut4SaveSchema.CurrentVersion} save.";
        var report = new FlatOut4SaveRecoveryReport(source.Length < expected ? "Cut-off save recovered" : "Padded save recovered", summary, [note]);
        return new FlatOut4SaveDocument(path, bytes, layout.Version, migrated: true, $"{summary} {note}", report);
    }

    private static FlatOut4SaveDocument ReadByMatching(byte[] source, string? path, FlatOut4SaveSchema schema, uint version)
    {
        FlatOut4RecoveryResult result = FlatOut4SaveRecovery.Recover(source, schema);
        if (!result.IsUsable(schema))
        {
            throw new InvalidDataException(DescribeUnsupportedSave(version, source.Length, schema) +
                $" Matching its values against the V{FlatOut4SaveSchema.CurrentVersion} layout did not work either ({result.ImplausibleValues:N0} of {result.CheckedValues:N0} values made no sense), so it is probably not a FlatOut 4 profile.");
        }

        var notes = new List<string>(result.Notes);
        if (result.ImplausibleValues > 0)
        {
            notes.Add($"{result.ImplausibleValues:N0} of {result.CheckedValues:N0} values look damaged. Save health lists the ones the game cannot use.");
        }

        // A grown FOSerializableExt eats into the fixed padding, so "missing" padding loses nothing.
        bool newer = version > FlatOut4SaveSchema.LatestReadableVersion && version < 1000;
        if (newer && result.Map.Missing.All(range => IsPadding(schema, range)))
        {
            string kept = result.Map.UnknownBytes > 0
                ? $" and keeps the other {result.Map.UnknownBytes:N0} bytes exactly as they are"
                : string.Empty;
            string summary = $"This save is V{version}, newer than this editor knows. It found every V{FlatOut4SaveSchema.CurrentVersion} value by matching{kept}. Saving writes V{version} back in the same layout. The live Steam build (V{FlatOut4SaveSchema.CurrentVersion}) resets V{version} saves.";
            var newerReport = new FlatOut4SaveRecoveryReport($"Newer V{version} layout", summary, notes, result.Map, source, result.CheckedValues, result.ImplausibleValues);
            return new FlatOut4SaveDocument(path, result.Bytes, version, migrated: false, summary, newerReport);
        }

        string what = SaveLayouts.Any(layout => layout.Version == version) || version == FlatOut4SaveSchema.CurrentVersion
            ? $"This V{version} save is {source.Length:N0} bytes, but V{version} saves are {ExpectedSize(version, schema):N0}."
            : $"This file is stamped V{version} and is {source.Length:N0} bytes, which matches no layout this editor knows.";
        string healed = $"{what} It was read by matching values against the V{FlatOut4SaveSchema.CurrentVersion} layout; saving writes a complete V{FlatOut4SaveSchema.CurrentVersion} save. Keep the automatic backup.";
        var report = new FlatOut4SaveRecoveryReport("Recovered by matching values", healed, notes, result.Map, null, result.CheckedValues, result.ImplausibleValues);
        return new FlatOut4SaveDocument(path, result.Bytes, version, migrated: true, healed, report);
    }

    // A V95-sized file whose FXT1 tag sits a few slots late: something grew in front of m_aOther
    // (FOSerializableExt) without changing the size, so the slots have to be found by matching.
    private static bool HasShiftedExtensionSlots(byte[] source, FlatOut4SaveSchema schema, SaveLayout layout)
    {
        if (layout.Version != FlatOut4SaveSchema.CurrentVersion ||
            BitConverter.ToUInt32(source, schema.ExtensionSlotOffset(FlatOut4SaveSchema.ExtensionSlotMagic)) == FlatOut4SaveSchema.ExtensionMagicValue)
        {
            return false;
        }

        for (int slot = FlatOut4SaveSchema.ExtensionSlotMagic + 1; slot <= FlatOut4SaveSchema.ExtensionSlotMagic + 32; slot++)
        {
            if (BitConverter.ToUInt32(source, schema.ExtensionSlotOffset(slot)) == FlatOut4SaveSchema.ExtensionMagicValue &&
                BitConverter.ToUInt32(source, schema.ExtensionSlotOffset(slot + 1)) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPadding(FlatOut4SaveSchema schema, FlatOut4SaveLayoutMap.Range range) =>
        schema.Fields.Any(field => field.Name.StartsWith("m_vPadding", StringComparison.Ordinal) && field.Offset == range.Offset) &&
        schema.Fields.Any(field => field.Name.StartsWith("m_vPadding", StringComparison.Ordinal) && field.Offset + field.Length == range.Offset + range.Length);

    private static int ExpectedSize(uint version, FlatOut4SaveSchema schema) =>
        SaveLayouts.FirstOrDefault(layout => layout.Version == version)?.SerializableSize(schema) ?? schema.SerializableSize;

    // "Garage › Car 12 state" for a V95 offset.
    public static string DescribeLocation(FlatOut4SaveSchema schema, int offset)
    {
        SaveFieldDefinition? field = schema.Fields
            .Where(candidate => candidate.Kind is not (SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff) && candidate.Offset <= offset)
            .MaxBy(candidate => candidate.Offset);
        return field is null ? $"0x{offset:X}" : $"{field.Section} › {field.ShortName}";
    }

    private static string Join(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";

    // Returns the backup path when one was made.
    public static string? Save(FlatOut4SaveDocument document, string path, bool createBackup)
    {
        string? backupPath = null;
        if (File.Exists(path) && createBackup)
        {
            // Two saves in the same second get -2, -3... rather than failing on an existing backup.
            string stamp = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            backupPath = stamp;
            for (int attempt = 2; File.Exists(backupPath); attempt++)
            {
                backupPath = $"{stamp}-{attempt}";
            }

            File.Copy(path, backupPath, overwrite: false);
        }

        WriteUInt32(document.Bytes, 0, FlatOut4SaveSchema.FooterValue);
        WriteUInt32(document.Bytes, 4, FlatOut4SaveSchema.CurrentVersion);
        WriteUInt32(document.Bytes, document.Bytes.Length - 4, FlatOut4SaveSchema.FooterValue);
        byte[] output = document.Recovery is { PreservesLayout: true } report
            ? report.Map!.WriteBack(document.Bytes, report.OriginalFile!)
            : document.Bytes;
        File.WriteAllBytes(path, output);
        document.Path = path;
        return backupPath;
    }

    public static IReadOnlyList<FlatOut4SaveCandidate> FindSaveCandidates(FlatOut4SaveSchema schema)
    {
        var primaryCandidates = new Dictionary<string, FlatOut4SaveCandidate>(StringComparer.OrdinalIgnoreCase);
        AddSteamCandidates(primaryCandidates, schema, includeFallbackFiles: false);
        AddOfflineCandidates(primaryCandidates, schema, includeFallbackFiles: false);
        AddTestSlotCandidates(primaryCandidates, schema);

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
                            priority: app.Priority + userPenalty + pathIndex,
                            SaveLocationKind.SteamCloud);
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
                            priority: app.Priority + userPenalty + 100,
                            SaveLocationKind.SteamCloud);
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
                "Local save folder",
                null,
                null,
                schema,
                isPrimary: true,
                priority: 20,
                SaveLocationKind.Local);

            AddCandidate(
                candidates,
                Path.Combine(directory, "Save.dat"),
                "Local save folder",
                null,
                null,
                schema,
                isPrimary: true,
                priority: 21,
                SaveLocationKind.Local);

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
                    "Local save folder file",
                    null,
                    null,
                    schema,
                    isPrimary: false,
                    priority: 120,
                    SaveLocationKind.Local);
            }
        }
    }

    // Non-Retail builds write "DEBUGSave<slot>" into "My Games\Flatout VR Test <slot>" when a
    // test slot isolates the profile (FOX_WRECK_RUN_TEST_SLOT), and into the main folder otherwise.
    private static void AddTestSlotCandidates(Dictionary<string, FlatOut4SaveCandidate> candidates, FlatOut4SaveSchema schema)
    {
        foreach (string myGames in GetMyGamesDirectories())
        {
            foreach (string directory in SafeEnumerateDirectories(myGames))
            {
                if (!Path.GetFileName(directory).StartsWith("Flatout VR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string file in SafeEnumerateFiles(directory, SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (!name.StartsWith("DEBUGSave", StringComparison.OrdinalIgnoreCase) || name.Contains('_'))
                    {
                        continue;
                    }

                    string slot = name["DEBUGSave".Length..];
                    AddCandidate(
                        candidates,
                        file,
                        string.IsNullOrEmpty(slot) ? "Developer save" : $"Developer test slot {slot}",
                        null,
                        null,
                        schema,
                        isPrimary: true,
                        priority: 60,
                        SaveLocationKind.TestSlot);
                }
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
        int priority,
        SaveLocationKind kind)
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
            priority,
            kind,
            saveInfo.NeedsRecovery);

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

    // A file worth listing: a known layout, or a FOO-marked file close enough in size to be
    // recovered (a newer build's layout, a cut-off or padded save).
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

            stream.Position = info.Length - sizeof(uint);
            Span<byte> tailBytes = stackalloc byte[sizeof(uint)];
            bool tailValid = stream.Read(tailBytes) == tailBytes.Length && BitConverter.ToUInt32(tailBytes) == FlatOut4SaveSchema.FooterValue;
            bool knownLayout = TryGetSaveLayout(version, info.Length, schema, out _);
            if (!knownLayout && Math.Abs(info.Length - schema.SerializableSize) > MaxRecoverableSizeDifference)
            {
                return false;
            }

            saveInfo = new SaveInfo(version, info.Length, info.LastWriteTimeUtc, NeedsRecovery: !knownLayout || !tailValid);
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

    private readonly record struct SaveInfo(uint Version, long Size, DateTime LastWriteTimeUtc, bool NeedsRecovery);

    // How far from the V95 size a file can be and still be offered for recovery.
    private const int MaxRecoverableSizeDifference = 8192;

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

    // FOSaveMigration::MigrateV96ToV95: V96 is V95 with one OnOff telemetry opt-in appended to
    // FOGameplayOptions, so every byte after it sits four bytes later. The opt-in moves to the
    // tagged m_aOther slots and the file is stamped V95.
    private static byte[] ConvertRcTelemetryToCurrent(byte[] source, FlatOut4SaveSchema schema, out bool telemetryOn)
    {
        byte[] destination = new byte[schema.SerializableSize];
        int telemetryOffset = FlatOut4SaveSchema.GameOptionsOffset + FlatOut4SaveSchema.CurrentGameplayOptionsSize;
        int tailFooterOffset = destination.Length - FlatOut4SaveSchema.UInt32Size;
        telemetryOn = BitConverter.ToUInt32(source, telemetryOffset) == 1;

        Array.Copy(source, 0, destination, 0, telemetryOffset);
        Array.Copy(
            source,
            telemetryOffset + FlatOut4SaveSchema.UInt32Size,
            destination,
            telemetryOffset,
            tailFooterOffset - telemetryOffset);
        WriteUInt32(destination, tailFooterOffset, FlatOut4SaveSchema.FooterValue);
        WriteUInt32(destination, 4, FlatOut4SaveSchema.CurrentVersion);
        WriteUInt32(destination, schema.ExtensionSlotOffset(FlatOut4SaveSchema.ExtensionSlotMagic), FlatOut4SaveSchema.ExtensionMagicValue);
        WriteUInt32(destination, schema.ExtensionSlotOffset(FlatOut4SaveSchema.ExtensionSlotTelemetry), telemetryOn ? 1u : 0u);
        return destination;
    }

    private static string DescribeUnsupportedSave(uint version, long size, FlatOut4SaveSchema schema)
    {
        if (version > FlatOut4SaveSchema.LatestReadableVersion)
        {
            return $"This save is V{version}, which is newer than this editor understands (V{FlatOut4SaveSchema.EarliestSupportedVersion}-V{FlatOut4SaveSchema.CurrentVersion}, plus the RC-only V{FlatOut4SaveSchema.RcTelemetryVersion}). Update the editor before changing it.";
        }

        if (version < FlatOut4SaveSchema.EarliestSupportedVersion)
        {
            return $"This save is V{version}, which predates the oldest layout this editor can read (V{FlatOut4SaveSchema.EarliestSupportedVersion}).";
        }

        SaveLayout? layout = SaveLayouts.FirstOrDefault(candidate => candidate.Version == version);
        string expected = layout is null
            ? "a layout this editor does not know"
            : $"{layout.SerializableSize(schema):N0} bytes";
        return $"This V{version} save is {size:N0} bytes, but V{version} saves are {expected}. The file may be truncated or corrupt.";
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

    private static IEnumerable<string> GetMyGamesDirectories()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return [Path.Combine(documents, "My Games")];
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
            // Newer versions count too: the editor can read them by matching values.
            return footer == FlatOut4SaveSchema.FooterValue &&
                version >= FlatOut4SaveSchema.EarliestSupportedVersion &&
                version < 1000;
        }
        catch
        {
            return false;
        }
    }
}
