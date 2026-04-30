using System.Text.RegularExpressions;

namespace FlatOut4SaveEditor.Models;

public static partial class SaveFieldNameFormatter
{
    private static readonly IReadOnlyDictionary<string, string> Containers = new Dictionary<string, string>
    {
        ["m_oGameOptions"] = "Game options",
        ["m_oGameplayOptions"] = "Gameplay",
        ["m_uUserBindings"] = "Game binding",
        ["m_fDeviceSettings"] = "Device setting",
        ["m_iAudioVolumes"] = "Audio volume",
        ["m_uMenuBindings"] = "Menu binding",
        ["m_oStats"] = "Stats",
        ["m_oTrophies"] = "Trophies",
        ["m_oTrophiesUnlocked"] = "Unlocked trophies",
        ["m_xChunks"] = "Storage chunk",
        ["m_oCareerData"] = "Career",
        ["m_oCurrentEvent"] = "Current event",
        ["m_vDriversScore"] = "Driver result",
        ["m_vEventRewards"] = "Event reward",
        ["m_oGarageData"] = "Garage",
        ["m_oCareerGarageConfig"] = "Career garage selection",
        ["m_oSingleGarageConfig"] = "Single-player garage selection",
        ["m_oMultiGarageConfig"] = "Multiplayer garage selection",
        ["m_vCarsState"] = "Car",
        ["m_oUnlockableItem"] = "Unlock data",
        ["m_iUpgradeLevel"] = "Upgrade level",
        ["m_vCarSkinsState"] = "Skin",
        ["m_vDriversState"] = "Driver",
        ["m_vBoostFxState"] = "Boost effect",
        ["m_vCarHornState"] = "Horn",
        ["m_vTrackState"] = "Track",
        ["m_vCarSkins"] = "Selected skin",
        ["m_oChallengeModeData"] = "Challenge mode",
        ["m_oChallengesRecords"] = "Challenge record",
        ["m_oNewChallenges"] = "New challenge",
        ["m_oRecords"] = "Records",
        ["m_vBestLapTimeTrial"] = "Time-trial best lap",
        ["m_vBestLapOther"] = "Race best lap",
        ["m_vStuntsRecords"] = "Stunt score",
        ["m_vCarnageRecords"] = "Carnage score",
        ["m_vSurvivorRecords"] = "Survivor score",
        ["m_vKTFRecords"] = "Keep-the-flag score",
        ["m_vDMRecords"] = "Deathmatch score",
        ["m_oFavorites"] = "Favorites",
        ["m_vCarUsed"] = "Car usage",
        ["m_vTrackPlayed"] = "Track plays",
        ["m_vGameplayTypePlayed"] = "Mode plays",
        ["m_vStuntsPlayed"] = "Stunt plays",
        ["m_oGameModeStats"] = "Game mode",
        ["m_oGameplayStats"] = "Gameplay type",
        ["m_uNbWon"] = "Wins",
        ["m_uNbLost"] = "Losses",
        ["m_vRecentTrophies"] = "Recent trophy",
        ["m_uSuntsMedals"] = "Stunt medals",
        ["m_aOther"] = "Extra config",
        ["m_vPadding"] = "Reserved padding"
    };

    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["m_oFooterHead"] = "Header marker",
        ["m_oFooterTail"] = "Footer marker",
        ["m_uSaveVersion"] = "Save version",
        ["m_uInputDevice"] = "Last input device",
        ["m_fVibrationLevel"] = "Vibration level",
        ["m_uCamXInversed"] = "Invert camera X",
        ["m_uCamYInversed"] = "Invert camera Y",
        ["m_uSpeedUnit"] = "Speed unit",
        ["m_uDifficulty"] = "AI difficulty",
        ["m_vInteriorCamOffset"] = "Interior camera offset",
        ["m_bShowDriverInInterior"] = "Show driver in interior view",
        ["m_uFFBStrength"] = "Force-feedback strength",
        ["m_uWheelRotationDegrees"] = "Wheel rotation degrees",
        ["m_uPedalSwapOverride"] = "Override pedal swap",
        ["m_uPedalSwapEnabled"] = "Pedal swap enabled",
        ["m_uManualShift"] = "Manual shift",
        ["m_uManualShiftRequireClutch"] = "Require clutch for manual shift",
        ["m_uAutoEngageManualShiftOnHShifter"] = "Auto manual shift with H-shifter",
        ["m_uVRMotionSteeringEnabled"] = "VR motion steering enabled",
        ["m_uVRMotionLockToLockDeg"] = "VR motion lock-to-lock degrees",
        ["m_uVRMotionDeadzoneDeg"] = "VR motion deadzone degrees",
        ["m_uVRMotionSensitivity"] = "VR motion sensitivity",
        ["m_uVRMotionAutoRecenterIdle"] = "VR motion auto-recenter while idle",
        ["m_uVRMotionDominantHand"] = "VR motion dominant hand",
        ["m_uVRMotionEnableHaptic"] = "VR motion haptics",
        ["m_bUnlockedCrashForrestCrash"] = "Crash Forest Crash trophy guard",
        ["m_bHighestScoreInStuntReached"] = "Highest stunt score reached",
        ["m_bCollector"] = "Collector progress flag",
        ["m_bAllStarsCarMaxUpgrades"] = "All-Stars max-upgrade flag",
        ["m_bRaceWonWithoutAnyCrash"] = "Clean race win flag",
        ["m_bDestroyedCarForTheFirstTime"] = "First car destroyed flag",
        ["m_bExhaustNitroForTheFirstTime"] = "First full nitro drain flag",
        ["m_uNbArenaDeathMatchDominations"] = "Arena deathmatch dominations",
        ["m_uModesCompleted"] = "Completed modes bitmask",
        ["m_uCupClassesWon"] = "Won cup classes bitmask",
        ["m_uNitroLongestBurnDuration"] = "Longest nitro burn",
        ["m_fNitroBurnDuration"] = "Total nitro burn time",
        ["m_uNbDriverEjections"] = "Driver ejections",
        ["m_uNbBreakablesDestroyed"] = "Breakables destroyed",
        ["m_uNbOfflineKills"] = "Offline kills",
        ["m_uNbOnlineKills"] = "Online kills",
        ["m_uNbOfflineDeaths"] = "Offline deaths",
        ["m_uNbOnlineDeaths"] = "Online deaths",
        ["m_uNbOnlineSlams"] = "Online slams",
        ["m_uMaxNbCarsWreckedInOneRaceInArenaMode"] = "Most arena wrecks in one race",
        ["m_uNbFlagsStolenInCaptureTheFlagMode"] = "Flags stolen",
        ["m_uTrapsTypesUsedInASingleRace"] = "Trap types used in one race",
        ["m_fTotalGroundDistance"] = "Total ground distance",
        ["m_uMaxHorizontalDistanceCoveredWithAJump"] = "Longest jump distance",
        ["m_fTotalHorizontalJumpDistance"] = "Total jump distance",
        ["m_fTotalJumpDuration"] = "Total jump time",
        ["m_uMaxFlagCaptureDuration"] = "Longest flag capture",
        ["m_fTotalFlagCaptureDuration"] = "Total flag capture time",
        ["m_fTotalSurvivorDuration"] = "Total survivor time",
        ["m_fTotalEliminatorDuration"] = "Total eliminator time",
        ["m_fAccelerationDuration"] = "Total acceleration time",
        ["m_fBrakeDuration"] = "Total braking time",
        ["m_uNbGamesPlayed"] = "Games played",
        ["m_uNbStuntsPlayed"] = "Stunts played",
        ["m_uNbTrapsPlaced"] = "Traps placed",
        ["m_fPauseDuration"] = "Pause time",
        ["m_fOnlineDuration"] = "Online time",
        ["m_fDistanceTraveled"] = "Distance traveled",
        ["m_fDistanceTraveledBackward"] = "Reverse distance traveled",
        ["m_fPlayTime"] = "Play time",
        ["m_uNbRecentTrophies"] = "Recent trophy count",
        ["m_uNbCreditsEarned"] = "Credits earned",
        ["m_uNbPodiumStreaks"] = "Podium streak",
        ["m_uNbFirstPos"] = "First-place finishes",
        ["m_uMaxScore"] = "Best score",
        ["m_uMedals"] = "Medals bitmask",
        ["m_uChunkShift"] = "Bit chunk shift",
        ["m_iCredits"] = "Credits",
        ["m_iCurrentEventIndex"] = "Current event",
        ["m_iCurrentRound"] = "Current round",
        ["m_iIndex"] = "Item ID",
        ["m_iScore"] = "Score",
        ["m_bPlayer"] = "Player-controlled",
        ["m_oName"] = "Driver name ID",
        ["m_oId"] = "Event ID",
        ["m_oScore"] = "Medal",
        ["m_bNew"] = "New",
        ["m_bPoped"] = "Popup shown",
        ["m_eState"] = "Unlock state",
        ["m_iTrackLocaDiff"] = "Track ID offset",
        ["m_uCarType"] = "Car",
        ["m_uDriverType"] = "Driver",
        ["m_uCarSkin"] = "Skin",
        ["m_uHornType"] = "Horn",
        ["m_uBoostFXType"] = "Boost effect",
        ["m_iPlayerScore"] = "Total challenge score"
    };

    public static string Format(string rawName)
    {
        string[] parts = rawName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var labels = new List<string>();

        for (int i = 0; i < parts.Length; i++)
        {
            string label = FormatSegment(parts[i]);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (labels.Count > 0 && string.Equals(labels[^1], label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            labels.Add(label);
        }

        return labels.Count == 0 ? Humanize(rawName) : string.Join(" > ", labels);
    }

    private static string FormatSegment(string segment)
    {
        int bracketStart = segment.IndexOf('[');
        string baseName = bracketStart >= 0 ? segment[..bracketStart] : segment;
        string[] indices = BracketValueRegex().Matches(segment).Select(match => match.Groups[1].Value).ToArray();

        if (baseName == "Unlocked" && indices.Length == 1)
        {
            return $"Trophy unlocked: {Humanize(indices[0])}";
        }

        if (baseName == "m_uUserBindings" && indices.Length == 2)
        {
            return $"Game binding: {Humanize(indices[0])} - {Humanize(indices[1])}";
        }

        if (baseName == "m_uMenuBindings" && indices.Length == 2)
        {
            return $"Menu binding: {Humanize(indices[0])} - {Humanize(indices[1])}";
        }

        if (baseName == "m_fDeviceSettings" && indices.Length == 2)
        {
            return $"Device setting: {Humanize(indices[0])} - {Humanize(indices[1])}";
        }

        if (baseName == "m_iAudioVolumes" && indices.Length == 1)
        {
            return $"Audio volume: {Humanize(indices[0])}";
        }

        if (baseName == "m_iUpgradeLevel" && indices.Length == 1)
        {
            return $"{Humanize(indices[0])} upgrade level";
        }

        string label = Fields.TryGetValue(baseName, out string? fieldLabel)
            ? fieldLabel
            : Containers.TryGetValue(baseName, out string? containerLabel)
                ? containerLabel
                : Humanize(baseName);

        if (indices.Length == 0)
        {
            return label;
        }

        return indices.Length == 1 && int.TryParse(indices[0], out int slot)
            ? $"{label} slot {slot}"
            : $"{label}: {string.Join(" - ", indices.Select(Humanize))}";
    }

    private static string Humanize(string value)
    {
        string text = value;

        foreach (string prefix in new[]
        {
            "m_o", "m_u", "m_i", "m_f", "m_b", "m_v", "m_e",
            "FO_TROPHY_", "FO_ACTION_", "ACTION_RACE_", "FO_DEBUG_",
            "E_BINDING_DEVICES_", "E_CONTROLLER_PARAM_", "I_", "E_"
        })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                text = text[prefix.Length..];
                break;
            }
        }

        text = text
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("Nb", "Number of", StringComparison.Ordinal)
            .Replace("Sunts", "Stunt", StringComparison.Ordinal)
            .Replace("Poped", "Popped", StringComparison.Ordinal)
            .Replace("KTF", "Keep The Flag", StringComparison.Ordinal)
            .Replace("DM", "Deathmatch", StringComparison.Ordinal);

        text = CamelCaseBoundaryRegex().Replace(text, "$1 $2");
        text = MultiSpaceRegex().Replace(text, " ").Trim();
        text = text.ToLowerInvariant();

        foreach ((string source, string replacement) in new[]
        {
            ("vr", "VR"),
            ("ffb", "FFB"),
            ("dlc", "DLC"),
            ("ai", "AI"),
            ("hud", "HUD"),
            ("id", "ID"),
            ("x", "X"),
            ("y", "Y"),
            ("z", "Z")
        })
        {
            text = WordRegex(source).Replace(text, replacement);
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex BracketValueRegex();

    [GeneratedRegex(@"([a-z])([A-Z])")]
    private static partial Regex CamelCaseBoundaryRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    private static Regex WordRegex(string word) => new($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
}
