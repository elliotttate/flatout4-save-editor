using System.Runtime.CompilerServices;
using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

// Where each part of a file sits relative to the V95 layout the editor works in. Runs are
// stretches of whole 4-byte cells; bytes in Unknown are in the file but not in V95 (data a newer
// build added), bytes in Missing are in V95 but not in the file (cut off, or never written).
public sealed record FlatOut4SaveLayoutMap(
    IReadOnlyList<FlatOut4SaveLayoutMap.Run> Matched,
    IReadOnlyList<FlatOut4SaveLayoutMap.Range> Unknown,
    IReadOnlyList<FlatOut4SaveLayoutMap.Range> Missing,
    int FileLength)
{
    public readonly record struct Run(int SchemaOffset, int FileOffset, int Length);

    // Offset is a file offset for Unknown ranges and a V95 offset for Missing ones.
    public readonly record struct Range(int Offset, int Length);

    public int UnknownBytes => Unknown.Sum(range => range.Length);

    public int MissingBytes => Missing.Sum(range => range.Length);

    public bool IsIdentity => Unknown.Count == 0 && Missing.Count == 0 && Matched.All(run => run.SchemaOffset == run.FileOffset);

    // Puts edited V95 values back into the original file: every matched run is copied to where
    // it came from, unknown bytes and the file's own version stamp stay exactly as they were.
    public byte[] WriteBack(byte[] schemaBytes, byte[] originalFile)
    {
        byte[] output = (byte[])originalFile.Clone();
        foreach (Run run in Matched)
        {
            Array.Copy(schemaBytes, run.SchemaOffset, output, run.FileOffset, run.Length);
        }

        Array.Copy(originalFile, 4, output, 4, 4);
        return output;
    }
}

public sealed record FlatOut4RecoveryResult(
    byte[] Bytes,
    FlatOut4SaveLayoutMap Map,
    int CheckedValues,
    int ImplausibleValues,
    int StrongValues,
    IReadOnlyList<string> Notes)
{
    public double ImplausibleRatio => CheckedValues == 0 ? 1 : (double)ImplausibleValues / CheckedValues;

    public double MatchedRatio(FlatOut4SaveSchema schema) =>
        Map.Matched.Sum(run => run.Length) / (double)schema.SerializableSize;

    // Enough of the file lines up with sensible values to call it a FlatOut 4 profile. Blank or
    // zero-filled data fits almost anything, so it also takes a good number of values only a real
    // profile has (default bindings, unset lap records, real floats, markers).
    public bool IsUsable(FlatOut4SaveSchema schema) =>
        ImplausibleRatio <= 0.08 && MatchedRatio(schema) >= 0.5 && StrongValues >= 64;
}

// Reads a save whose layout the editor does not know (a newer build, a cut-off or padded file,
// a damaged header) by lining its 4-byte cells up with the V95 layout. Each V95 value has a
// notion of a sensible value (bindings, enums, floats, markers, record times...); a banded
// sequence alignment with gap penalties finds where bytes were added or lost so that the most
// values read sensibly, preferring gaps at the end of a struct, where new fields get appended.
public static class FlatOut4SaveRecovery
{
    private const int CellSize = 4;
    private const int MaxShiftCells = 2048;
    private const int SlackCells = 40;
    private const float GapOpen = -8f;
    private const float GapExtend = -0.05f;

    // A gap that runs to the end of the file: cut-off saves and trailing junk are far more
    // common than data missing from the middle.
    private const float TrailingGapBonus = 6f;

    // Where a newer build would add data: the end of FOGameplayOptions (every options bump from
    // V83 to the RC-only V96 appended there) and FOSerializableExt, just before m_aOther.
    private const float GrowthPointBonus = 6f;
    private const byte FromMatch = 0;
    private const byte FromInsert = 1;
    private const byte FromDelete = 2;

    private static readonly ConditionalWeakTable<FlatOut4SaveSchema, CellModel> Models = new();

