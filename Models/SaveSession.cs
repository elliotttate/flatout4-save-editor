using FlatOut4SaveEditor.Services;

namespace FlatOut4SaveEditor.Models;

// The loaded save plus everything the UI needs to edit it live: one view model per
// schema field, a byte -> field index so writes refresh every overlapping view, and a
// baseline copy so "modified" means "differs from what is on disk".
public sealed class SaveSession : ObservableObject
{
    private readonly Dictionary<int, List<SaveFieldViewModel>> fieldsByByte = [];
    private readonly Dictionary<string, SaveFieldViewModel> fieldsByName;
    private readonly HashSet<SaveFieldViewModel> modifiedFields = [];
    private byte[] baseline;
    private bool pendingConversion;
    private int batchDepth;

    // Undo history: each step holds the byte runs one user action changed, before and after.
    private const int MaxUndoSteps = 500;
    private const int SameFieldMs = 700;
    private readonly List<UndoStep> undoSteps = [];
    private readonly List<UndoStep> redoSteps = [];
    private UndoStep? openStep;
    private string? groupLabel;
    private int groupDepth;
    private bool replaying;

    public SaveSession(FlatOut4SaveDocument document, FlatOut4SaveSchema schema, FlatOut4SaveCandidate? origin = null)
    {
        Document = document;
        Schema = schema;
        Origin = origin;
        baseline = (byte[])document.Bytes.Clone();
        pendingConversion = document.Migrated;
        Fields = schema.Fields.Select(field => new SaveFieldViewModel(field, this)).ToArray();
        fieldsByName = Fields.ToDictionary(field => field.RawName, StringComparer.Ordinal);

        foreach (SaveFieldViewModel field in Fields)
        {
            foreach (int offset in CoveredOffsets(field.Definition))
            {
                if (!fieldsByByte.TryGetValue(offset, out List<SaveFieldViewModel>? list))
                {
                    list = [];
                    fieldsByByte[offset] = list;
                }

                list.Add(field);
            }
        }
    }

    public event EventHandler? DataChanged;

    // Diagnostics hook (snapshot mode logs every write with its stack).
    public static Action<string>? WriteTrace { get; set; }

    public FlatOut4SaveDocument Document { get; }

    public FlatOut4SaveSchema Schema { get; }

    public FlatOut4SaveCandidate? Origin { get; }

    public byte[] Bytes => Document.Bytes;

    // What is on disk (as loaded, or as last saved); "modified" means different from this.
    public byte[] BaselineBytes => baseline;

    public IReadOnlyList<SaveFieldViewModel> Fields { get; }

    public int ModifiedCount => modifiedFields.Count;

    public bool HasChanges => modifiedFields.Count > 0;

    // A converted (migrated) save still needs writing even when no field was touched.
    public bool NeedsWrite => HasChanges || pendingConversion;

    public bool PendingConversion => pendingConversion;

    public string FileName => System.IO.Path.GetFileName(Document.Path ?? "Save");

    public SaveFieldViewModel Field(string rawName) =>
        fieldsByName.TryGetValue(rawName, out SaveFieldViewModel? field)
            ? field
            : throw new KeyNotFoundException($"The save schema has no field {rawName}.");

    public SaveFieldViewModel? TryField(string rawName) =>
        fieldsByName.TryGetValue(rawName, out SaveFieldViewModel? field) ? field : null;

    public byte ReadByte(int offset) => Bytes[offset];

    public int ReadInt32(int offset) => BitConverter.ToInt32(Bytes, offset);

    public uint ReadUInt32(int offset) => BitConverter.ToUInt32(Bytes, offset);

    public float ReadSingle(int offset) => BitConverter.ToSingle(Bytes, offset);

    public void WriteUInt32(int offset, uint value, SaveFieldViewModel? origin = null) =>
        WriteBytes(offset, BitConverter.GetBytes(value), origin);

    public void WriteInt32(int offset, int value, SaveFieldViewModel? origin = null) =>
        WriteBytes(offset, BitConverter.GetBytes(value), origin);

    public void WriteBytes(int offset, ReadOnlySpan<byte> value, SaveFieldViewModel? origin = null)
    {
        Span<byte> target = Bytes.AsSpan(offset, value.Length);
        if (target.SequenceEqual(value))
        {
            return;
        }

        Record(offset, target, value, origin);
        value.CopyTo(target);
        WriteTrace?.Invoke($"write 0x{offset:X} by {origin?.RawName ?? "(none)"}{Environment.NewLine}{Environment.StackTrace}");
        NotifyRangeChanged(offset, value.Length, origin);
    }

    // Groups many writes into one refresh and one undo step (bulk actions).
    public void Batch(Action action, string? label = null)
    {
        batchDepth++;
        try
        {
            Group(action, label);
        }
        finally
        {
            batchDepth--;
            if (batchDepth == 0)
            {
                NotifyAllChanged();
            }
        }
    }

