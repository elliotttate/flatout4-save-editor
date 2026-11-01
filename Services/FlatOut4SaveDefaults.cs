using System.Text.RegularExpressions;
using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

// What a freshly reset profile holds: FOSaveData::Reset and each component's Reset(), with the
// values the game fills in at runtime replaced by their fallbacks. Used to fill the parts of a
// damaged save that are missing, and as the value a health-check repair writes.
public static partial class FlatOut4SaveDefaults
{
    // GamePlayFactors.cfg CareerCredits (FOCareerDataStruct::Reset reads it through FORaceConfigManager).
    public const int StartingCredits = 10000;

    // Gameplay options (FOGameplayOptions::Reset). The primary input family stays Auto: the game
    // resolves Auto once on load, which is what a recovered profile needs.
    private static readonly Dictionary<string, double> GameplayDefaults = new(StringComparer.Ordinal)
    {
        ["m_fVibrationLevel"] = 100,
        ["m_uDifficulty"] = 1,
        ["m_vInteriorCamOffset.x"] = 50,
        ["m_vInteriorCamOffset.y"] = 50,
        ["m_vInteriorCamOffset.z"] = 50,
        ["m_bShowDriverInInterior"] = 1,
        ["m_uFFBStrength"] = 100,
        ["m_uAutoEngageManualShiftOnHShifter"] = 1,
        ["m_uVRMotionLockToLockDeg"] = 130,
        ["m_uVRMotionDeadzoneDeg"] = 20,
        ["m_uVRMotionSensitivity"] = 50,
        ["m_uVRMotionDominantHand"] = 1,
        ["m_uVRMotionEnableHaptic"] = 1
    };

    // A V95 profile as Reset() leaves it, with untouched m_aOther slots (untagged, so telemetry
    // reads as ON, the way the game treats any profile older than the opt-in).
    public static byte[] CreateFreshProfile(FlatOut4SaveSchema schema)
    {
        byte[] bytes = new byte[schema.SerializableSize];
        foreach (SaveFieldDefinition field in schema.Fields)
        {
            if (field.Kind is not (SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff))
            {
                WriteDefault(bytes, field);
            }
        }

        return bytes;
    }

    public static void WriteDefault(byte[] bytes, SaveFieldDefinition field)
    {
        Span<byte> target = bytes.AsSpan(field.Offset, field.Length);
        switch (field.Kind)
        {
            case SaveFieldKind.Footer:
                BitConverter.TryWriteBytes(target, FlatOut4SaveSchema.FooterValue);
                break;
            case SaveFieldKind.Float32:
                BitConverter.TryWriteBytes(target, (float)DefaultValue(field.Name));
                break;
            case SaveFieldKind.UInt8 or SaveFieldKind.Bool8:
                target[0] = (byte)DefaultValue(field.Name);
                break;
            case SaveFieldKind.Int32 or SaveFieldKind.UInt32:
                BitConverter.TryWriteBytes(target, unchecked((uint)(long)DefaultValue(field.Name)));
                break;
        }
    }

    // The default as a number: floats as floats, everything else as its integer value
    // (0xFFFFFFFF for "use the built-in binding").
    public static double DefaultValue(string name)
    {
        const string gameplay = "m_oGameOptions.m_oGameplayOptions.";
        if (name.StartsWith(gameplay, StringComparison.Ordinal))
        {
            return GameplayDefaults.GetValueOrDefault(name[gameplay.Length..]);
        }

        if (name.StartsWith("m_oGameOptions.m_uUserBindings", StringComparison.Ordinal) ||
            name.StartsWith("m_oGameOptions.m_uMenuBindings", StringComparison.Ordinal))
        {
            return uint.MaxValue;
        }

        if (name.StartsWith("m_oGameOptions.m_fDeviceSettings", StringComparison.Ordinal))
        {
            // FOGameOptions::Reset: the middle of each FODevicesConfig::DEFAULT range.
            string setting = name[(name.LastIndexOf('[') + 1)..^1];
            return setting switch
            {
                "ACC_SENSITIVITY" or "BRAKE_SENSITIVITY" => 1.75,
                "STEERING_SENSITIVITY" => 0.5,
                "ACC_DEADZONE" or "BRAKE_DEADZONE" or "STEERING_DEADZONE" => 49.5,
                _ => 0
            };
        }

        if (name.StartsWith("m_oGameOptions.m_iAudioVolumes", StringComparison.Ordinal))
        {
            return 100;
        }

        string pattern = IndexPattern().Replace(name, "[]");
        return pattern switch
        {
            "m_uSaveVersion" => FlatOut4SaveSchema.CurrentVersion,
            "m_oCareerData.m_iCredits" => StartingCredits,
            "m_oCareerData.m_oCurrentEvent.m_vDriversScore[].m_iIndex" => -1,
            "m_oCareerData.m_oCurrentEvent.m_vDriversScore[].m_oName" => -1,
            "m_oCareerData.m_vEventRewards[].m_oScore" => FlatOut4SaveSchema.MedalNone,
            "m_oCareerData.m_vEventRewards[].m_bNew" => 1,
            "m_oGarageData.m_vCarsState[].m_oUnlockableItem.m_iIndex" => FlatOut4GameData.CarTypeNone,
            "m_oGarageData.m_vCarsState[].m_iUpgradeLevel[]" => -1,
            "m_oGarageData.m_vTrackState[].m_iTrackLocaDiff" => -1,
            "m_oChallengeModeData.m_oNewChallenges[]" => 1,
            "m_oRecords.m_vBestLapTimeTrial[]" or "m_oRecords.m_vBestLapOther[]" => -1,
            _ when pattern.StartsWith("m_oGarageData.", StringComparison.Ordinal) && pattern.EndsWith(".m_eState", StringComparison.Ordinal) => FlatOut4SaveSchema.GarageStateUnused,
            _ when pattern.EndsWith("GarageConfig.m_uCarType", StringComparison.Ordinal) => FlatOut4GameData.CarTypeNone,
            _ when pattern.EndsWith("GarageConfig.m_uDriverType", StringComparison.Ordinal) => FlatOut4SaveSchema.DriverNone,
            _ => 0
        };
    }

    // Device settings with no FODevicesConfig::DEFAULT range. Nothing sets them and every real
    // save holds NaN there, so their value means nothing.
    public static bool IsUnsetDeviceSetting(string name) =>
        name.StartsWith("m_oGameOptions.m_fDeviceSettings", StringComparison.Ordinal) &&
        (name.EndsWith("[COUNTER_STEER_SENSITIVITY]", StringComparison.Ordinal) ||
         name.EndsWith("[VIBRATIONS_ENABLED]", StringComparison.Ordinal) ||
         name.EndsWith("[VIBRATIONS_LEVEL]", StringComparison.Ordinal));

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex IndexPattern();
}
