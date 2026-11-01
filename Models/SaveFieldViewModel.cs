using System.Globalization;
using FlatOut4SaveEditor.Services;

namespace FlatOut4SaveEditor.Models;

public enum FieldEditorKind
{
    Toggle,
    Choice,
    Number,
    Range,
    Text,
    ReadOnly
}

public sealed record FieldOption(long Value, string Label)
{
    public override string ToString() => Label;
}

// One schema field bound to the session's live bytes. Typed editors (toggle, choice,
// number) write immediately; the text editor keeps an invalid draft with an error
// instead of writing it.
public sealed class SaveFieldViewModel : ObservableObject
{
    private readonly SaveSession session;
    private string textValue;
    private string error = string.Empty;
    private bool isModified;
    private IReadOnlyList<FieldOption>? options;
    private bool isHighlighted;

    public SaveFieldViewModel(SaveFieldDefinition definition, SaveSession session)
    {
        Definition = definition;
        this.session = session;
        Editor = ResolveEditor(definition);
        textValue = FormatValue();
    }

    public SaveFieldDefinition Definition { get; }

    public FieldEditorKind Editor { get; }

    public string Label => Definition.ShortName;

    public string FullName => Definition.DisplayName;

    public string RawName => Definition.Name;

    public string Section => Definition.Section;

    public string Offset => Definition.OffsetHex;

    public string Type => Definition.TypeLabel;