    // Writes made inside one group undo together (a medal and its reward slot, a tagged value).
    public void Group(Action action, string? label = null)
    {
        if (groupDepth == 0)
        {
            groupLabel = label;
        }

        groupDepth++;
        try
        {
            action();
        }
        finally
        {
            groupDepth--;
            if (groupDepth == 0)
            {
                if (openStep is { Writes.Count: 0 })
                {
                    undoSteps.Remove(openStep);
                }

                openStep = null;
                groupLabel = null;
            }
        }
    }

    // Changes made straight to the bytes (Unlock All, Revert) become one undo step.
    public void ApplyExternal(Action mutate, string label)
    {
        byte[] before = (byte[])Bytes.Clone();
        mutate();
        RecordWholeBuffer(before, label);
        NotifyAllChanged();
    }

    public bool CanUndo => undoSteps.Count > 0;

    public bool CanRedo => redoSteps.Count > 0;

    // What Undo / Redo would change, for tooltips and the status bar.
    public string? UndoLabel => undoSteps.Count > 0 ? undoSteps[^1].Label : null;

    public string? RedoLabel => redoSteps.Count > 0 ? redoSteps[^1].Label : null;

    // The most recent undo steps, newest first (the Undo button's history list).
    public IReadOnlyList<string> UndoLabels(int max) =>
        undoSteps.AsEnumerable().Reverse().Take(max).Select(step => step.Label).ToArray();

    // Both return the label of the step they replayed, or null when there was nothing to do.
    public string? Undo() => Replay(undoSteps, redoSteps, undo: true);

    public string? Redo() => Replay(redoSteps, undoSteps, undo: false);

    private string? Replay(List<UndoStep> from, List<UndoStep> to, bool undo)
    {
        if (from.Count == 0)
        {
            return null;
        }

        UndoStep step = from[^1];
        from.RemoveAt(from.Count - 1);
        replaying = true;
        try
        {
            IEnumerable<UndoWrite> writes = undo ? Enumerable.Reverse(step.Writes) : step.Writes;
            foreach (UndoWrite write in writes)
            {
                (undo ? write.Before : write.After).CopyTo(Bytes, write.Offset);
            }
        }
        finally
        {
            replaying = false;
        }

        to.Add(step);
        if (step.Writes.Sum(write => write.Before.Length) > 256)
        {
            NotifyAllChanged();
        }
        else
        {
            foreach (UndoWrite write in step.Writes)
            {
                NotifyRangeChanged(write.Offset, write.Before.Length);
            }
        }

        return step.Label;
    }

    private void Record(int offset, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, SaveFieldViewModel? origin)
    {
        if (replaying)
        {
            return;
        }

        redoSteps.Clear();
        long now = Environment.TickCount64;
        UndoStep? step = openStep;
        if (step is null && groupDepth == 0 && origin is { Editor: FieldEditorKind.Range or FieldEditorKind.Number } && undoSteps.Count > 0)
        {
            // Dragging a slider or spinning a number box keeps extending one step.
            UndoStep last = undoSteps[^1];
            if (ReferenceEquals(last.Origin, origin) && now - last.LastWriteMs < SameFieldMs)
            {
                step = last;
            }
        }

        if (step is null)
        {
            step = new UndoStep(groupDepth > 0 ? null : origin, groupLabel ?? origin?.Label ?? "Edit");
            Push(step);
            if (groupDepth > 0)
            {
                openStep = step;
            }
        }

        step.Add(offset, before, after);
        step.LastWriteMs = now;
        if (step.Writes.Count == 0 && !ReferenceEquals(step, openStep))
        {
            // A drag that ended where it started.
            undoSteps.Remove(step);
        }
    }

    private void RecordWholeBuffer(byte[] before, string label)
    {
        var step = new UndoStep(null, label);
        int i = 0;
        while (i < before.Length)
        {
            if (before[i] == Bytes[i])
            {
                i++;
                continue;
            }

            int start = i;
            while (i < before.Length && before[i] != Bytes[i])
            {
                i++;
            }

            step.Add(start, before.AsSpan(start, i - start), Bytes.AsSpan(start, i - start));
        }

        if (step.Writes.Count > 0)
        {
            redoSteps.Clear();
            Push(step);
        }
    }

    private void Push(UndoStep step)
    {
        undoSteps.Add(step);
        if (undoSteps.Count > MaxUndoSteps)
        {
            undoSteps.RemoveAt(0);
        }
    }

    private sealed record UndoWrite(int Offset, byte[] Before, byte[] After);

    private sealed class UndoStep(SaveFieldViewModel? origin, string label)
    {
        public SaveFieldViewModel? Origin { get; } = origin;

        public string Label { get; } = label;

        public List<UndoWrite> Writes { get; } = [];

        public long LastWriteMs { get; set; }

