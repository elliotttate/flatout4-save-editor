using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FlatOut4SaveEditor.Models;

public sealed class SaveFieldViewModel : INotifyPropertyChanged
{
    private readonly byte[] buffer;
    private string draftValue;
    private string displayValue;
    private string labelValue;
    private string error = string.Empty;
    private bool isModified;

    public SaveFieldViewModel(SaveFieldDefinition definition, byte[] buffer)
    {
        Definition = definition;
        this.buffer = buffer;
        displayValue = ReadValue();
        draftValue = displayValue;
        labelValue = ReadLabel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SaveFieldDefinition Definition { get; }

    public string Section => Definition.Section;

    public string Name => Definition.DisplayName;

    public string RawName => Definition.Name;

    public string Offset => Definition.OffsetHex;

    public string Type => Definition.TypeLabel;

    public bool CanEdit => Definition.IsEditable;

    public string DisplayValue
    {
        get => displayValue;
        private set => SetField(ref displayValue, value);
    }

    public string DraftValue
    {
        get => draftValue;
        set
        {
            if (SetField(ref draftValue, value))
            {
                IsModified = draftValue != DisplayValue;
                ValidateDraft();
            }
        }
    }

    public string LabelValue
    {
        get => labelValue;
        private set => SetField(ref labelValue, value);
    }

    public string Error
    {
        get => error;
        private set => SetField(ref error, value);
    }

    public bool IsModified
    {
        get => isModified;
        private set => SetField(ref isModified, value);
    }

    public bool CommitDraft()
    {
        ValidateDraft();
        if (!string.IsNullOrWhiteSpace(Error))
        {
            return false;
        }

        if (!CanEdit || !IsModified)
        {
            return true;
        }

        try
        {
            WriteValue(DraftValue.Trim());
            DisplayValue = ReadValue();
            DraftValue = DisplayValue;
            LabelValue = ReadLabel();
            IsModified = false;
            Error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            Error = ex.Message;
            return false;
        }
    }

    public void ResetDraft()
    {
        DisplayValue = ReadValue();
        DraftValue = DisplayValue;
        LabelValue = ReadLabel();
        Error = string.Empty;
        IsModified = false;
    }

    private string ReadValue()
    {
        return Definition.Kind switch
        {
            SaveFieldKind.UInt8 => buffer[Definition.Offset].ToString(CultureInfo.InvariantCulture),
            SaveFieldKind.Bool8 => buffer[Definition.Offset] != 0 ? "true" : "false",
            SaveFieldKind.Int32 => BitConverter.ToInt32(buffer, Definition.Offset).ToString(CultureInfo.InvariantCulture),
            SaveFieldKind.UInt32 => BitConverter.ToUInt32(buffer, Definition.Offset).ToString(CultureInfo.InvariantCulture),
            SaveFieldKind.Float32 => BitConverter.ToSingle(buffer, Definition.Offset).ToString("R", CultureInfo.InvariantCulture),
            SaveFieldKind.Footer => $"0x{BitConverter.ToUInt32(buffer, Definition.Offset):X8}",
            SaveFieldKind.Bit => ReadBit() ? "true" : "false",
            _ => string.Empty
        };
    }

    private void ValidateDraft()
    {
        if (!CanEdit)
        {
            Error = string.Empty;
            return;
        }

        try
        {
            ValidateValue(DraftValue.Trim());
            Error = string.Empty;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            Error = ex.Message;
        }
    }

    private void ValidateValue(string value)
    {
        switch (Definition.Kind)
        {
            case SaveFieldKind.UInt8:
                _ = ParseUInt8(value);
                break;
            case SaveFieldKind.Bool8:
            case SaveFieldKind.Bit:
                _ = ParseBool(value);
                break;
            case SaveFieldKind.Int32:
                _ = ParseInt32(value);
                break;
            case SaveFieldKind.UInt32:
                _ = ParseUInt32(value);
                break;
            case SaveFieldKind.Float32:
                _ = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                break;
            case SaveFieldKind.Footer:
                throw new InvalidOperationException("Footer fields are not editable.");
        }
    }

    private string ReadLabel()
    {
        if (Definition.ValueLabels is null)
        {
            return string.Empty;
        }

        long value = Definition.Kind switch
        {
            SaveFieldKind.UInt8 => buffer[Definition.Offset],
            SaveFieldKind.Bool8 => buffer[Definition.Offset] != 0 ? 1 : 0,
            SaveFieldKind.Int32 => BitConverter.ToInt32(buffer, Definition.Offset),
            SaveFieldKind.UInt32 => BitConverter.ToUInt32(buffer, Definition.Offset),
            SaveFieldKind.Bit => ReadBit() ? 1 : 0,
            _ => long.MinValue
        };

        return Definition.ValueLabels.TryGetValue(value, out string? label) ? label : string.Empty;
    }

    private bool ReadBit()
    {
        int bit = Definition.BitIndex ?? 0;
        uint chunk = BitConverter.ToUInt32(buffer, Definition.Offset);
        return (chunk & (1u << bit)) != 0;
    }

    private void WriteValue(string value)
    {
        switch (Definition.Kind)
        {
            case SaveFieldKind.UInt8:
                buffer[Definition.Offset] = ParseUInt8(value);
                break;
            case SaveFieldKind.Bool8:
                buffer[Definition.Offset] = ParseBool(value) ? (byte)1 : (byte)0;
                break;
            case SaveFieldKind.Int32:
                WriteBytes(BitConverter.GetBytes(ParseInt32(value)));
                break;
            case SaveFieldKind.UInt32:
                WriteBytes(BitConverter.GetBytes(ParseUInt32(value)));
                break;
            case SaveFieldKind.Float32:
                WriteBytes(BitConverter.GetBytes(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)));
                break;
            case SaveFieldKind.Bit:
                WriteBit(ParseBool(value));
                break;
            case SaveFieldKind.Footer:
                throw new InvalidOperationException("Footer fields are not editable.");
        }
    }

    private void WriteBytes(byte[] bytes)
    {
        Array.Copy(bytes, 0, buffer, Definition.Offset, Definition.Length);
    }

    private void WriteBit(bool enabled)
    {
        int bit = Definition.BitIndex ?? 0;
        uint chunk = BitConverter.ToUInt32(buffer, Definition.Offset);
        chunk = enabled ? chunk | (1u << bit) : chunk & ~(1u << bit);
        WriteBytes(BitConverter.GetBytes(chunk));
    }

    private static byte ParseUInt8(string value)
    {
        uint parsed = ParseUInt32(value);
        if (parsed > byte.MaxValue)
        {
            throw new OverflowException("Value must be between 0 and 255.");
        }

        return (byte)parsed;
    }

    private static int ParseInt32(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return unchecked((int)uint.Parse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        }

        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static uint ParseUInt32(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