    public string Description => Definition.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Definition.Description);

    public bool CanEdit => Definition.IsEditable;

    public string Unit => Definition.Unit ?? string.Empty;

    public bool HasUnit => !string.IsNullOrEmpty(Definition.Unit);

    public double Minimum => Definition.Minimum ?? Definition.Kind switch
    {
        SaveFieldKind.UInt8 => byte.MinValue,
        SaveFieldKind.UInt32 => uint.MinValue,
        SaveFieldKind.Int32 => int.MinValue,
        _ => -float.MaxValue
    };

    public double Maximum => Definition.Maximum ?? Definition.Kind switch
    {
        SaveFieldKind.UInt8 => byte.MaxValue,
        SaveFieldKind.UInt32 => uint.MaxValue,
        SaveFieldKind.Int32 => int.MaxValue,
        _ => float.MaxValue
    };

    public double SmallChange => Definition.Kind == SaveFieldKind.Float32 && (Definition.Maximum ?? 0) <= 1 ? 0.01 : 1;

    public double LargeChange => Math.Max(1, (Definition.Maximum ?? 100) / 10);

    public bool IsFloat => Definition.Kind == SaveFieldKind.Float32;

    public bool BoolValue
    {
        get => ReadBool();
        set
        {
            if (value != ReadBool())
            {
                WriteBool(value);
            }
        }
    }

    public string OnOffText => BoolValue ? "On" : "Off";

    // CheckBox.IsChecked is nullable.
    public bool? CheckedValue
    {
        get => BoolValue;
        set => BoolValue = value == true;
    }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set => SetField(ref isHighlighted, value);
    }

    public string CellToolTip => HasError
        ? Error
        : HasValueLabel ? $"{Label}: {ValueLabel}" : Label;

    public IReadOnlyList<FieldOption> Options => options ??= BuildOptions();

    public int SelectedIndex
    {
        get
        {
            long current = ReadInteger();
            IReadOnlyList<FieldOption> list = Options;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Value == current)
                {
                    return i;
                }
            }

            return -1;
        }
        set
        {
            if (value >= 0 && value < Options.Count && Options[value].Value != ReadInteger())
            {
                WriteInteger(Options[value].Value);
            }
        }
    }

    public double NumberValue
    {
        get => Definition.Kind == SaveFieldKind.Float32 ? session.ReadSingle(Definition.Offset) : ReadInteger();
        set
        {
            if (double.IsNaN(value) || !CanEdit)
            {
                return;
            }

            if (Definition.Kind == SaveFieldKind.Float32)
            {
                float next = (float)Math.Clamp(value, Minimum, Maximum);
                float current = session.ReadSingle(Definition.Offset);
                if (BitConverter.SingleToInt32Bits(next) != BitConverter.SingleToInt32Bits(current))
                {
                    session.WriteBytes(Definition.Offset, BitConverter.GetBytes(next), this);
                }

                return;
            }

            long rounded = (long)Math.Round(Math.Clamp(value, Minimum, Maximum));
            if (rounded != ReadInteger())
            {
                WriteInteger(rounded);
            }

            if (rounded != value)
            {
                // The editor has no bound range; push the clamped value back to it.
                OnPropertyChanged();
            }
        }
    }

    public string TextValue
    {
        get => textValue;
        set
        {
            if (SetField(ref textValue, value))
            {
                CommitText();
            }
        }
    }

    public string DisplayValue => FormatValue();

    public string ValueLabel => DescribeValue();

    public bool HasValueLabel => !string.IsNullOrEmpty(ValueLabel);

    public string Error
    {
        get => error;
        private set
        {
            if (SetField(ref error, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(CellToolTip));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(error);

    public bool IsModified
    {
        get => isModified;
        private set => SetField(ref isModified, value);
    }

    public string ToolTipText
    {
        get
        {
            string text = $"{FullName}\n{RawName}\n{Offset} | {Type}";
            return HasDescription ? $"{Description}\n\n{text}" : text;
        }
    }

    internal void Refresh(bool keepDraft)
    {
        if (!keepDraft)
        {
            SetField(ref textValue, FormatValue(), nameof(TextValue));
            Error = string.Empty;
        }

        if (Editor == FieldEditorKind.Choice && options is not null && !options.Any(option => option.Value == ReadInteger()))
        {
            options = null;
            OnPropertyChanged(nameof(Options));
        }

        IsModified = session.DiffersFromBaseline(Definition);
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(CheckedValue));
        OnPropertyChanged(nameof(OnOffText));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ValueLabel));
        OnPropertyChanged(nameof(HasValueLabel));
        OnPropertyChanged(nameof(CellToolTip));
        OnPropertyChanged(nameof(NewValueText));
    }

    public long ReadInteger() => ReadIntegerFrom(session.Bytes);

    // Reads from either the live bytes or the on-disk baseline (for old -> new summaries).
    private long ReadIntegerFrom(byte[] bytes) => Definition.Kind switch
    {
        SaveFieldKind.UInt8 => bytes[Definition.Offset],
        SaveFieldKind.Bool8 => bytes[Definition.Offset] != 0 ? 1 : 0,
        SaveFieldKind.Int32 => BitConverter.ToInt32(bytes, Definition.Offset),
        SaveFieldKind.UInt32 or SaveFieldKind.Footer => BitConverter.ToUInt32(bytes, Definition.Offset),
        SaveFieldKind.Float32 => (long)BitConverter.ToSingle(bytes, Definition.Offset),
        SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff => ReadBoolFrom(bytes) ? 1 : 0,
        _ => 0
    };

    public void WriteInteger(long value)
    {
        switch (Definition.Kind)
        {
            case SaveFieldKind.UInt8:
                session.WriteBytes(Definition.Offset, [(byte)Math.Clamp(value, byte.MinValue, byte.MaxValue)], this);
                break;
            case SaveFieldKind.Bool8:
                session.WriteBytes(Definition.Offset, [(byte)(value != 0 ? 1 : 0)], this);
                break;
            case SaveFieldKind.Int32:
                session.WriteInt32(Definition.Offset, unchecked((int)value), this);
                break;
            case SaveFieldKind.UInt32:
                session.WriteUInt32(Definition.Offset, unchecked((uint)value), this);
                break;
            case SaveFieldKind.Float32:
                session.WriteBytes(Definition.Offset, BitConverter.GetBytes((float)value), this);
                break;
            case SaveFieldKind.Bit:
            case SaveFieldKind.ExtensionOnOff:
                WriteBool(value != 0);
                break;
        }
    }

    public bool ReadBool() => ReadBoolFrom(session.Bytes);

    private bool ReadBoolFrom(byte[] bytes) => Definition.Kind switch
    {
        SaveFieldKind.Bit => (BitConverter.ToUInt32(bytes, Definition.Offset) & (1u << (Definition.BitIndex ?? 0))) != 0,
        SaveFieldKind.ExtensionOnOff => !(BitConverter.ToUInt32(bytes, Definition.AuxOffset!.Value) == FlatOut4SaveSchema.ExtensionMagicValue &&
                                          BitConverter.ToUInt32(bytes, Definition.Offset) != 1),
        SaveFieldKind.Float32 => BitConverter.ToSingle(bytes, Definition.Offset) != 0,
        _ => ReadIntegerFrom(bytes) != 0
    };

    // Puts this value back to what was loaded (or last saved).
    public SaveSession Session => session;

    public void Revert() => session.RevertField(Definition);

    public string OldValueText => SummaryFrom(session.BaselineBytes);

    public string NewValueText => SummaryFrom(session.Bytes);

    // Short human-readable value, the way the editor shows it.
    private string SummaryFrom(byte[] bytes)
    {
        switch (Editor)
        {
            case FieldEditorKind.Toggle:
                return ReadBoolFrom(bytes) ? "On" : "Off";
            case FieldEditorKind.Choice:
                long value = ReadIntegerFrom(bytes);
                return Definition.ValueLabels?.TryGetValue(value, out string? label) == true ? label : $"Other ({value})";
        }

        // Ids with a known name (career events, skins) read better by name than as hex.
        if (Definition.Format == SaveFieldFormat.Hex && Definition.ValueLabels?.TryGetValue(ReadIntegerFrom(bytes), out string? name) == true)
        {
            return name;
        }

        string text = FormatValueFrom(bytes);
        return Definition.Unit is { Length: > 0 } unit && Editor is FieldEditorKind.Range or FieldEditorKind.Number ? $"{text} {unit}" : text;
    }

    public void WriteBool(bool value)
    {
        switch (Definition.Kind)
        {
            case SaveFieldKind.Bit:
            {
                uint mask = 1u << (Definition.BitIndex ?? 0);
                uint chunk = session.ReadUInt32(Definition.Offset);
                session.WriteUInt32(Definition.Offset, value ? chunk | mask : chunk & ~mask, this);
                break;
            }
            case SaveFieldKind.ExtensionOnOff:
                // FOSerializable::SetTelemetryOutput: tag the slots, then store the value.
                session.Group(() =>
                {
                    session.WriteUInt32(Definition.AuxOffset!.Value, FlatOut4SaveSchema.ExtensionMagicValue, this);
                    session.WriteUInt32(Definition.Offset, value ? 1u : 0u, this);
                }, Label);
                break;
            default:
                WriteInteger(value ? 1 : 0);
                break;
        }
    }

    private void CommitText()
    {
        if (!CanEdit)
        {
            return;
        }

        try
        {
            byte[] bytes = ParseText(textValue.Trim());
            Error = string.Empty;
            if (Definition.Kind is SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff)
            {
                WriteBool(bytes[0] != 0);
            }
            else
            {
                session.WriteBytes(Definition.Offset, bytes, this);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            Error = ex.Message;
        }
    }

    private byte[] ParseText(string value)
    {
        if (Definition.ValueLabels is not null)
        {
            foreach ((long key, string label) in Definition.ValueLabels)
            {
                if (string.Equals(label, value, StringComparison.OrdinalIgnoreCase))
                {
                    return EncodeInteger(key);
                }
            }
        }

        if (Definition.Format == SaveFieldFormat.Milliseconds)
        {
            return EncodeInteger(ParseLapTime(value));
        }

        switch (Definition.Kind)
        {
            case SaveFieldKind.Bool8:
            case SaveFieldKind.Bit:
            case SaveFieldKind.ExtensionOnOff:
                return [(byte)(ParseBool(value) ? 1 : 0)];
            case SaveFieldKind.Float32:
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                {
                    throw new FormatException("Enter a number such as 1.5.");
                }

                return BitConverter.GetBytes(parsedFloat);
            default:
                return EncodeInteger(ParseInteger(value));
        }
    }

    private byte[] EncodeInteger(long value) => Definition.Kind switch
    {
        SaveFieldKind.UInt8 => [checked((byte)value)],
        SaveFieldKind.Bool8 => [(byte)(value != 0 ? 1 : 0)],
        SaveFieldKind.Int32 => BitConverter.GetBytes(value is >= 0x80000000L and <= 0xFFFFFFFFL ? unchecked((int)(uint)value) : checked((int)value)),
        SaveFieldKind.UInt32 => BitConverter.GetBytes(value < 0 ? unchecked((uint)checked((int)value)) : checked((uint)value)),
        SaveFieldKind.Float32 => BitConverter.GetBytes((float)value),
        _ => [(byte)(value != 0 ? 1 : 0)]
    };

    // "1:23.456", "83.456" or "83" (seconds) -> milliseconds.
    private static long ParseLapTime(string value)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(value, @"^(?:(\d+):)?(\d+)(?:\.(\d{1,3}))?$");
        if (!match.Success)
        {
            throw new FormatException("Enter a time such as 1:23.456, or \"No record\".");
        }

        long minutes = match.Groups[1].Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        long seconds = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        long millis = match.Groups[3].Success ? long.Parse(match.Groups[3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;
        return checked((((minutes * 60) + seconds) * 1000) + millis);
    }

    internal static string FormatLapTime(long milliseconds)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds:D3}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:D2}.{span.Milliseconds:D3}";
    }

    private static long ParseInteger(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hex) || hex > uint.MaxValue)
            {
                throw new FormatException("Enter a 32-bit hex value such as 0x1F.");
            }

            return (long)hex;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            throw new FormatException("Enter a whole number.");
        }

        return parsed;
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        return value.ToLowerInvariant() switch
        {
            "1" or "yes" or "on" or "enabled" => true,
            "0" or "no" or "off" or "disabled" => false,
            _ => throw new FormatException("Use true/false, 1/0, on/off, or yes/no.")
        };
    }

    private string FormatValue() => FormatValueFrom(session.Bytes);

    private string FormatValueFrom(byte[] bytes)
    {
        switch (Definition.Kind)
        {
            case SaveFieldKind.Bool8:
            case SaveFieldKind.Bit:
                return ReadBoolFrom(bytes) ? "true" : "false";
            case SaveFieldKind.ExtensionOnOff:
                return ReadBoolFrom(bytes) ? "ON" : "OFF";
            case SaveFieldKind.Footer:
                return $"0x{BitConverter.ToUInt32(bytes, Definition.Offset):X8}";
            case SaveFieldKind.Float32:
                return BitConverter.ToSingle(bytes, Definition.Offset).ToString("R", CultureInfo.InvariantCulture);
        }

        long value = ReadIntegerFrom(bytes);
        if (Definition.Format == SaveFieldFormat.Milliseconds)
        {
            return Definition.ValueLabels?.TryGetValue(value, out string? none) == true ? none : FormatLapTime(value);
        }

        if (Definition.Format == SaveFieldFormat.Hex)
        {
            return $"0x{unchecked((uint)value):X8}";
        }

        if (Definition.Format == SaveFieldFormat.ControlId && Definition.ValueLabels?.TryGetValue(value, out string? sentinel) == true)
        {
            return sentinel;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private string DescribeValue()
    {
        if (Editor is FieldEditorKind.Toggle or FieldEditorKind.Choice)
        {
            return string.Empty;
        }

        if (Definition.Format == SaveFieldFormat.Seconds)
        {
            return FormatDuration(session.ReadSingle(Definition.Offset));
        }

        if (Definition.Format == SaveFieldFormat.Milliseconds)
        {
            return string.Empty;
        }

        if (Definition.ValueLabels is not null && Definition.Kind != SaveFieldKind.Float32)
        {
            long value = ReadInteger();
            if (Definition.Format == SaveFieldFormat.ControlId)
            {
                return Definition.ValueLabels.ContainsKey(value) ? "Uses the game default" : string.Empty;
            }

            if (Definition.ValueLabels.TryGetValue(value, out string? label))
            {
                return label;
            }
        }

        if (Definition.Kind == SaveFieldKind.Float32 && float.IsNaN(session.ReadSingle(Definition.Offset)))
        {
            return "Unused (NaN)";
        }

        return string.Empty;
    }

    internal static string FormatDuration(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0)
        {
            return string.Empty;
        }

        TimeSpan span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} h {span.Minutes:D2} min";
        }

        return span.TotalMinutes >= 1 ? $"{span.Minutes} min {span.Seconds:D2} s" : $"{span.TotalSeconds:0.#} s";
    }

    private IReadOnlyList<FieldOption> BuildOptions()
    {
        var list = new List<FieldOption>();
        if (Definition.ValueLabels is not null && Definition.KeepLabelOrder)
        {
            list.AddRange(Definition.ValueLabels.Select(pair => new FieldOption(pair.Key, pair.Value)));
        }
        else if (Definition.ValueLabels is not null)
        {
            // Sentinels such as "Default"/"Cleared" (0xFFFFFFFF/0xFFFFFFFE) lead the list. Lists sorted
            // by name also keep 0 on top: WithNone lists use it for "None" or "Default paint".
            IEnumerable<KeyValuePair<long, string>> ordered = Definition.ValueLabels.OrderBy(pair => pair.Key switch
            {
                0xFFFFFFFFL => 0,
                0xFFFFFFFEL => 1,
                0L when Definition.SortOptionsByLabel => 2,
                _ => 3
            });
            ordered = Definition.SortOptionsByLabel
                ? ((IOrderedEnumerable<KeyValuePair<long, string>>)ordered).ThenBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
                : ((IOrderedEnumerable<KeyValuePair<long, string>>)ordered).ThenBy(pair => pair.Key);
            list.AddRange(ordered.Select(pair => new FieldOption(pair.Key, pair.Value)));
        }

        long current = ReadInteger();
        if (!list.Any(option => option.Value == current))
        {
            list.Add(new FieldOption(current, Definition.Format == SaveFieldFormat.Hex ? $"Other (0x{(uint)current:X8})" : $"Other ({current})"));
        }

        return list;
    }

    private static FieldEditorKind ResolveEditor(SaveFieldDefinition definition)
    {
        if (!definition.IsEditable)
        {
            return FieldEditorKind.ReadOnly;
        }

        switch (definition.Kind)
        {
            case SaveFieldKind.Bit:
            case SaveFieldKind.Bool8:
            case SaveFieldKind.ExtensionOnOff:
                return FieldEditorKind.Toggle;
        }

        if (definition.PreferChoice && definition.ValueLabels is not null)
        {
            return FieldEditorKind.Choice;
        }

        if (definition.Format is SaveFieldFormat.Hex or SaveFieldFormat.ControlId or SaveFieldFormat.Milliseconds)
        {
            return FieldEditorKind.Text;
        }

        if (definition.ValueLabels is not null)
        {
            bool isOnOff = ReferenceEquals(definition.ValueLabels, FlatOut4SaveSchema.OnOffLabels) ||
                ReferenceEquals(definition.ValueLabels, FlatOut4SaveSchema.BoolLabels);
            if (isOnOff && !definition.PreferChoice)
            {
                return FieldEditorKind.Toggle;
            }

            if (definition.PreferChoice || definition.ValueLabels.Count > 1)
            {
                return FieldEditorKind.Choice;
            }
        }

        if (definition.Minimum is not null && definition.Maximum is not null && definition.Maximum - definition.Minimum <= 1080)
        {
            return FieldEditorKind.Range;
        }

        return FieldEditorKind.Number;
    }
}
