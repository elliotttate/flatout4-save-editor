using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

public sealed class FlatOut4SaveSchema
{
    public const uint FooterValue = 0x204F4F46; // "FOO " in little-endian saves
    public const uint CurrentVersion = 95;
    // RC10-RC17 appended a telemetry opt-in to the gameplay options and wrote V96.
    // The game reads those back into the shared V95 layout and never writes 96 again.
    public const uint RcTelemetryVersion = 96;
    public const uint LatestReadableVersion = RcTelemetryVersion;
    public const uint EarliestSupportedVersion = 82;
    public const uint ExtensionMagicValue = 0x31545846; // "FXT1" in little-endian saves
    public const int ExtensionSlotWorkshopId = 0;
    public const int ExtensionSlotMagic = 1;
    public const int ExtensionSlotTelemetry = 2;
    public const int OtherSlotCount = 1024;
    public const int MedalNone = 3; // M_COUNT: an event reward slot with no medal
    public const int GarageStateUnused = 6; // E_GARAGE_STATE_COUNT: an empty garage slot
    public const int DriverNone = 30; // FODriverName::_None_
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
    public const int V96GameplayOptionsSize = CurrentGameplayOptionsSize + UInt32Size;
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

    private FlatOut4SaveSchema(IReadOnlyList<SaveFieldDefinition> fields, int serializableSize, int otherSlotsOffset)
    {
        Fields = fields;
        SerializableSize = serializableSize;
        OtherSlotsOffset = otherSlotsOffset;
    }

    public IReadOnlyList<SaveFieldDefinition> Fields { get; }

    public int SerializableSize { get; }

    // Offset of m_aOther[0] (the Workshop ID slot) inside the serialized save.
    public int OtherSlotsOffset { get; }

    public int ExtensionSlotOffset(int slot) => OtherSlotsOffset + (slot * UInt32Size);

    public static FlatOut4SaveSchema Create()
    {
        var b = new SchemaBuilder();

        b.AddFooter("Header", "m_oFooterHead");
        b.AddUInt32("Header", "m_uSaveVersion");
        b.AddUInt8("Header", "m_uInputDevice", InputDeviceLabels);
        b.Annotate(label: "Last input device", description: "Device family the profile last played with. The game re-detects this at startup.", preferChoice: true);
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

        int otherSlotsOffset = b.Offset;
        for (int i = 0; i < OtherSlotCount; i++)
        {
            b.AddInt32("Extra", $"m_aOther[{i}]");
            switch (i)
            {
                case ExtensionSlotWorkshopId:
                    b.Annotate(
                        label: "Workshop item ID",
                        description: "Steam Workshop livery the player last picked in the garage (m_aOther[0]). Untagged saves can hold leftover bytes here.",
                        format: SaveFieldFormat.Hex);
                    break;
                case ExtensionSlotMagic:
                    b.Annotate(
                        label: "Extension tag",
                        description: "Holds \"FXT1\" once the game has written tagged extension values. Without it the game ignores slots 1 and up and uses their defaults.",
                        format: SaveFieldFormat.Hex,
                        labels: ExtensionMagicLabels);
                    break;
                case ExtensionSlotTelemetry:
                    b.Annotate(
                        label: "Telemetry slot (raw)",
                        description: "Raw telemetry opt-in value (1 = ON). Only read while the extension tag is set.",
                        format: SaveFieldFormat.Hex);
                    break;
            }
        }

        for (int i = 0; i < 31; i++)
        {
            b.AddInt32("Padding", $"m_vPadding[{i}]");
        }

        b.AddFooter("Footer", "m_oFooterTail");

        b.AddVirtual(new SaveFieldDefinition(
            "Options",
            "m_aOther.TelemetryOutput",
            otherSlotsOffset + (ExtensionSlotTelemetry * UInt32Size),
            SaveFieldKind.ExtensionOnOff,
            UInt32Size,
            null,
            OnOffLabels)
        {
            AuxOffset = otherSlotsOffset + (ExtensionSlotMagic * UInt32Size),
            Label = "Telemetry output",
            Description = "Motion-rig / dashboard telemetry opt-in. Turning it on or off tags the extension slots so the game trusts the value; untagged older profiles behave as ON."
        });

        return new FlatOut4SaveSchema(b.Fields, b.Offset, otherSlotsOffset);
    }