        public void Add(int offset, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
        {
            // Rewriting the same bytes keeps the first "before" and the latest "after"; only merge
            // into the newest write touching them, so replaying in order stays correct.
            int length = before.Length;
            int last = Writes.FindLastIndex(write => write.Offset < offset + length && offset < write.Offset + write.Before.Length);
            if (last < 0 || Writes[last].Offset != offset || Writes[last].Before.Length != length)
            {
                Writes.Add(new UndoWrite(offset, before.ToArray(), after.ToArray()));
            }
            else if (after.SequenceEqual(Writes[last].Before))
            {
                Writes.RemoveAt(last);
            }
            else
            {
                Writes[last] = Writes[last] with { After = after.ToArray() };
            }
        }
    }

    public void NotifyRangeChanged(int offset, int length, SaveFieldViewModel? origin = null)
    {
        if (batchDepth > 0)
        {
            return;
        }

        var affected = new HashSet<SaveFieldViewModel>();
        for (int i = offset; i < offset + length; i++)
        {
            if (fieldsByByte.TryGetValue(i, out List<SaveFieldViewModel>? list))
            {
                affected.UnionWith(list);
            }
        }

        foreach (SaveFieldViewModel field in affected)
        {
            field.Refresh(keepDraft: ReferenceEquals(field, origin));
            TrackModified(field);
        }

        RaiseChanged();
    }

    public void NotifyAllChanged()
    {
        foreach (SaveFieldViewModel field in Fields)
        {
            field.Refresh(keepDraft: false);
            TrackModified(field);
        }

        RaiseChanged();
    }

    public void RevertField(SaveFieldDefinition definition) => Group(() =>
    {
        if (definition.Kind == SaveFieldKind.Bit)
        {
            uint mask = 1u << (definition.BitIndex ?? 0);
            uint current = ReadUInt32(definition.Offset);
            uint original = BitConverter.ToUInt32(baseline, definition.Offset);
            WriteUInt32(definition.Offset, (current & ~mask) | (original & mask));
            return;
        }

        WriteBytes(definition.Offset, baseline.AsSpan(definition.Offset, definition.Length));
        if (definition.AuxOffset is int aux)
        {
            WriteBytes(aux, baseline.AsSpan(aux, 4));
        }
    }, $"Revert {definition.ShortName}");

    // Modified fields worth listing: a raw view is left out when a friendlier view of the same
    // bytes (a trophy bit, the telemetry toggle) is already listed.
    public IReadOnlyList<SaveFieldViewModel> ChangedFields()
    {
        var result = new List<SaveFieldViewModel>();
        foreach (SaveFieldViewModel field in Fields)
        {
            if (!field.IsModified)
            {
                continue;
            }

            bool covered = field.Definition.Kind is not (SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff) &&
                CoveredOffsets(field.Definition).Any(offset =>
                    fieldsByByte.TryGetValue(offset, out List<SaveFieldViewModel>? overlapping) &&
                    overlapping.Any(other => !ReferenceEquals(other, field) && other.IsModified &&
                        other.Definition.Kind is SaveFieldKind.Bit or SaveFieldKind.ExtensionOnOff));
            if (!covered)
            {
                result.Add(field);
            }
        }

        return result;
    }

    // Back to what is on disk; undoable like any other edit.
    public void Revert() => ApplyExternal(() => Array.Copy(baseline, Bytes, baseline.Length), "Revert all");

    public void MarkSaved()
    {
        baseline = (byte[])Bytes.Clone();
        pendingConversion = false;
        NotifyAllChanged();
    }

    internal bool DiffersFromBaseline(SaveFieldDefinition definition)
    {
        if (definition.Kind == SaveFieldKind.Bit)
        {
            uint mask = 1u << (definition.BitIndex ?? 0);
            return (BitConverter.ToUInt32(Bytes, definition.Offset) & mask) != (BitConverter.ToUInt32(baseline, definition.Offset) & mask);
        }

        if (!Bytes.AsSpan(definition.Offset, definition.Length).SequenceEqual(baseline.AsSpan(definition.Offset, definition.Length)))
        {
            return true;
        }

        return definition.AuxOffset is int aux &&
            !Bytes.AsSpan(aux, 4).SequenceEqual(baseline.AsSpan(aux, 4));
    }

    private void TrackModified(SaveFieldViewModel field)
    {
        if (field.IsModified)
        {
            modifiedFields.Add(field);
        }
        else
        {
            modifiedFields.Remove(field);
        }
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(NeedsWrite));
        OnPropertyChanged(nameof(PendingConversion));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<int> CoveredOffsets(SaveFieldDefinition definition)
    {
        for (int i = 0; i < definition.Length; i++)
        {
            yield return definition.Offset + i;
        }

        if (definition.AuxOffset is int aux)
        {
            for (int i = 0; i < 4; i++)
            {
                yield return aux + i;
            }
        }
    }
}
