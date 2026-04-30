namespace FlatOut4SaveEditor.Models;

public sealed record SaveFieldDefinition(
    string Section,
    string Name,
    int Offset,
    SaveFieldKind Kind,
    int Length,
    int? BitIndex = null,
    IReadOnlyDictionary<long, string>? ValueLabels = null)
{
    public string OffsetHex => $"0x{Offset:X6}";

    public string DisplayName => SaveFieldNameFormatter.Format(Name);

    public string TypeLabel => Kind switch
    {
        SaveFieldKind.UInt8 => "uint8",
        SaveFieldKind.Bool8 => "bool",
        SaveFieldKind.Int32 => "int32",
        SaveFieldKind.UInt32 => "uint32",
        SaveFieldKind.Float32 => "float",
        SaveFieldKind.Footer => "footer",
        SaveFieldKind.Bit => "bit",
        _ => Kind.ToString()
    };

    public bool IsEditable => Kind != SaveFieldKind.Footer;
}
