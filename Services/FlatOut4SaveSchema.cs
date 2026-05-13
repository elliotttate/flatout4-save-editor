using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

public sealed class FlatOut4SaveSchema
{
    public const uint FooterValue = 0x204F4F46; // "FOO " in little-endian saves
    public const uint CurrentVersion = 95;
    public const uint EarliestSupportedVersion = 82;
    public const int GameOptionsOffset = 12;
    public const int UInt32Size = 4;
    public const int BindingDeviceCount = 3;
    public const int ControllerSettingCount = 11;
    public const int AudioVolumeCount = 5;
    public const int V82BoundActionCount = 50;
    public const int V90BoundActionCount = 58;
    public const int V91BoundActionCount = 59;
    public const int CurrentBoundActionCount = 60;
    public const int V90MenuBindingCount = 10;
    public const int CurrentMenuBindingCount = 11;
    public const int V82GameplayOptionsSize = 36;
    public const int V83GameplayOptionsSize = 52;
    public const int V84GameplayOptionsSize = 56;
    public const int V85GameplayOptionsSize = 60;
    public const int V86GameplayOptionsSize = 64;
    public const int V92GameplayOptionsSize = 92;
    public const int V93GameplayOptionsSize = 96;
    public const int V94GameplayOptionsSize = 100;
    public const int CurrentGameplayOptionsSize = 104;
    public const int DeviceSettingsSize = BindingDeviceCount * ControllerSettingCount * UInt32Size;
    public const int AudioVolumesSize = AudioVolumeCount * UInt32Size;
    public const int V82InputBindingSize = BindingDeviceCount * V82BoundActionCount * UInt32Size;
    public const int V90InputBindingSize = BindingDeviceCount * V90BoundActionCount * UInt32Size;
    public const int V91InputBindingSize = BindingDeviceCount * V91BoundActionCount * UInt32Size;
    public const int CurrentInputBindingSize = BindingDeviceCount * CurrentBoundActionCount * UInt32Size;
    public const int V90MenuBindingSize = BindingDeviceCount * V90MenuBindingCount * UInt32Size;
    public const int CurrentMenuBindingSize = BindingDeviceCount * CurrentMenuBindingCount * UInt32Size;
    public const int CurrentUserBindingsOffset = CurrentGameplayOptionsSize;
    public const int CurrentDeviceSettingsOffset = CurrentUserBindingsOffset + CurrentInputBindingSize;
    public const int CurrentAudioVolumesOffset = CurrentDeviceSettingsOffset + DeviceSettingsSize;
    public const int CurrentMenuBindingsOffset = CurrentAudioVolumesOffset + AudioVolumesSize;
    public const int V82GameOptionsSize = V82GameplayOptionsSize + V82InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V83GameOptionsSize = V83GameplayOptionsSize + V82InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V84GameOptionsSize = V84GameplayOptionsSize + V82InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V85GameOptionsSize = V85GameplayOptionsSize + V82InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V86GameOptionsSize = V86GameplayOptionsSize + V90InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V88GameOptionsSize = V92GameplayOptionsSize + V90InputBindingSize + DeviceSettingsSize + AudioVolumesSize;
    public const int V90GameOptionsSize = V88GameOptionsSize + V90MenuBindingSize;
    public const int V91GameOptionsSize = V92GameplayOptionsSize + V91InputBindingSize + DeviceSettingsSize + AudioVolumesSize + CurrentMenuBindingSize;
    public const int V92GameOptionsSize = V92GameplayOptionsSize + CurrentInputBindingSize + DeviceSettingsSize + AudioVolumesSize + CurrentMenuBindingSize;
    public const int V93GameOptionsSize = V93GameplayOptionsSize + CurrentInputBindingSize + DeviceSettingsSize + AudioVolumesSize + CurrentMenuBindingSize;
    public const int V94GameOptionsSize = V94GameplayOptionsSize + CurrentInputBindingSize + DeviceSettingsSize + AudioVolumesSize + CurrentMenuBindingSize;
    public const int GameOptionsSize = CurrentGameplayOptionsSize + CurrentInputBindingSize + DeviceSettingsSize + AudioVolumesSize + CurrentMenuBindingSize;