    private static void AddGameOptions(SchemaBuilder b)
    {
        const string gameplay = "m_oGameOptions.m_oGameplayOptions";

        b.AddFloat("Options", $"{gameplay}.m_fVibrationLevel");
        b.Annotate(label: "Vibration level", description: "Controller rumble strength.", min: 0, max: 100, unit: "%");
        b.AddUInt32("Options", $"{gameplay}.m_uCamXInversed", OnOffLabels);
        b.Annotate(label: "Invert camera X");
        b.AddUInt32("Options", $"{gameplay}.m_uCamYInversed", OnOffLabels);
        b.Annotate(label: "Invert camera Y");
        b.AddUInt32("Options", $"{gameplay}.m_uSpeedUnit", SpeedUnitLabels);
        b.Annotate(label: "Speed unit", preferChoice: true);
        b.AddUInt32("Options", $"{gameplay}.m_uDifficulty", DifficultyLabels);
        b.Annotate(label: "AI difficulty", description: "Scales AI pace on top of each event's own tuning: Easy 0.85x, Medium 1.0x, Hard 1.2x.");
        b.AddFloat("Options", $"{gameplay}.m_vInteriorCamOffset.x");
        b.Annotate(label: "Interior camera offset X", description: "50 is the default seat position.", min: 0, max: 100);
        b.AddFloat("Options", $"{gameplay}.m_vInteriorCamOffset.y");
        b.Annotate(label: "Interior camera offset Y", description: "50 is the default seat position.", min: 0, max: 100);
        b.AddFloat("Options", $"{gameplay}.m_vInteriorCamOffset.z");
        b.Annotate(label: "Interior camera offset Z", description: "50 is the default seat position.", min: 0, max: 100);
        b.AddUInt32("Options", $"{gameplay}.m_bShowDriverInInterior", OnOffLabels);
        b.Annotate(label: "Show driver in interior view");
        b.AddUInt32("Options", $"{gameplay}.m_uFFBStrength");
        b.Annotate(label: "Force-feedback strength", description: "100 is the wheel profile as authored; direct-drive users can go higher.", min: 0, max: 200, unit: "%");
        b.AddUInt32("Options", $"{gameplay}.m_uWheelRotationDegrees");
        b.Annotate(label: "Wheel rotation", description: "Lock-to-lock rotation the wheel driver should use. 0 = automatic (engine default).", min: 0, max: 1080, unit: "°");
        b.AddUInt32("Options", $"{gameplay}.m_uPedalSwapOverride", OnOffLabels);
        b.Annotate(label: "Override pedal swap", description: "Off uses the per-wheel default; on uses the pedal-swap setting below.");
        b.AddUInt32("Options", $"{gameplay}.m_uPedalSwapEnabled", OnOffLabels);
        b.Annotate(label: "Swap pedals", description: "Only used while the override is on.");
        b.AddUInt32("Options", $"{gameplay}.m_uManualShift", OnOffLabels);
        b.Annotate(label: "Manual gearbox", description: "Off = automatic (paddles still override). On = the player shifts every gear.");
        b.AddUInt32("Options", $"{gameplay}.m_uManualShiftRequireClutch", OnOffLabels);
        b.Annotate(label: "Require clutch for paddle shifts", description: "Paddle shifts only register with the clutch at least half pressed.");
        b.AddUInt32("Options", $"{gameplay}.m_uAutoEngageManualShiftOnHShifter", OnOffLabels);
        b.Annotate(label: "Manual gearbox with H-shifter", description: "Switches to manual automatically when an H-pattern shifter with gear bindings is connected.");
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionSteeringEnabled", OnOffLabels);
        b.Annotate(label: "VR motion steering", description: "Hold both controllers like a wheel; the line between your hands steers.");
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionLockToLockDeg");
        b.Annotate(label: "Lock-to-lock", description: "Virtual wheel travel from full left to full right.", min: 10, max: 1080, unit: "°");
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionDeadzoneDeg");
        b.Annotate(label: "Deadzone", description: "Dead angle around the captured centre.", min: 0, max: 30, unit: "°");
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionSensitivity");
        b.Annotate(label: "Sensitivity", description: "100 is the nominal lock-to-lock mapping.", min: 5, max: 200);
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionAutoRecenterIdle", OnOffLabels);
        b.Annotate(label: "Auto-recenter while idle", description: "Re-zeroes the wheel after holding near centre for a few seconds.");
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionDominantHand", DominantHandLabels);
        b.Annotate(label: "Dominant hand", description: "Hand used for single-hand steering.", preferChoice: true);
        b.AddUInt32("Options", $"{gameplay}.m_uVRMotionEnableHaptic", OnOffLabels);
        b.Annotate(label: "Controller haptics", description: "Lock-stop and collision pulses on the controllers.");
        b.AddUInt32("Options", $"{gameplay}.m_uDisableCameraShake", OnOffLabels);
        b.Annotate(label: "Disable camera shake", description: "Removes collision, explosion and boost camera shake. Rumble is unaffected.");
        b.AddUInt32("Options", $"{gameplay}.m_uComfortVignette", ComfortVignetteLabels);
        b.Annotate(label: "Comfort vignette", description: "Darkens the view edges during sustained camera rotation.");
        b.AddUInt32("Options", $"{gameplay}.m_uInputPrimaryFamily", InputPrimaryFamilyLabels);
        b.Annotate(label: "Primary input", description: "Which device family drives the car. Auto is the legacy value and is resolved once by the game.");

        for (int device = 0; device < BindingDeviceNames.Length; device++)
        {
            for (int action = 0; action < InputActionNames.Length; action++)
            {
                b.AddUInt32("Input", $"m_oGameOptions.m_uUserBindings[{BindingDeviceNames[device]}][{InputActionNames[action]}]", BindingLabels[device]);
                b.Annotate(format: SaveFieldFormat.ControlId, preferChoice: true, sortByLabel: device == 0);
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
            b.Annotate(label: $"{AudioVolumeNames[i]} volume", min: 0, max: 100, unit: "%");
        }

        for (int device = 0; device < BindingDeviceNames.Length; device++)
        {
            for (int action = 0; action < MenuBindingNames.Length; action++)
            {
                b.AddUInt32("Input", $"m_oGameOptions.m_uMenuBindings[{BindingDeviceNames[device]}][{MenuBindingNames[action]}]", BindingLabels[device]);
                b.Annotate(format: SaveFieldFormat.ControlId, preferChoice: true, sortByLabel: device == 0);
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
        b.Annotate(format: SaveFieldFormat.Hex);
        b.AddUInt32("Stats", "m_oStats.m_uCupClassesWon");
        b.Annotate(format: SaveFieldFormat.Hex);

        for (int medal = 0; medal < ScoreObjectiveNames.Length; medal++)
        {
            b.AddUInt32("Stats", $"m_oStats.m_uSuntsMedals[{ScoreObjectiveNames[medal]}]");
            b.Annotate(label: $"{ScoreObjectiveNames[medal]} stunt medals");
        }

        b.AddUInt32("Stats", "m_oStats.m_uNitroLongestBurnDuration");
        b.AddFloat("Stats", "m_oStats.m_fNitroBurnDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
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
        b.Annotate(description: "In miles. Crash Forest, Crash! unlocks at 500.", unit: "mi");
        b.AddFloat("Stats", "m_oStats.m_uMaxHorizontalDistanceCoveredWithAJump");
        b.Annotate(description: "In miles. AirForce One unlocks at 0.06 (about 100 m).", unit: "mi");
        b.AddFloat("Stats", "m_oStats.m_fTotalHorizontalJumpDistance");
        b.AddFloat("Stats", "m_oStats.m_fTotalJumpDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddUInt32("Stats", "m_oStats.m_uMaxFlagCaptureDuration");
        b.AddFloat("Stats", "m_oStats.m_fTotalFlagCaptureDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fTotalSurvivorDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fTotalEliminatorDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fAccelerationDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fBrakeDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddUInt32("Stats", "m_oStats.m_uNbGamesPlayed");
        b.AddUInt32("Stats", "m_oStats.m_uNbStuntsPlayed");
        b.AddUInt32("Stats", "m_oStats.m_uNbTrapsPlaced");
        b.AddFloat("Stats", "m_oStats.m_fPauseDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fOnlineDuration");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddFloat("Stats", "m_oStats.m_fDistanceTraveled");
        b.AddFloat("Stats", "m_oStats.m_fDistanceTraveledBackward");
        b.AddFloat("Stats", "m_oStats.m_fPlayTime");
        b.Annotate(format: SaveFieldFormat.Seconds);
        b.AddUInt32("Stats", "m_oStats.m_uNbRecentTrophies");
        b.Annotate(min: 0, max: 4);

        for (int i = 0; i < 4; i++)
        {
            b.AddUInt32("Stats", $"m_oStats.m_vRecentTrophies[{i}]", TrophyLabels);
            b.Annotate(label: $"Recent trophy {i + 1}", preferChoice: true);
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
                b.Annotate(format: SaveFieldFormat.Hex);
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
            b.Annotate(format: SaveFieldFormat.Hex);
        }

        for (int trophy = 0; trophy < 128; trophy++)
        {
            int chunk = trophy / 32;
            int bit = trophy % 32;
            string trophyName = trophy < TrophyNames.Length ? TrophyNames[trophy] : $"Unused_{trophy}";
            b.AddBit("Trophies", $"m_oTrophies.Unlocked[{trophyName}]", chunksBase + (chunk * 4), bit, BoolLabels);
            if (trophy < TrophyNames.Length)
            {
                FlatOut4GameData.TrophyInfo info = FlatOut4GameData.Trophies[trophy];
                b.Annotate(label: info.Title, description: info.Description);
            }
        }
    }

    private static void AddCareer(SchemaBuilder b)
    {
        b.AddInt32("Career", "m_oCareerData.m_iCredits");
        b.Annotate(label: "Credits", description: "Career wallet.");
        b.AddUInt32("Career", "m_oCareerData.m_oCurrentEvent.m_iCurrentEventIndex", FlatOut4GameData.CareerEventLabels);
        b.Annotate(label: "Event in progress", description: "Hashed event index of the cup being played; 0 when no cup is in progress.", format: SaveFieldFormat.Hex);
        b.AddUInt32("Career", "m_oCareerData.m_oCurrentEvent.m_iCurrentRound");
        b.Annotate(label: "Current round");

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
            b.AddUInt32("Career", $"{path}.m_oId", FlatOut4GameData.CareerEventLabels);
            b.Annotate(format: SaveFieldFormat.Hex);
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
            b.AddUInt32(section, $"{path}.m_oUnlockableItem.m_iIndex", FlatOut4GameData.CarTypeLabels);
            b.Annotate(label: "Car", preferChoice: true);
            b.AddInt32(section, $"{path}.m_oUnlockableItem.m_eState", GarageStateLabels);

            for (int upgrade = 0; upgrade < UpgradeCategoryNames.Length; upgrade++)
            {
                b.AddInt32(section, $"{path}.m_iUpgradeLevel[{UpgradeCategoryNames[upgrade]}]", UpgradeLevelLabels);
            }
        }

        for (int skin = 0; skin < 512; skin++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vCarSkinsState[{skin}]", FlatOut4CustomizationNames.CarSkins, hexIndex: true);
        }

        for (int driver = 0; driver < 64; driver++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vDriversState[{driver}]", FlatOut4CustomizationNames.Drivers, hexIndex: false);
        }

        for (int boost = 0; boost < 64; boost++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vBoostFxState[{boost}]", FlatOut4CustomizationNames.BoostEffects, hexIndex: true);
        }

        for (int horn = 0; horn < 64; horn++)
        {
            AddUnlockableItem(b, section, $"{prefix}.m_vCarHornState[{horn}]", FlatOut4CustomizationNames.Horns, hexIndex: true);
        }

        for (int track = 0; track < 64; track++)
        {
            string path = $"{prefix}.m_vTrackState[{track}]";
            b.AddInt32(section, $"{path}.m_iTrackLocaDiff", FlatOut4GameData.TrackLabels);
            b.Annotate(label: "Track", preferChoice: true);
            b.AddInt32(section, $"{path}.m_eState", GarageStateLabels);
        }

        for (int car = 0; car < 64; car++)
        {
            b.AddUInt32(section, $"{prefix}.m_vCarSkins[{car}]", SkinChoiceLabels);
            b.Annotate(format: SaveFieldFormat.Hex, preferChoice: true, sortByLabel: true);
        }
    }

    private static void AddUnlockableItem(SchemaBuilder b, string section, string path, IReadOnlyDictionary<long, string> names, bool hexIndex)
    {
        b.AddUInt32(section, $"{path}.m_iIndex", names);
        if (hexIndex)
        {
            b.Annotate(format: SaveFieldFormat.Hex);
        }

        b.AddInt32(section, $"{path}.m_eState", GarageStateLabels);
    }

    private static void AddGarageConfig(SchemaBuilder b, string section, string prefix)
    {
        b.AddUInt32(section, $"{prefix}.m_uCarType", FlatOut4GameData.CarTypeLabels);
        b.Annotate(preferChoice: true);
        b.AddUInt32(section, $"{prefix}.m_uDriverType", FlatOut4CustomizationNames.Drivers);
        b.Annotate(preferChoice: true);
        b.AddUInt32(section, $"{prefix}.m_uCarSkin", SkinChoiceLabels);
        b.Annotate(format: SaveFieldFormat.Hex, preferChoice: true, sortByLabel: true);
        b.AddUInt32(section, $"{prefix}.m_uHornType", WithNone(FlatOut4CustomizationNames.Horns));
        b.Annotate(preferChoice: true);
        b.AddUInt32(section, $"{prefix}.m_uBoostFXType", WithNone(FlatOut4CustomizationNames.BoostEffects));
        b.Annotate(preferChoice: true);
    }

    private static void AddChallengeMode(SchemaBuilder b)
    {
        b.AddInt32("Challenge", "m_oChallengeModeData.m_iPlayerScore");
        b.Annotate(label: "Total challenge score");

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
        AddIntArray(b, "Records", "m_oRecords.m_vBestLapTimeTrial", 64, NoRecordLabels, SaveFieldFormat.Milliseconds);
        AddIntArray(b, "Records", "m_oRecords.m_vBestLapOther", 64, NoRecordLabels, SaveFieldFormat.Milliseconds);
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

    private static void AddIntArray(SchemaBuilder b, string section, string name, int count, IReadOnlyDictionary<long, string>? labels = null, SaveFieldFormat format = SaveFieldFormat.Default)
    {
        for (int i = 0; i < count; i++)
        {
            b.AddInt32(section, $"{name}[{i}]", labels);
            b.Annotate(format: format);
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

        public void AddFooter(string section, string name) => Add(section, name, SaveFieldKind.Footer, 4);

        public void AddUInt8(string section, string name, IReadOnlyDictionary<long, string>? labels = null) => Add(section, name, SaveFieldKind.UInt8, 1, labels);

        public void AddBool8(string section, string name) => Add(section, name, SaveFieldKind.Bool8, 1, BoolLabels);

        public void AddInt32(string section, string name, IReadOnlyDictionary<long, string>? labels = null) => Add(section, name, SaveFieldKind.Int32, 4, labels);

        public void AddUInt32(string section, string name, IReadOnlyDictionary<long, string>? labels = null) => Add(section, name, SaveFieldKind.UInt32, 4, labels);

        public void AddFloat(string section, string name) => Add(section, name, SaveFieldKind.Float32, 4);

        public void AddBit(string section, string name, int offset, int bit, IReadOnlyDictionary<long, string>? labels = null)
        {
            fields.Add(new SaveFieldDefinition(section, name, offset, SaveFieldKind.Bit, 4, bit, labels));
        }

        // A view onto bytes other fields already cover; does not advance the layout.
        public void AddVirtual(SaveFieldDefinition field) => fields.Add(field);

        public void Annotate(
            string? label = null,
            string? description = null,
            double? min = null,
            double? max = null,
            string? unit = null,
            SaveFieldFormat? format = null,
            IReadOnlyDictionary<long, string>? labels = null,
            bool? preferChoice = null,
            bool? sortByLabel = null)
        {
            SaveFieldDefinition last = fields[^1];
            fields[^1] = last with
            {
                Label = label ?? last.Label,
                Description = description ?? last.Description,
                Minimum = min ?? last.Minimum,
                Maximum = max ?? last.Maximum,
                Unit = unit ?? last.Unit,
                Format = format ?? last.Format,
                ValueLabels = labels ?? last.ValueLabels,
                PreferChoice = preferChoice ?? last.PreferChoice,
                SortOptionsByLabel = sortByLabel ?? last.SortOptionsByLabel
            };
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

    internal static readonly IReadOnlyDictionary<long, string> BoolLabels = new Dictionary<long, string>
    {
        [0] = "false",
        [1] = "true"
    };

    internal static readonly IReadOnlyDictionary<long, string> OnOffLabels = new Dictionary<long, string>
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

    // PlayAll::Input::EnumDeviceFamily
    private static readonly IReadOnlyDictionary<long, string> InputDeviceLabels = new Dictionary<long, string>
    {
        [0] = "None",
        [1] = "Keyboard",
        [2] = "Pointer",
        [4] = "Gamepad",
        [32] = "Steering wheel",
        [64] = "Steering wheel shifter",
        [128] = "Steering wheel add-on"
    };

    private static readonly IReadOnlyDictionary<long, string> ExtensionMagicLabels = new Dictionary<long, string>
    {
        [unchecked((int)ExtensionMagicValue)] = "FXT1 (tagged)"
    };

    // WRC_INVALID_CONTROL_ID / FO_MENU_BIND_CLEARED
    private static readonly IReadOnlyDictionary<long, string> BindingSentinelLabels = new Dictionary<long, string>
    {
        [0xFFFFFFFF] = "Default",
        [0xFFFFFFFE] = "Cleared"
    };

    // Per BindingDeviceNames entry: the sentinels plus every control the device can bind.
    private static readonly IReadOnlyDictionary<long, string>[] BindingLabels =
    [
        WithSentinels(FlatOut4ControlNames.Keyboard),
        WithSentinels(FlatOut4ControlNames.Gamepad),
        WithSentinels(FlatOut4ControlNames.SteeringWheel)
    ];

    private static IReadOnlyDictionary<long, string> WithSentinels(IReadOnlyDictionary<long, string> controls)
    {
        var labels = new Dictionary<long, string>(BindingSentinelLabels);
        foreach ((long id, string name) in controls)
        {
            labels[id] = name;
        }

        return labels;
    }

    // Skin hash or 0 for "use the car's current paint".
    private static readonly IReadOnlyDictionary<long, string> SkinChoiceLabels = WithNone(FlatOut4CustomizationNames.CarSkins, "Default paint");

    private static IReadOnlyDictionary<long, string> WithNone(IReadOnlyDictionary<long, string> names, string none = "None")
    {
        var labels = new Dictionary<long, string> { [0] = none };
        foreach ((long id, string name) in names)
        {
            labels[id] = name;
        }

        return labels;
    }

    // FOGarageData: -1 = nothing bought; Derby cars have 3 levels, Classic 4, All-Stars 5.
    private static readonly IReadOnlyDictionary<long, string> UpgradeLevelLabels = new Dictionary<long, string>
    {
        [-1] = "Stock",
        [0] = "1",
        [1] = "2",
        [2] = "3",
        [3] = "4",
        [4] = "5"
    };

    internal static readonly IReadOnlyDictionary<long, string> ScoreObjectiveLabels = new Dictionary<long, string>
    {
        [0] = "Bronze",
        [1] = "Silver",
        [2] = "Gold",
        [3] = "None"
    };

    private static readonly IReadOnlyDictionary<long, string> NoRecordLabels = new Dictionary<long, string>
    {
        [-1] = "No record"
    };

    internal static readonly IReadOnlyDictionary<long, string> GarageStateLabels = new Dictionary<long, string>
    {
        [0] = "Locked",
        [1] = "New",
        [2] = "Unlocked",
        [3] = "Owned",
        [4] = "DLC Locked",
        [5] = "DLC Owned",
        [6] = "Unused slot"
    };

    private static readonly IReadOnlyDictionary<long, string> TrophyLabels = BuildTrophyLabels();

    private static IReadOnlyDictionary<long, string> BuildTrophyLabels()
    {
        var labels = new Dictionary<long, string>();
        for (int i = 0; i < FlatOut4GameData.Trophies.Count; i++)
        {
            labels[i] = FlatOut4GameData.Trophies[i].Title;
        }

        return labels;
    }

    internal static readonly string[] BindingDeviceNames =
    [
        "Keyboard",
        "Pad",
        "SteeringWheel"
    ];

    internal static readonly string[] InputActionNames =
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

    internal static readonly string[] ControllerSettingNames =
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

    internal static readonly string[] AudioVolumeNames =
    [
        "Master",
        "Ambiance",
        "Vehicle",
        "Voice",
        "Music"
    ];

    internal static readonly string[] MenuBindingNames =
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

    internal static readonly string[] ScoreObjectiveNames =
    [
        "Bronze",
        "Silver",
        "Gold"
    ];

    internal static readonly string[] GameModeNames =
    [
        "CAREER",
        "CHALLENGE",
        "PARTY_GAME",
        "ONLINE",
        "QUICK_EVENTS",
        "ONLINE_IN_LOBBY",
        "_None_"
    ];

    internal static readonly string[] GamePlayTypeNames =
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

    internal static readonly string[] UpgradeCategoryNames =
    [
        "Motor",
        "Gearbox",
        "Exhaust",
        "Nitro",
        "WheelsBrakes",
        "Chassis"
    ];

    internal static readonly string[] TrophyNames =
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