    public static FlatOut4RecoveryResult Recover(byte[] source, FlatOut4SaveSchema schema)
    {
        CellModel model = Models.GetValue(schema, key => new CellModel(key));
        int n = model.CellCount;
        int m = source.Length / CellSize;
        int shift = m - n;
        if (Math.Abs(shift) > MaxShiftCells)
        {
            throw new InvalidDataException(
                $"This file is {source.Length:N0} bytes; a FlatOut 4 profile is {schema.SerializableSize:N0}. It is too different to be read as one.");
        }

        int low = Math.Min(0, shift) - SlackCells;
        int band = Math.Max(0, shift) + SlackCells - low + 1;
        byte[] trace = new byte[(n + 1) * band];
        var hPrev = new float[band];
        var iPrev = new float[band];
        var dPrev = new float[band];
        var hCur = new float[band];
        var iCur = new float[band];
        var dCur = new float[band];

        // Row 0: nothing of the layout consumed yet; leading file cells can only be insertions.
        Array.Fill(hPrev, float.NegativeInfinity);
        Array.Fill(dPrev, float.NegativeInfinity);
        Array.Fill(iPrev, float.NegativeInfinity);
        int start = -low;
        hPrev[start] = 0;
        FillInsertions(0, hPrev, iPrev, dPrev, trace, band, low, m, model);

        for (int c = 1; c <= n; c++)
        {
            int traceRow = c * band;
            for (int k = 0; k < band; k++)
            {
                int f = c + low + k;
                hCur[k] = float.NegativeInfinity;
                dCur[k] = float.NegativeInfinity;
                iCur[k] = float.NegativeInfinity;
                if (f < 0 || f > m)
                {
                    continue;
                }

                byte bits = 0;
                if (f >= 1)
                {
                    (float best, byte from) = Best(hPrev[k], FromMatch, iPrev[k], FromInsert, dPrev[k], FromDelete);
                    if (!float.IsNegativeInfinity(best))
                    {
                        hCur[k] = best + model.Score(c - 1, source, f - 1);
                        bits |= from;
                    }
                }

                if (k + 1 < band)
                {
                    float open = GapOpen + model.Boundary[c - 1];
                    (float best, byte from) = Best(hPrev[k + 1] + open, FromMatch, iPrev[k + 1] + open, FromInsert, dPrev[k + 1] + GapExtend, FromDelete);
                    dCur[k] = best;
                    bits |= (byte)(from << 4);
                }

                trace[traceRow + k] = bits;
            }

            FillInsertions(c, hCur, iCur, dCur, trace, band, low, m, model);
            (hPrev, hCur) = (hCur, hPrev);
            (iPrev, iCur) = (iCur, iPrev);
            (dPrev, dCur) = (dCur, dPrev);
        }

        int end = shift - low;
        (float _, byte state) = Best(hPrev[end], FromMatch, iPrev[end] + TrailingGapBonus, FromInsert, dPrev[end] + TrailingGapBonus, FromDelete);
        return Build(source, schema, model, Traceback(trace, band, low, n, end, start, state));
    }

    // Diagnostics: the values of a V95-sized file that the matcher counts as implausible.
    public static IReadOnlyList<string> ImplausibleValues(byte[] source, FlatOut4SaveSchema schema)
    {
        CellModel model = Models.GetValue(schema, key => new CellModel(key));
        var result = new List<string>();
        for (int cell = 0; cell < Math.Min(model.CellCount, source.Length / CellSize); cell++)
        {
            result.AddRange(model.Explain(cell, source));
        }

        return result;
    }

    // File cells with no V95 counterpart, scanned left to right within one layout row.
    private static void FillInsertions(int c, float[] h, float[] i, float[] d, byte[] trace, int band, int low, int m, CellModel model)
    {
        float open = GapOpen + model.Boundary[c];
        for (int k = 1; k < band; k++)
        {
            int f = c + low + k;
            if (f < 1 || f > m)
            {
                continue;
            }

            (float best, byte from) = Best(h[k - 1] + open, FromMatch, i[k - 1] + GapExtend, FromInsert, d[k - 1] + open, FromDelete);
            if (best > i[k])
            {
                i[k] = best;
                trace[(c * band) + k] = (byte)((trace[(c * band) + k] & 0b11_0011) | (from << 2));
            }
        }
    }

    private static (float Score, byte From) Best(float a, byte fromA, float b, byte fromB, float c, byte fromC)
    {
        if (a >= b && a >= c)
        {
            return (a, fromA);
        }

        return b >= c ? (b, fromB) : (c, fromC);
    }

