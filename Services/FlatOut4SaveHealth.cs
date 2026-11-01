using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

// One value the game cannot make sense of, with the bytes that repair it.
public sealed record SaveHealthIssue(SaveFieldDefinition Field, string Category, string Problem, string RepairText, byte[] Repair);

// Finds values in a V95 profile that no build of the game writes: binding ids past the end of
// their device's control list, enum values outside their range, NaN floats, yes/no bytes that are
// neither, and uninitialised-memory fills. The game itself only checks the version and markers,
// so it loads such values as they are; repairing writes what a new profile has instead.
public static class FlatOut4SaveHealth
{
    private const uint KeyboardMaxControl = 0xED; // KEYBOARD_MEDIA_SELECT
    private const uint GamepadMaxControl = 37; // GamePadDeviceControlId POINTER2_Y
    private const uint WheelMaxControl = 143; // GenericDeviceControlId POV_UP

    // Issue categories, in the order the health page lists them.
    public const string UnknownControl = "Bindings to controls that do not exist";
    public const string NotANumber = "Numbers that are not numbers";
    public const string OutOfRange = "Values outside their range";
    public const string UnknownChoice = "Choices the game does not have";
    public const string NotOnOff = "On/off values that are neither";
    public const string UninitialisedMemory = "Uninitialised memory";

    public static IReadOnlyList<string> Categories { get; } = [UnknownControl, NotANumber, OutOfRange, UnknownChoice, NotOnOff, UninitialisedMemory];

    // Uninitialised memory as MSVC debug builds and heaps leave it.
    private static readonly HashSet<uint> FillPatterns = [0xCDCDCDCD, 0xCCCCCCCC, 0xDDDDDDDD, 0xFDFDFDFD, 0xFEEEFEEE, 0xABABABAB, 0xBAADF00D];

    // Choice fields whose labels list every value the game uses.
    private static readonly string[] ClosedChoices =
    [
        "m_oGarageData.m_vCarsState[].m_oUnlockableItem.m_eState",
        "m_oGarageData.m_vCarSkinsState[].m_eState",
        "m_oGarageData.m_vDriversState[].m_eState",
        "m_oGarageData.m_vBoostFxState[].m_eState",
        "m_oGarageData.m_vCarHornState[].m_eState",
        "m_oGarageData.m_vTrackState[].m_eState",
        "m_oGarageData.m_vCarsState[].m_iUpgradeLevel[]",
        "m_oCareerData.m_vEventRewards[].m_oScore",
        "m_oCareerGarageConfig.m_uDriverType",
        "m_oSingleGarageConfig.m_uDriverType",
        "m_oMultiGarageConfig.m_uDriverType"
    ];

    public static bool IsFillPattern(uint value) => FillPatterns.Contains(value);

    public static bool IsPlausibleControl(int device, uint value) => device switch
    {
        0 => value <= KeyboardMaxControl,
        1 => value <= GamepadMaxControl,
        2 => value <= WheelMaxControl,
        _ => false
    };

