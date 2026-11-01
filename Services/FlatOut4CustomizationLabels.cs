namespace FlatOut4SaveEditor.Services;

public static partial class FlatOut4CustomizationNames
{
    private static IReadOnlyDictionary<long, string>? carSkins;

    // "Grosse Pointe Skin 1 · Buick (Derby)": the skin's name, then its car named as the garage
    // names it. Built on first use so it never depends on the generated table's initialisation order.
    public static IReadOnlyDictionary<long, string> CarSkins => carSkins ??= CarSkinParts.ToDictionary(
        entry => entry.Key,
        entry => entry.Value.CarType.Length == 0
            ? entry.Value.Name
            : $"{entry.Value.Name} · {FlatOut4GameData.CarTypeLabel(entry.Value.CarType)}");
}