    private enum Step : byte
    {
        Match,
        Insert,
        Delete
    }

    private static List<(Step Step, int Cell, int FileCell)> Traceback(byte[] trace, int band, int low, int n, int end, int start, byte state)
    {
        var steps = new List<(Step, int, int)>();
        int c = n;
        int k = end;
        while (c > 0 || k != start)
        {
            int f = c + low + k;
            byte bits = trace[(c * band) + k];
            switch (state)
            {
                case FromMatch:
                    steps.Add((Step.Match, c - 1, f - 1));
                    state = (byte)(bits & 0b11);
                    c--;
                    break;
                case FromInsert:
                    steps.Add((Step.Insert, c, f - 1));
                    state = (byte)((bits >> 2) & 0b11);
                    k--;
                    break;
                default:
                    steps.Add((Step.Delete, c - 1, f));
                    state = (byte)((bits >> 4) & 0b11);
                    c--;
                    k++;
                    break;
            }

            if (c == 0 && k == start)
            {
                break;
            }
        }

        steps.Reverse();
        return steps;
    }

    private static FlatOut4RecoveryResult Build(byte[] source, FlatOut4SaveSchema schema, CellModel model, List<(Step Step, int Cell, int FileCell)> steps)
    {
        var matched = new List<FlatOut4SaveLayoutMap.Run>();
        var unknown = new List<FlatOut4SaveLayoutMap.Range>();
        var missing = new List<FlatOut4SaveLayoutMap.Range>();
        var unknownAt = new List<int>();
        int checkedValues = 0;
        int implausible = 0;
        int strong = 0;

        foreach ((Step step, int cell, int fileCell) in steps)
        {
            switch (step)
            {
                case Step.Match:
                    if (matched.Count > 0 && matched[^1] is var run &&
                        run.SchemaOffset + run.Length == cell * CellSize && run.FileOffset + run.Length == fileCell * CellSize)
                    {
                        matched[^1] = run with { Length = run.Length + CellSize };
                    }
                    else
                    {
                        matched.Add(new(cell * CellSize, fileCell * CellSize, CellSize));
                    }

                    (int probes, int bad, int good) = model.Check(cell, source, fileCell);
                    checkedValues += probes;
                    implausible += bad;
                    strong += good;
                    break;
                case Step.Insert:
                    if (unknown.Count > 0 && unknown[^1].Offset + unknown[^1].Length == fileCell * CellSize)
                    {
                        unknown[^1] = unknown[^1] with { Length = unknown[^1].Length + CellSize };
                    }
                    else
                    {
                        unknown.Add(new(fileCell * CellSize, CellSize));
                        unknownAt.Add(cell);
                    }

                    break;
                default:
                    if (missing.Count > 0 && missing[^1].Offset + missing[^1].Length == cell * CellSize)
                    {
                        missing[^1] = missing[^1] with { Length = missing[^1].Length + CellSize };
                    }
                    else
                    {
                        missing.Add(new(cell * CellSize, CellSize));
                    }

                    break;
            }
        }

        int tail = source.Length % CellSize;
        if (tail > 0)
        {
            unknown.Add(new(source.Length - tail, tail));
            unknownAt.Add(model.CellCount);
        }

        byte[] bytes = FlatOut4SaveDefaults.CreateFreshProfile(schema);
        foreach (FlatOut4SaveLayoutMap.Run run in matched)
        {
            Array.Copy(source, run.FileOffset, bytes, run.SchemaOffset, run.Length);
        }

        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), FlatOut4SaveSchema.FooterValue);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), FlatOut4SaveSchema.CurrentVersion);
        BitConverter.TryWriteBytes(bytes.AsSpan(bytes.Length - 4, 4), FlatOut4SaveSchema.FooterValue);

        var notes = new List<string>();
        for (int i = 0; i < unknown.Count; i++)
        {
            notes.Add($"{Bytes(unknown[i].Length)} the editor does not know, {model.DescribeGap(unknownAt[i])}.");
        }

        foreach (FlatOut4SaveLayoutMap.Range range in missing)
        {
            notes.Add(model.IsPadding(range.Offset, range.Length)
                ? $"The reserved padding is {Bytes(range.Length)} shorter; nothing is stored there."
                : $"{Bytes(range.Length)} missing {model.DescribeSpan(range.Offset, range.Length)}; those values start as a new profile has them.");
        }

        var map = new FlatOut4SaveLayoutMap(matched, unknown, missing, source.Length);
        return new FlatOut4RecoveryResult(bytes, map, checkedValues, implausible, strong, notes);
    }

    private static string Bytes(int count) => count == 1 ? "1 byte" : $"{count:N0} bytes";

    // Per-cell view of the V95 layout: which values live in each 4-byte cell and how plausible a
    // given 4 bytes are for them, plus how cheap it is to place a gap before each cell.
    private sealed class CellModel
    {
        private readonly Probe[][] probes;
        private readonly SaveFieldDefinition?[] firstField;
        private readonly SaveFieldDefinition?[] lastField;

        public CellModel(FlatOut4SaveSchema schema)
        {
            CellCount = schema.SerializableSize / CellSize;
            var lists = new List<Probe>[CellCount];
            firstField = new SaveFieldDefinition?[CellCount];
            lastField = new SaveFieldDefinition?[CellCount];
            foreach (SaveFieldDefinition field in schema.Fields.OrderBy(field => field.Offset))
            {
                if (field.Kind is SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff)
                {
                    continue;
                }

                int cell = field.Offset / CellSize;
                if (cell >= CellCount)
                {
                    continue;
                }

                firstField[cell] ??= field;
                lastField[cell] = field;
                if (Classify(field) is { } validator)
                {
                    (lists[cell] ??= []).Add(new Probe(field.Offset % CellSize, field.Length, validator, field));
                }
            }

            for (int cell = 1; cell < CellCount; cell++)
            {
                firstField[cell] ??= lastField[cell - 1];
                lastField[cell] ??= lastField[cell - 1];
            }

            probes = lists.Select(list => list?.ToArray() ?? []).ToArray();
            Boundary = new float[CellCount + 1];
            for (int cell = 1; cell < CellCount; cell++)
            {
                Boundary[cell] = BoundaryBonus(lastField[cell - 1]?.Name, firstField[cell]?.Name);
            }

            Boundary[(FlatOut4SaveSchema.GameOptionsOffset + FlatOut4SaveSchema.CurrentGameplayOptionsSize) / CellSize] = GrowthPointBonus;
            Boundary[schema.OtherSlotsOffset / CellSize] = GrowthPointBonus;
        }

        public int CellCount { get; }

        // Bonus for opening a gap just before a cell: big between save components, smaller between
        // sub-structs, a little between elements of a struct array, none inside a struct.
        public float[] Boundary { get; }

        public float Score(int cell, byte[] source, int fileCell)
        {
            float score = 0;
            foreach (Probe probe in probes[cell])
            {
                int offset = (fileCell * CellSize) + probe.Within;
                if (offset + probe.Length > source.Length)
                {
                    score -= 1;
                    continue;
                }

                score += Plausibility(probe, source, offset);
            }

            return score;
        }

        public (int Checked, int Bad, int Strong) Check(int cell, byte[] source, int fileCell)
        {
            int bad = 0;
            int strong = 0;
            foreach (Probe probe in probes[cell])
            {
                int offset = (fileCell * CellSize) + probe.Within;
                float plausibility = offset + probe.Length > source.Length ? -1 : Plausibility(probe, source, offset);
                if (plausibility < 0)
                {
                    bad++;
                }
                else if (plausibility >= 1)
                {
                    strong++;
                }
            }

            return (probes[cell].Length, bad, strong);
        }

        public IEnumerable<string> Explain(int cell, byte[] source)
        {
            foreach (Probe probe in probes[cell])
            {
                int offset = (cell * CellSize) + probe.Within;
                if (offset + probe.Length <= source.Length && Plausibility(probe, source, offset) < 0)
                {
                    yield return $"{probe.Field.Name} = 0x{BitConverter.ToUInt32(source, offset - probe.Within):X8} ({probe.Validator.Kind})";
                }
            }
        }

        public bool IsPadding(int offset, int length) =>
            firstField[offset / CellSize]?.Name.StartsWith("m_vPadding", StringComparison.Ordinal) == true &&
            lastField[Math.Min(CellCount - 1, (offset + length - 1) / CellSize)]?.Name.StartsWith("m_vPadding", StringComparison.Ordinal) == true;

        public string DescribeGap(int cell)
        {
            if (cell <= 0)
            {
                return "at the start of the file";
            }

            if (cell >= CellCount)
            {
                return "after the end of the profile";
            }

            return lastField[cell - 1] is { } field ? $"after {Describe(field)}" : $"at 0x{cell * CellSize:X}";
        }

        public string DescribeSpan(int offset, int length)
        {
            SaveFieldDefinition? first = firstField[offset / CellSize];
            SaveFieldDefinition? last = lastField[Math.Min(CellCount - 1, (offset + length - 1) / CellSize)];
            if (first is null || last is null)
            {
                return $"at 0x{offset:X}";
            }

            return ReferenceEquals(first, last) || first.Section == last.Section && first.ShortName == last.ShortName
                ? $"at {Describe(first)}"
                : $"from {Describe(first)} to {Describe(last)}";
        }

        private static string Describe(SaveFieldDefinition field) => $"{field.Section} › {field.ShortName}";

        private static float BoundaryBonus(string? before, string? after)
        {
            if (before is null || after is null)
            {
                return 0;
            }

            string[] left = Parents(before);
            string[] right = Parents(after);
            if (left.SequenceEqual(right))
            {
                return 0;
            }

            int common = 0;
            while (common < left.Length && common < right.Length && Strip(left[common]) == Strip(right[common]))
            {
                common++;
            }

            if (common == left.Length && common == right.Length)
            {
                return 0.5f; // next element of a struct array
            }

            return common switch
            {
                0 => 4f,
                1 => 3.5f,
                _ => 2.5f
            };
        }

        // The struct path a field belongs to; a top-level field (m_aOther[], m_vPadding[]) is its own.
        private static string[] Parents(string name)
        {
            string[] parts = name.Split('.');
            return parts.Length > 1 ? parts[..^1] : [Strip(parts[0])];
        }

        private static string Strip(string segment)
        {
            int bracket = segment.IndexOf('[');
            return bracket < 0 ? segment : segment[..bracket];
        }

        private static Validator? Classify(SaveFieldDefinition field)
        {
            string name = field.Name;
            if (field.Kind == SaveFieldKind.Footer)
            {
                return new Validator(ValueKind.Marker);
            }

            if (name == $"m_aOther[{FlatOut4SaveSchema.ExtensionSlotMagic}]")
            {
                return new Validator(ValueKind.ExtensionMagic);
            }

            if (name == "m_oTrophies.m_oTrophiesUnlocked.m_uChunkShift")
            {
                return new Validator(ValueKind.ChunkShift);
            }

            if (name == "m_uSaveVersion" || name.StartsWith("m_aOther", StringComparison.Ordinal) || name.StartsWith("m_vPadding", StringComparison.Ordinal))
            {
                return null;
            }

            if (name.StartsWith("m_oGameOptions.m_uUserBindings", StringComparison.Ordinal) ||
                name.StartsWith("m_oGameOptions.m_uMenuBindings", StringComparison.Ordinal))
            {
                int device = Array.FindIndex(FlatOut4SaveSchema.BindingDeviceNames, device => name.Contains($"[{device}]", StringComparison.Ordinal));
                return new Validator(ValueKind.Binding, Device: device);
            }

            if (field.Kind == SaveFieldKind.Float32)
            {
                return new Validator(FlatOut4SaveDefaults.IsUnsetDeviceSetting(name) ? ValueKind.UnsetFloat : ValueKind.Float, field.Minimum, field.Maximum);
            }

            if (field.Kind == SaveFieldKind.Bool8 || field.ValueLabels is { Count: 2 } labels && labels.ContainsKey(0) && labels.ContainsKey(1))
            {
                return new Validator(ValueKind.Bool);
            }

            if (name.EndsWith(".m_iTrackLocaDiff", StringComparison.Ordinal))
            {
                // Mostly a slot number, but DLC entries hold raw LocaIds (1700 and up) in real saves.
                return new Validator(ValueKind.Range, -1, 65535);
            }

            if (field.Format == SaveFieldFormat.Milliseconds)
            {
                return new Validator(ValueKind.LapTime);
            }

            if (field.ValueLabels is { } values)
            {
                return new Validator(field.Format == SaveFieldFormat.Hex ? ValueKind.Hash : ValueKind.Enum, Labels: values);
            }

            if (field.Minimum is not null || field.Maximum is not null)
            {
                return new Validator(ValueKind.Range, field.Minimum, field.Maximum);
            }


            return field.Kind switch
            {
                SaveFieldKind.UInt32 => new Validator(ValueKind.Counter),
                SaveFieldKind.Int32 => new Validator(ValueKind.Signed),
                _ => null
            };
        }

        private static float Plausibility(Probe probe, byte[] source, int offset)
        {
            Validator validator = probe.Validator;
            long value = probe.Length switch
            {
                1 => source[offset],
                _ => probe.Field.Kind == SaveFieldKind.Int32 ? BitConverter.ToInt32(source, offset) : BitConverter.ToUInt32(source, offset)
            };

            switch (validator.Kind)
            {
                case ValueKind.Marker:
                    return value == FlatOut4SaveSchema.FooterValue ? 12f : -2f;
                case ValueKind.ExtensionMagic:
                    return value == FlatOut4SaveSchema.ExtensionMagicValue ? 12f : 0f;
                case ValueKind.ChunkShift:
                    return value == TrophyChunkShift ? 6f : -3f;
                case ValueKind.UnsetFloat:
                    // The game never sets these and saves whatever is there (NaN on every real save).
                    return 0f;
                case ValueKind.Bool:
                    return value is 0 or 1 ? 0.3f : -3f;
                case ValueKind.Binding:
                    return BindingPlausibility(validator.Device, (uint)value);
                case ValueKind.Float:
                {
                    float number = BitConverter.ToSingle(source, offset);
                    if (!float.IsFinite(number))
                    {
                        return -3f;
                    }

                    if (number == 0)
                    {
                        return 0.1f;
                    }

                    float magnitude = Math.Abs(number);
                    if (magnitude < 1e-4f || magnitude > 1e7f)
                    {
                        return -3f;
                    }

                    return validator.Minimum is double min && number < min || validator.Maximum is double max && number > max ? -1f : 1f;
                }
                case ValueKind.LapTime:
                    return value switch
                    {
                        -1 => 1.5f,
                        0 => 0f,
                        > 0 and <= 3_600_000 => 1f,
                        _ => -2f
                    };
                case ValueKind.Hash:
                    if (validator.Labels!.ContainsKey(value))
                    {
                        return value == 0 ? 0.2f : 3f;
                    }

                    return value == 0 ? 0.2f : -0.5f;
                case ValueKind.Enum:
                    if (validator.Labels!.ContainsKey(value))
                    {
                        return value == 0 ? 0.3f : 1f;
                    }

                    return -1.5f;
                case ValueKind.Range:
                    return (validator.Minimum is null || value >= validator.Minimum) && (validator.Maximum is null || value <= validator.Maximum)
                        ? value == 0 ? 0.2f : 0.6f
                        : -2f;
                case ValueKind.Counter:
                    return value switch
                    {
                        0 => 0f,
                        < 100_000_000 => 0.1f,
                        _ => -1f
                    };
                default:
                    return value switch
                    {
                        0 => 0f,
                        -1 => 0.2f,
                        > -100_000_000 and < 100_000_000 => 0.05f,
                        _ => -0.5f
                    };
            }
        }

        private static float BindingPlausibility(int device, uint value)
        {
            if (value is uint.MaxValue or uint.MaxValue - 1)
            {
                return 1.5f;
            }

            return FlatOut4SaveHealth.IsPlausibleControl(device, value) ? 0.5f : -3f;
        }

        private const long TrophyChunkShift = 5; // FOStaticBitsField: 32 trophies per chunk

        private enum ValueKind : byte
        {
            Marker,
            ExtensionMagic,
            ChunkShift,
            UnsetFloat,
            Bool,
            Binding,
            Float,
            LapTime,
            Hash,
            Enum,
            Range,
            Counter,
            Signed
        }

        private sealed record Validator(
            ValueKind Kind,
            double? Minimum = null,
            double? Maximum = null,
            IReadOnlyDictionary<long, string>? Labels = null,
            int Device = -1);

        private readonly record struct Probe(int Within, int Length, Validator Validator, SaveFieldDefinition Field);
    }
}