    private FlatOut4SaveSchema(IReadOnlyList<SaveFieldDefinition> fields, int serializableSize)
    {
        Fields = fields;
        SerializableSize = serializableSize;
    }

    public IReadOnlyList<SaveFieldDefinition> Fields { get; }

    public int SerializableSize { get; }

    public static FlatOut4SaveSchema Create()
    {
        var b = new SchemaBuilder();

        b.AddFooter("Header", "m_oFooterHead");
        b.AddUInt32("Header", "m_uSaveVersion");
        b.AddUInt8("Header", "m_uInputDevice");
        b.Align(4);

        AddGameOptions(b);
        AddStats(b);
        AddTrophies(b);
        AddCareer(b);
        AddGarageData(b, "Garage", "m_oGarageData");
        AddGarageConfig(b, "Garage", "m_oCareerGarageConfig");
        AddGarageConfig(b, "Garage", "m_oSingleGarageConfig");
        AddGarageConfig(b, "Garage", "m_oMultiGarageConfig");
        AddChallengeMode(b);
        AddRecords(b);
        AddFavorites(b);

        b.Skip(1); // FOSerializableExt is intentionally empty, but C++ empty structs occupy one byte.
        b.Align(4);

        for (int i = 0; i < 1024; i++)
        {
            b.AddInt32("Extra", $"m_aOther[{i}]");
        }

        for (int i = 0; i < 31; i++)
        {
            b.AddInt32("Padding", $"m_vPadding[{i}]");
        }

        b.AddFooter("Footer", "m_oFooterTail");

        return new FlatOut4SaveSchema(b.Fields, b.Offset);
    }

