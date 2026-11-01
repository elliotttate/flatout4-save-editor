namespace FlatOut4SaveEditor.Models;

public enum SaveFieldKind
{
    UInt8,
    Bool8,
    Int32,
    UInt32,
    Float32,
    Footer,
    Bit,

    // A tagged m_aOther extension toggle: the value slot only counts while the
    // magic slot (AuxOffset) holds "FXT1"; untagged saves read as ON.
    ExtensionOnOff
}
