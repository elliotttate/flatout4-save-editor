namespace FlatOut4SaveEditor.Models;

public enum SaveFieldFormat
{
    Default,
    Hex,
    Seconds,
    ControlId,
    Milliseconds
}

public sealed record SaveFieldDefinition(
    string Section,
    string Name,
    int Offset,
    SaveFieldKind Kind,
    int Length,
    int? BitIndex = null,
    IReadOnlyDictionary<long, string>? ValueLabels = null)
{
    public string? Label { get; init; }

    public string? Description { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public string? Unit { get; init; }

    public SaveFieldFormat Format { get; init; }

    public int? AuxOffset { get; init; }

    public bool PreferChoice { get; init; }

    public bool SortOptionsByLabel { get; init; }

    // Offer the labels in the order they were built (the game's own order) rather than by value,
    // for hashed ids whose numeric order means nothing.
    public bool KeepLabelOrder { get; init; }

    public string OffsetHex => $"0x{Offset:X6}";

    public string DisplayName => SaveFieldNameFormatter.Format(Name);

    public string ShortName => Label ?? SaveFieldNameFormatter.FormatLast(Name);

    public string TypeLabel => Kind switch
    {
        SaveFieldKind.UInt8 => "uint8",
        SaveFieldKind.Bool8 => "bool",
        SaveFieldKind.Int32 => "int32",
        SaveFieldKind.UInt32 => "uint32",
        SaveFieldKind.Float32 => "float",
        SaveFieldKind.Footer => "marker",
        SaveFieldKind.Bit => "bit",
        SaveFieldKind.ExtensionOnOff => "ext on/off",
        _ => Kind.ToString()
    };

    public bool IsEditable => Kind != SaveFieldKind.Footer;
}