    public static IReadOnlyList<SaveHealthIssue> Check(byte[] bytes, FlatOut4SaveSchema schema)
    {
        var issues = new List<SaveHealthIssue>();
        foreach (SaveFieldDefinition field in schema.Fields)
        {
            if (CheckField(bytes, field) is { } issue)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    // Non-zero m_aOther values past the slots this editor knows. A newer build may own them
    // (new profile values go there), so they are listed and always kept.
    public static IReadOnlyList<(int Slot, uint Value)> UnknownExtensionSlots(byte[] bytes, FlatOut4SaveSchema schema)
    {
        var slots = new List<(int, uint)>();
        for (int slot = FlatOut4SaveSchema.ExtensionSlotTelemetry + 1; slot < FlatOut4SaveSchema.OtherSlotCount; slot++)
        {
            uint value = BitConverter.ToUInt32(bytes, schema.ExtensionSlotOffset(slot));
            if (value != 0 && !IsFillPattern(value))
            {
                slots.Add((slot, value));
            }
        }

        return slots;
    }

    public static SaveHealthIssue? CheckField(byte[] bytes, SaveFieldDefinition field)
    {
        string name = field.Name;
        if (field.Kind is SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff or SaveFieldKind.Footer ||
            name == "m_uSaveVersion" || name.StartsWith("m_vPadding", StringComparison.Ordinal))
        {
            return null;
        }

        if (name.StartsWith("m_aOther", StringComparison.Ordinal))
        {
            uint slot = BitConverter.ToUInt32(bytes, field.Offset);
            return IsFillPattern(slot)
                ? Issue(field, UninitialisedMemory, $"Holds 0x{slot:X8}, uninitialised memory an old debug build left behind.", "Set to 0", 0)
                : null;
        }

        if (field.Kind == SaveFieldKind.Float32 && !FlatOut4SaveDefaults.IsUnsetDeviceSetting(name))
        {
            float value = BitConverter.ToSingle(bytes, field.Offset);
            if (!float.IsFinite(value))
            {
                return Issue(field, NotANumber, $"Holds {value}, which is not a number the game can use.", DefaultText(field), FlatOut4SaveDefaults.DefaultValue(name));
            }

            if (field.Minimum is double min && value < min || field.Maximum is double max && value > max)
            {
                double clamped = Math.Clamp(value, field.Minimum ?? double.MinValue, field.Maximum ?? double.MaxValue);
                return Issue(field, OutOfRange, $"{value:0.###} is outside {RangeText(field)}.", $"Set to {clamped:0.###}", clamped);
            }

            return null;
        }

        if (field.Kind == SaveFieldKind.Float32)
        {
            return null;
        }

        long raw = field.Kind switch
        {
            SaveFieldKind.UInt8 or SaveFieldKind.Bool8 => bytes[field.Offset],
            SaveFieldKind.Int32 => BitConverter.ToInt32(bytes, field.Offset),
            _ => BitConverter.ToUInt32(bytes, field.Offset)
        };

        if (name.StartsWith("m_oGameOptions.m_uUserBindings", StringComparison.Ordinal) ||
            name.StartsWith("m_oGameOptions.m_uMenuBindings", StringComparison.Ordinal))
        {
            uint control = (uint)raw;
            int device = Array.FindIndex(FlatOut4SaveSchema.BindingDeviceNames, device => name.Contains($"[{device}]", StringComparison.Ordinal));
            return control is uint.MaxValue or uint.MaxValue - 1 || IsPlausibleControl(device, control)
                ? null
                : Issue(field, UnknownControl, $"Control id {control} (0x{control:X}) is past the end of the {FlatOut4SaveSchema.BindingDeviceNames[Math.Max(0, device)]} control list.", "Use the default binding", uint.MaxValue);
        }

        if (field.Kind == SaveFieldKind.Bool8 || field.ValueLabels is { Count: 2 } onOff && onOff.ContainsKey(0) && onOff.ContainsKey(1))
        {
            return raw is 0 or 1
                ? null
                : Issue(field, NotOnOff, $"Holds {raw}; an on/off value is 0 or 1.", field.Kind == SaveFieldKind.Bool8 ? "Set to 1 (on)" : DefaultText(field), field.Kind == SaveFieldKind.Bool8 ? 1 : FlatOut4SaveDefaults.DefaultValue(name));
        }

        if (field.ValueLabels is { } labels && field.Format != SaveFieldFormat.Hex && IsClosedChoice(name) && !labels.ContainsKey(raw))
        {
            return Issue(field, UnknownChoice, $"{raw} is not one of the values the game uses here.", DefaultText(field), FlatOut4SaveDefaults.DefaultValue(name));
        }

        if (field.ValueLabels is null && (field.Minimum is double low && raw < low || field.Maximum is double high && raw > high))
        {
            double clamped = Math.Clamp(raw, field.Minimum ?? double.MinValue, field.Maximum ?? double.MaxValue);
            return Issue(field, OutOfRange, $"{raw} is outside {RangeText(field)}.", $"Set to {clamped:0}", clamped);
        }

        return null;
    }

    private static bool IsClosedChoice(string name)
    {
        if (name.StartsWith("m_oGameOptions.m_oGameplayOptions.", StringComparison.Ordinal))
        {
            return true;
        }

        string pattern = System.Text.RegularExpressions.Regex.Replace(name, @"\[[^\]]*\]", "[]");
        return ClosedChoices.Contains(pattern, StringComparer.Ordinal);
    }

    private static string RangeText(SaveFieldDefinition field) =>
        $"the {field.Minimum:0.###}–{field.Maximum:0.###}{(field.Unit is { Length: > 0 } unit ? " " + unit : string.Empty)} range";

    private static string DefaultText(SaveFieldDefinition field)
    {
        double value = FlatOut4SaveDefaults.DefaultValue(field.Name);
        string text = field.Kind == SaveFieldKind.Float32 ? value.ToString("0.###") : ((long)value).ToString();
        return field.ValueLabels is { } labels && labels.TryGetValue((long)value, out string? label)
            ? $"Set to {label}"
            : $"Set to {text} (new-profile value)";
    }

    private static SaveHealthIssue Issue(SaveFieldDefinition field, string category, string problem, string repairText, double value)
    {
        byte[] repair = field.Kind switch
        {
            SaveFieldKind.Float32 => BitConverter.GetBytes((float)value),
            SaveFieldKind.UInt8 or SaveFieldKind.Bool8 => [(byte)value],
            _ => BitConverter.GetBytes(unchecked((uint)(long)value))
        };

        return new SaveHealthIssue(field, category, problem, repairText, repair);
    }
}