    private static void AddGameOptions(SchemaBuilder b)
    {
        b.Mark("Options", "m_oGameOptions");

        b.AddFloat("Options", "m_oGameOptions.m_oGameplayOptions.m_fVibrationLevel");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uCamXInversed", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uCamYInversed", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uSpeedUnit", SpeedUnitLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uDifficulty", DifficultyLabels);
        b.AddFloat("Options", "m_oGameOptions.m_oGameplayOptions.m_vInteriorCamOffset.x");
        b.AddFloat("Options", "m_oGameOptions.m_oGameplayOptions.m_vInteriorCamOffset.y");
        b.AddFloat("Options", "m_oGameOptions.m_oGameplayOptions.m_vInteriorCamOffset.z");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_bShowDriverInInterior", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uFFBStrength");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uWheelRotationDegrees");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uPedalSwapOverride", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uPedalSwapEnabled", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uManualShift", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uManualShiftRequireClutch", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uAutoEngageManualShiftOnHShifter", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionSteeringEnabled", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionLockToLockDeg");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionDeadzoneDeg");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionSensitivity");
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionAutoRecenterIdle", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionDominantHand", DominantHandLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uVRMotionEnableHaptic", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uDisableCameraShake", OnOffLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uComfortVignette", ComfortVignetteLabels);
        b.AddUInt32("Options", "m_oGameOptions.m_oGameplayOptions.m_uInputPrimaryFamily", InputPrimaryFamilyLabels);

        for (int device = 0; device < BindingDeviceNames.Length; device++)
        {
            for (int action = 0; action < InputActionNames.Length; action++)
            {
                b.AddUInt32("Input", $"m_oGameOptions.m_uUserBindings[{BindingDeviceNames[device]}][{InputActionNames[action]}]");
            }
        }

        for (int device = 0; device < BindingDeviceNames.Length; device++)
        {
            for (int setting = 0; setting < ControllerSettingNames.Length; setting++)
            {
                b.AddFloat("Input", $"m_oGameOptions.m_fDeviceSettings[{BindingDeviceNames[device]}][{ControllerSettingNames[setting]}]");
            }
        }

        for (int i = 0; i < AudioVolumeNames.Length; i++)
        {
            b.AddInt32("Options", $"m_oGameOptions.m_iAudioVolumes[{AudioVolumeNames[i]}]");
        }

        for (int device = 0; device < BindingDeviceNames.Length; device++)
        {
            for (int action = 0; action < MenuBindingNames.Length; action++)
            {
                b.AddUInt32("Input", $"m_oGameOptions.m_uMenuBindings[{BindingDeviceNames[device]}][{MenuBindingNames[action]}]");
            }
        }
    }

    private static void AddStats(SchemaBuilder b)
    {
        b.AddBool8("Stats", "m_oStats.m_bUnlockedCrashForrestCrash");
        b.AddBool8("Stats", "m_oStats.m_bHighestScoreInStuntReached");
        b.AddUInt8("Stats", "m_oStats.m_bCollector");
        b.AddUInt8("Stats", "m_oStats.m_bAllStarsCarMaxUpgrades");
        b.AddUInt8("Stats", "m_oStats.m_bRaceWonWithoutAnyCrash");
        b.AddUInt8("Stats", "m_oStats.m_bDestroyedCarForTheFirstTime");
        b.AddUInt8("Stats", "m_oStats.m_bExhaustNitroForTheFirstTime");
        b.Align(4);

        b.AddUInt32("Stats", "m_oStats.m_uNbArenaDeathMatchDominations");
        b.AddUInt32("Stats", "m_oStats.m_uModesCompleted");
        b.AddUInt32("Stats", "m_oStats.m_uCupClassesWon");

        for (int medal = 0; medal < ScoreObjectiveNames.Length; medal++)
        {
            b.AddUInt32("Stats", $"m_oStats.m_uSuntsMedals[{ScoreObjectiveNames[medal]}]");
        }

        b.AddUInt32("Stats", "m_oStats.m_uNitroLongestBurnDuration");
        b.AddFloat("Stats", "m_oStats.m_fNitroBurnDuration");
        b.AddUInt32("Stats", "m_oStats.m_uNbDriverEjections");
        b.AddUInt32("Stats", "m_oStats.m_uNbBreakablesDestroyed");
        b.AddUInt32("Stats", "m_oStats.m_uNbOfflineKills");
        b.AddUInt32("Stats", "m_oStats.m_uNbOnlineKills");
        b.AddUInt32("Stats", "m_oStats.m_uNbOfflineDeaths");
        b.AddUInt32("Stats", "m_oStats.m_uNbOnlineDeaths");
        b.AddUInt32("Stats", "m_oStats.m_uNbOnlineSlams");
        b.AddUInt32("Stats", "m_oStats.m_uMaxNbCarsWreckedInOneRaceInArenaMode");
        b.AddUInt32("Stats", "m_oStats.m_uNbFlagsStolenInCaptureTheFlagMode");
        b.AddUInt32("Stats", "m_oStats.m_uTrapsTypesUsedInASingleRace");
        b.AddFloat("Stats", "m_oStats.m_fTotalGroundDistance");
        b.AddFloat("Stats", "m_oStats.m_uMaxHorizontalDistanceCoveredWithAJump");
        b.AddFloat("Stats", "m_oStats.m_fTotalHorizontalJumpDistance");
        b.AddFloat("Stats", "m_oStats.m_fTotalJumpDuration");
        b.AddUInt32("Stats", "m_oStats.m_uMaxFlagCaptureDuration");
        b.AddFloat("Stats", "m_oStats.m_fTotalFlagCaptureDuration");
        b.AddFloat("Stats", "m_oStats.m_fTotalSurvivorDuration");
        b.AddFloat("Stats", "m_oStats.m_fTotalEliminatorDuration");
        b.AddFloat("Stats", "m_oStats.m_fAccelerationDuration");
        b.AddFloat("Stats", "m_oStats.m_fBrakeDuration");
        b.AddUInt32("Stats", "m_oStats.m_uNbGamesPlayed");
        b.AddUInt32("Stats", "m_oStats.m_uNbStuntsPlayed");
        b.AddUInt32("Stats", "m_oStats.m_uNbTrapsPlaced");
        b.AddFloat("Stats", "m_oStats.m_fPauseDuration");
        b.AddFloat("Stats", "m_oStats.m_fOnlineDuration");
        b.AddFloat("Stats", "m_oStats.m_fDistanceTraveled");
        b.AddFloat("Stats", "m_oStats.m_fDistanceTraveledBackward");
        b.AddFloat("Stats", "m_oStats.m_fPlayTime");
        b.AddUInt32("Stats", "m_oStats.m_uNbRecentTrophies");

        for (int i = 0; i < 4; i++)
        {
            b.AddUInt32("Stats", $"m_oStats.m_vRecentTrophies[{i}]");
        }

        for (int mode = 0; mode < GameModeNames.Length; mode++)
        {
            string modePath = $"m_oStats.m_oGameModeStats[{GameModeNames[mode]}]";
            b.AddUInt32("Stats", $"{modePath}.m_uNbCreditsEarned");
            b.AddUInt32("Stats", $"{modePath}.m_uNbPodiumStreaks");
            b.AddUInt32("Stats", $"{modePath}.m_uNbFirstPos");

            for (int gameplay = 0; gameplay < GamePlayTypeNames.Length; gameplay++)
            {
                string gameplayPath = $"{modePath}.m_oGameplayStats[{GamePlayTypeNames[gameplay]}]";
                b.AddUInt32("Stats", $"{gameplayPath}.m_uMaxScore");
                b.AddUInt32("Stats", $"{gameplayPath}.m_uNbWon[Offline]");
                b.AddUInt32("Stats", $"{gameplayPath}.m_uNbWon[Online]");
                b.AddUInt32("Stats", $"{gameplayPath}.m_uNbLost[Offline]");
                b.AddUInt32("Stats", $"{gameplayPath}.m_uNbLost[Online]");
                b.AddUInt32("Stats", $"{gameplayPath}.m_uMedals");
            }
        }
    }

    private static void AddTrophies(SchemaBuilder b)
    {
        b.AddUInt32("Trophies", "m_oTrophies.m_oTrophiesUnlocked.m_uChunkShift");
        int chunksBase = b.Offset;

        for (int chunk = 0; chunk < 4; chunk++)
        {
            b.AddUInt32("Trophies", $"m_oTrophies.m_oTrophiesUnlocked.m_xChunks[{chunk}]");
        }

        for (int trophy = 0; trophy < 128; trophy++)
        {
            int chunk = trophy / 32;
            int bit = trophy % 32;
            string trophyName = trophy < TrophyNames.Length ? TrophyNames[trophy] : $"Unused_{trophy}";
            b.AddBit("Trophies", $"m_oTrophies.Unlocked[{trophyName}]", chunksBase + (chunk * 4), bit, BoolLabels);
        }
    }

    private static void AddCareer(SchemaBuilder b)
    {
        b.AddInt32("Career", "m_oCareerData.m_iCredits");
        b.AddUInt32("Career", "m_oCareerData.m_oCurrentEvent.m_iCurrentEventIndex");
        b.AddUInt32("Career", "m_oCareerData.m_oCurrentEvent.m_iCurrentRound");

        for (int driver = 0; driver < 32; driver++)
        {
            string path = $"m_oCareerData.m_oCurrentEvent.m_vDriversScore[{driver}]";
            b.AddInt32("Career", $"{path}.m_iIndex");
            b.AddInt32("Career", $"{path}.m_iScore");
            b.AddBool8("Career", $"{path}.m_bPlayer");
            b.Align(4);
            b.AddInt32("Career", $"{path}.m_oName");
        }

        for (int reward = 0; reward < 150; reward++)
        {
            string path = $"m_oCareerData.m_vEventRewards[{reward}]";
            b.AddUInt32("Career", $"{path}.m_oId");
            b.AddInt32("Career", $"{path}.m_oScore", ScoreObjectiveLabels);
            b.AddBool8("Career", $"{path}.m_bNew");
            b.AddBool8("Career", $"{path}.m_bPoped");
            b.Align(4);
        }
    }

    private static void AddGarageData(SchemaBuilder b, string section, string prefix)
    {
        for (int car = 0; car < 64; car++)
        {
            string path = $"{prefix}.m_vCarsState[{car}]";
            AddUnlockableItem(b, section, $"{path}.m_oUnlockableItem");

            for (int upgrade = 0; upgrade < UpgradeCategoryNames.Length; upgrade++)
            {
                b.AddInt32(section, $"{path}.m_iUpgradeLevel[{UpgradeCategoryNames[upgrade]}]");
            }
        }

        for (int skin = 0; skin < 512; skin++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vCarSkinsState[{skin}]");
        }

        for (int driver = 0; driver < 64; driver++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vDriversState[{driver}]");
        }

        for (int boost = 0; boost < 64; boost++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vBoostFxState[{boost}]");
        }

        for (int horn = 0; horn < 64; horn++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vCarHornState[{horn}]");
        }

        for (int track = 0; track < 64; track++)
        {
            string path = $"{prefix}.m_vTrackState[{track}]";
            b.AddInt32(section, $"{path}.m_iTrackLocaDiff");
            b.AddInt32(section, $"{path}.m_eState", GarageStateLabels);
        }

        for (int car = 0; car < 64; car++)
        {
            b.AddUInt32(section, $"{prefix}.m_vCarSkins[{car}]");
        }
    }

    private static void AddUnlockableItem(SchemaBuilder b, string section, string path)
    {
        b.AddUInt32(section, $"{path}.m_iIndex");
        b.AddInt32(section, $"{path}.m_eState", GarageStateLabels);
    }

    private static void AddGarageConfig(SchemaBuilder b, string section, string prefix)
    {
        b.AddUInt32(section, $"{prefix}.m_uCarType");
        b.AddUInt32(section, $"{prefix}.m_uDriverType");
        b.AddUInt32(section, $"{prefix}.m_uCarSkin");
        b.AddUInt32(section, $"{prefix}.m_uHornType");
        b.AddUInt32(section, $"{prefix}.m_uBoostFXType");
    }

    private static void AddChallengeMode(SchemaBuilder b)
    {
        b.AddInt32("Challenge", "m_oChallengeModeData.m_iPlayerScore");

        for (int challenge = 0; challenge < 128; challenge++)
        {
            b.AddInt32("Challenge", $"m_oChallengeModeData.m_oChallengesRecords[{challenge}]");
        }

        for (int challenge = 0; challenge < 128; challenge++)
        {
            b.AddBool8("Challenge", $"m_oChallengeModeData.m_oNewChallenges[{challenge}]");
        }
    }

    private static void AddRecords(SchemaBuilder b)
    {
        AddIntArray(b, "Records", "m_oRecords.m_vBestLapTimeTrial", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vBestLapOther", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vStuntsRecords", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vCarnageRecords", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vSurvivorRecords", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vKTFRecords", 64);
        AddIntArray(b, "Records", "m_oRecords.m_vDMRecords", 64);
    }

    private static void AddFavorites(SchemaBuilder b)
    {
        AddUIntArray(b, "Favorites", "m_oFavorites.m_vCarUsed", 64);
        AddUIntArray(b, "Favorites", "m_oFavorites.m_vTrackPlayed", 64);
        AddUIntArray(b, "Favorites", "m_oFavorites.m_vGameplayTypePlayed", 64);
        AddUIntArray(b, "Favorites", "m_oFavorites.m_vStuntsPlayed", 64);
    }

    private static void AddIntArray(SchemaBuilder b, string section, string name, int count)
    {
        for (int i = 0; i < count; i++)
        {
            b.AddInt32(section, $"{name}[{i}]");
        }
    }

    private static void AddUIntArray(SchemaBuilder b, string section, string name, int count)
    {
        for (int i = 0; i < count; i++)
        {
            b.AddUInt32(section, $"{name}[{i}]");
        }
    }

    private sealed class SchemaBuilder
    {
        private readonly List<SaveFieldDefinition> fields = [];

        public IReadOnlyList<SaveFieldDefinition> Fields => fields;

        public int Offset { get; private set; }

        public void Mark(string section, string name)
        {
            _ = section;
            _ = name;
        }

        public void AddFooter(string section, string name) => Add(section, name, SaveFieldKind.Footer, 4);

        public void AddUInt8(string section, string name) => Add(section, name, SaveFieldKind.UInt8, 1);

        public void AddBool8(string section, string name) => Add(section, name, SaveFieldKind.Bool8, 1, BoolLabels);

        public void AddInt32(string section, string name, IReadOnlyDictionary<long, string>? labels = null) => Add(section, name, SaveFieldKind.Int32, 4, labels);

        public void AddUInt32(string section, string name, IReadOnlyDictionary<long, string>? labels = null) => Add(section, name, SaveFieldKind.UInt32, 4, labels);

        public void AddFloat(string section, string name) => Add(section, name, SaveFieldKind.Float32, 4);

        public void AddBit(string section, string name, int offset, int bit, IReadOnlyDictionary<long, string>? labels = null)
        {
            fields.Add(new SaveFieldDefinition(section, name, offset, SaveFieldKind.Bit, 4, bit, labels));
        }

        public void Align(int alignment)
        {
            int remainder = Offset % alignment;
            if (remainder != 0)
            {
                Offset += alignment - remainder;
            }
        }

        public void Skip(int length) => Offset += length;

        private void Add(string section, string name, SaveFieldKind kind, int length, IReadOnlyDictionary<long, string>? labels = null)
        {
            fields.Add(new SaveFieldDefinition(section, name, Offset, kind, length, null, labels));
            Offset += length;
        }
    }

    private static readonly IReadOnlyDictionary<long, string> BoolLabels = new Dictionary<long, string>
    {
        [0] = "false",
        [1] = "true"
    };

    private static readonly IReadOnlyDictionary<long, string> OnOffLabels = new Dictionary<long, string>
    {
        [0] = "OFF",
        [1] = "ON"
    };

    private static readonly IReadOnlyDictionary<long, string> SpeedUnitLabels = new Dictionary<long, string>
    {
        [0] = "MPH",
        [1] = "KPH"
    };

    private static readonly IReadOnlyDictionary<long, string> DifficultyLabels = new Dictionary<long, string>
    {
        [0] = "Easy",
        [1] = "Medium",
        [2] = "Hard"
    };

    private static readonly IReadOnlyDictionary<long, string> DominantHandLabels = new Dictionary<long, string>
    {
        [0] = "Left",
        [1] = "Right"
    };

    private static readonly IReadOnlyDictionary<long, string> ComfortVignetteLabels = new Dictionary<long, string>
    {
        [0] = "Off",
        [1] = "Low",
        [2] = "Medium",
        [3] = "High"
    };

    private static readonly IReadOnlyDictionary<long, string> InputPrimaryFamilyLabels = new Dictionary<long, string>
    {
        [0] = "Auto",
        [1] = "Wheel",
        [2] = "Gamepad",
        [3] = "Keyboard",
        [4] = "VR Motion",
        [5] = "VR Joystick"
    };

    private static readonly IReadOnlyDictionary<long, string> ScoreObjectiveLabels = new Dictionary<long, string>
    {
        [0] = "Bronze",
        [1] = "Silver",
        [2] = "Gold"
    };

    private static readonly IReadOnlyDictionary<long, string> GarageStateLabels = new Dictionary<long, string>
    {
        [0] = "Locked",
        [1] = "New",
        [2] = "Unlocked",
        [3] = "Owned",
        [4] = "DLC Locked",
        [5] = "DLC Owned"
    };

    private static readonly string[] BindingDeviceNames =
    [
        "Keyboard",
        "Pad",
        "SteeringWheel"
    ];

    private static readonly string[] InputActionNames =
    [
        "ACTION_RACE_ACCELERATE",
        "ACTION_RACE_BRAKE",
        "ACTION_RACE_LEAN_LEFT",
        "ACTION_RACE_LEAN_RIGHT",
        "ACTION_RACE_LEAN",
        "ACTION_RACE_TOGGLE_VIEW",
        "ACTION_RACE_TOGGLE_VIEW_PREV",
        "ACTION_RACE_REAR_VIEW",
        "ACTION_RACE_LEFT_VIEW",
        "ACTION_RACE_RIGHT_VIEW",
        "ACTION_RACE_RESPAWN",
        "ACTION_RACE_BOOST",
        "ACTION_RACE_SKID",
        "ACTION_RACE_LOOK_BACK",
        "ACTION_RACE_HAND_BRAKE",
        "ACTION_RACE_GEAR_SHIFT_UP",
        "ACTION_RACE_GEAR_SHIFT_DOWN",
        "ACTION_RACE_GEAR_1",
        "ACTION_RACE_GEAR_2",
        "ACTION_RACE_GEAR_3",
        "ACTION_RACE_GEAR_4",
        "ACTION_RACE_GEAR_5",
        "ACTION_RACE_GEAR_6",
        "ACTION_RACE_GEAR_7",
        "ACTION_RACE_GEAR_R",
        "ACTION_RACE_TOGGLE_AUTOPILOT",
        "ACTION_RACE_HORN",
        "ACTION_RACE_TOGGLE_GHOST_VIEW",
        "ACTION_RACE_AIR_CONTROL_UP_DOWN",
        "ACTION_RACE_SET_COMMANDED_RESPAWN",
        "ACTION_RACE_COMMANDED_RESPAWN",
        "ACTION_RACE_TOGGLE_WIPERS",
        "ACTION_RACE_TOGGLE_HEADLIGHTS",
        "ACTION_RACE_CLUTCH",
        "FO_ACTION_START_RACE",
        "FO_DEBUG_TOGGLE_HUD",
        "FO_ACTION_NEXT_SPECTATE_CAM",
        "FO_ACTION_PREV_SPECTATE_CAM",
        "FO_ACTION_STUNT_EJECT",
        "FO_ACTION_STUNT_EJECT_JUST",
        "FO_ACTION_TRAP_NEXT",
        "FO_ACTION_TRAP_PREV",
        "FO_ACTION_TRAP_CREATE",
        "FO_ACTION_TRAP_CREATE_BEHIND",
        "FO_ACTION_TRAP_LAUNCH_A",
        "FO_ACTION_TRAP_LAUNCH_B",
        "FO_ACTION_TRAP_LAUNCH_C",
        "FO_ACTION_TRAP_LAUNCH_D",
        "FO_ACTION_TRAP_LAUNCH_UNSELECTED_A",
        "FO_ACTION_TRAP_LAUNCH_UNSELECTED_B",
        "FO_ACTION_TRAP_LAUNCH_UNSELECTED_C",
        "FO_ACTION_TRAP_LAUNCH_UNSELECTED_D",
        "FO_ACTION_TRAP_SELECT_LAUNCH",
        "FO_ACTION_HUD_SWITCH_PLAYERLIST",
        "FO_ACTION_ROTATE_CAMERA",
        "FO_DEBUG_SWITCH_DAYTIME_UP",
        "FO_DEBUG_SWITCH_DAYTIME_DOWN",
        "FO_ACTION_SPECTATE_GAMERTAG",
        "FO_ACTION_SKIP_MUSIC",
        "FO_ACTION_VR_HMD_RECENTER"
    ];

    private static readonly string[] ControllerSettingNames =
    [
        "ACC_SENSITIVITY",
        "ACC_DEADZONE",
        "BRAKE_SENSITIVITY",
        "BRAKE_DEADZONE",
        "STEERING_SENSITIVITY",
        "STEERING_DEADZONE",
        "COUNTER_STEER_SENSITIVITY",
        "VIBRATIONS_ENABLED",
        "VIBRATIONS_LEVEL",
        "FFB_LEVEL",
        "FFB_VIBRATION"
    ];

    private static readonly string[] AudioVolumeNames =
    [
        "Master",
        "Ambiance",
        "Vehicle",
        "Voice",
        "Music"
    ];

    private static readonly string[] MenuBindingNames =
    [
        "Validate",
        "Cancel",
        "Secondary",
        "NextPage",
        "PreviousPage",
        "PopMenu",
        "Up",
        "Down",
        "Left",
        "Right",
        "ViewRecenter"
    ];

    private static readonly string[] ScoreObjectiveNames =
    [
        "Bronze",
        "Silver",
        "Gold"
    ];

    private static readonly string[] GameModeNames =
    [
        "CAREER",
        "CHALLENGE",
        "PARTY_GAME",
        "ONLINE",
        "QUICK_EVENTS",
        "ONLINE_IN_LOBBY",
        "_None_"
    ];

    private static readonly string[] GamePlayTypeNames =
    [
        "RACE_TIMED",
        "RACE_LAP",
        "RACE_TRAP",
        "RACE_DERBY",
        "RACE_BOMB",
        "ARENA_SURVIVOR",
        "ARENA_DEATH_MATCH",
        "ARENA_KEEP_THE_FLAG",
        "STUNT",
        "ALL",
        "_None_"
    ];

    private static readonly string[] UpgradeCategoryNames =
    [
        "Motor",
        "Gearbox",
        "Exhaust",
        "Nitro",
        "WheelsBrakes",
        "Chassis"
    ];

    private static readonly string[] TrophyNames =
    [
        "FO_TROPHY_COLLECTOR",
        "FO_TROPHY_ON_PODIUM",
        "FO_TROPHY_MONEY_MONEY",
        "FO_TROPHY_DESTROYER",
        "FO_TROPHY_PAINT_SHOP",
        "FO_TROPHY_FLATOUT_WAY",
        "FO_TROPHY_NO_SPEED_LIMIT",
        "FO_TROPHY_ROAD_TO_GLORY",
        "FO_TROPHY_LIGHTNING_MASTER",
        "FO_TROPHY_STUNTMAN_NEWBORN",
        "FO_TROPHY_STUNTMAN_MASTER",
        "FO_TROPHY_SPEED_JUNKIE",
        "FO_TROPHY_WELCOME_TO_THE_WORLD",
        "FO_TROPHY_BACHELOR_OF_ONLINE",
        "FO_TROPHY_ONLINE_MASTER",
        "FO_TROPHY_HOT_ROD",
        "FO_TROPHY_JAY_WILL_BE_PROUD",
        "FO_TROPHY_GOLD_CRUSHER",
        "FO_TROPHY_SO_SORRY",
        "FO_TROPHY_LOVE_IT",
        "FO_TROPHY_SWEETHEART",
        "FO_TROPHY_ANTI_SOCIAL",
        "FO_TROPHY_READY_FOR_JUNKYARD",
        "FO_TROPHY_HAMSTER_KING",
        "FO_TROPHY_BABY_HAMSTER",
        "FO_TROPHY_WRECK_FEST",
        "FO_TROPHY_FLAG_THIEF",
        "FO_TROPHY_THE_COYOTE",
        "FO_TROPHY_JUNKYARD_ORGY",
        "FO_TROPHY_CATCH_THE_ROAD_RUNNER",
        "FO_TROPHY_LETS_PLAY_A_GAME",
        "FO_TROPHY_TRAP_ASTIC",
        "FO_TROPHY_ROAD_ATTACK",
        "FO_TROPHY_SMOKIN",
        "FO_TROPHY_UNDERCOVER",
        "FO_TROPHY_CLASSES_WINNER",
        "FO_TROPHY_THAT_CAME_AS_A_SURPRISE",
        "FO_TROPHY_RELOAD",
        "FO_TROPHY_CRASH_FOREST_CRASH",
        "FO_TROPHY_AIRFORCE_ONE",
        "FO_TROPHY_ITS_JUST_THE_BEGINNING",
        "FO_TROPHY_RICH_BRAT",
        "FO_TROPHY_GENIUS_OR_LUCKY",
        "FO_TROPHY_DEVIL_IN_66_SECONDS",
        "FO_TROPHY_SURVIMINATOR"
    ];
}
