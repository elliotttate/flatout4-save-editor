using System.Collections.ObjectModel;
using System.Windows.Input;
using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.ViewModels;

public sealed class PageViewModel
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public IReadOnlyList<PageBlock> Blocks { get; init; } = [];

    public int FieldCount { get; init; }

    public string FieldCountText => FieldCount == 1 ? "1 value" : $"{FieldCount:N0} values";
}

public sealed record BlockAction(string Label, string Glyph, ICommand Command, string Tooltip = "");

// A common one-click edit offered on the Overview; DoneMessage goes to the status bar.
public sealed record QuickAction(string Title, string Glyph, string Description, ICommand Command, string DoneMessage = "");

public abstract class PageBlock : ObservableObject
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public IReadOnlyList<BlockAction> Actions { get; set; } = [];

    public bool HasActions => Actions.Count > 0;

    public string AnchorId { get; } = Guid.NewGuid().ToString("N");
}

public sealed class FieldGroupBlock : PageBlock
{
    public IReadOnlyList<SaveFieldViewModel> Fields { get; init; } = [];
}

public sealed class NoteBlock : PageBlock
{
    private string message = string.Empty;
    private string severity = "Informational";

    public string Message
    {
        get => message;
        set => SetField(ref message, value);
    }

    // InfoBarSeverity name: Informational, Success, Warning, Error.
    public string Severity
    {
        get => severity;
        set => SetField(ref severity, value);
    }
}

public sealed class TrophyGridBlock : PageBlock
{
    public IReadOnlyList<SaveFieldViewModel> Trophies { get; init; } = [];

    public string Progress => $"{Trophies.Count(trophy => trophy.BoolValue)} of {Trophies.Count} unlocked";

    public void Refresh() => OnPropertyChanged(nameof(Progress));
}

public sealed class TableColumn(string header, double width, string tooltip = "")
{
    public string Header { get; } = header;

    public double Width { get; } = width;

    public string Tooltip { get; } = tooltip;
}

public sealed class TableCell(SaveFieldViewModel? field, double width)
{
    public SaveFieldViewModel? Field { get; } = field;

    public double Width { get; } = width;

    public bool HasField => Field is not null;
}

public sealed class TableRowViewModel : ObservableObject
{
    private readonly Func<string>? titleProvider;
    private readonly Func<string>? subtitleProvider;
    private readonly Func<bool>? isEmpty;
    private string title;
    private string subtitle;
    private bool isHighlighted;

    public TableRowViewModel(
        TableBlock table,
        string title,
        IReadOnlyList<SaveFieldViewModel?> fields,
        string subtitle = "",
        Func<bool>? isEmpty = null,
        Func<string>? titleProvider = null,
        Func<string>? subtitleProvider = null)
    {
        Table = table;
        this.titleProvider = titleProvider;
        this.subtitleProvider = subtitleProvider;
        this.isEmpty = isEmpty;
        this.title = titleProvider?.Invoke() ?? title;
        this.subtitle = subtitleProvider?.Invoke() ?? subtitle;
        Cells = fields.Select((field, index) => new TableCell(field, table.Columns[index].Width)).ToArray();
        foreach (SaveFieldViewModel? field in fields)
        {
            if (field is not null)
            {
                field.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SaveFieldViewModel.IsModified))
                    {
                        OnPropertyChanged(nameof(IsModified));
                    }
                };
            }
        }
    }

    public TableBlock Table { get; }

    public IReadOnlyList<TableCell> Cells { get; }


    public string Title
    {
        get => title;
        private set => SetField(ref title, value);
    }

    public string Subtitle
    {
        get => subtitle;
        private set
        {
            if (SetField(ref subtitle, value))
            {
                OnPropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public bool HasSubtitle => !string.IsNullOrEmpty(subtitle);

    public bool IsEmpty => isEmpty?.Invoke() ?? false;

    public bool IsModified => Cells.Any(cell => cell.Field?.IsModified == true);

    public bool IsHighlighted
    {
        get => isHighlighted;
        set => SetField(ref isHighlighted, value);
    }

    public bool HasDynamicText => titleProvider is not null || subtitleProvider is not null;

    public void Revert()
    {
        SaveFieldViewModel[] modified = Cells.Select(cell => cell.Field).OfType<SaveFieldViewModel>().Where(field => field.IsModified).ToArray();
        if (modified.Length > 0)
        {
            modified[0].Session.Group(() =>
            {
                foreach (SaveFieldViewModel field in modified)
                {
                    field.Revert();
                }
            }, $"Revert {Title}");
        }
    }

    public void RefreshText()
    {
        if (titleProvider is not null)
        {
            Title = titleProvider();
        }

        if (subtitleProvider is not null)
        {
            Subtitle = subtitleProvider();
        }
    }

    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Cells.Any(cell => cell.Field is { } field &&
            (field.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             field.ValueLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (field.Editor == FieldEditorKind.Choice && field.SelectedIndex >= 0 &&
              field.Options[field.SelectedIndex].Label.Contains(query, StringComparison.OrdinalIgnoreCase))));
}

public sealed class TableBlock : PageBlock
{
    private const double MinTitleWidth = 140;
    private const double CellSpacing = 12;
    private const double RowPadding = 40;
    private const double RevertButtonWidth = 48;
    private readonly List<TableRowViewModel> rows = [];
    private double titleColumnWidth;
    private bool hideEmpty = true;
    private string filter = string.Empty;
    private string summary = string.Empty;

    public TableBlock(IReadOnlyList<TableColumn> columns, double titleWidth = 260, string titleHeader = "")
    {
        Columns = columns;
        TitleWidth = titleWidth;
        TitleHeader = titleHeader;
        titleColumnWidth = titleWidth;
    }

    public IReadOnlyList<TableColumn> Columns { get; }

    public double TitleWidth { get; }

    public string TitleHeader { get; }

    public IReadOnlyList<TableRowViewModel> AllRows => rows;

    public ObservableCollection<TableRowViewModel> VisibleRows { get; } = [];

    public bool CanHideEmpty { get; init; }

    public bool? HideEmptyChecked
    {
        get => HideEmpty;
        set => HideEmpty = value == true;
    }

    public bool HasNoVisibleRows => VisibleRows.Count == 0;

    // Why the table is empty, and what brings rows back.
    public string EmptyText => !string.IsNullOrWhiteSpace(Filter)
        ? $"No rows match \"{Filter.Trim()}\"."
        : CanHideEmpty && HideEmpty
            ? $"Every row is empty, so all are hidden. Untick \"{HideEmptyLabel}\" to fill them in."
            : "Nothing to show.";

    public string HideEmptyLabel { get; init; } = "Hide empty slots";

    public bool ShowFilter => rows.Count > 12;

    public bool HideEmpty
    {
        get => hideEmpty;
        set
        {
            if (SetField(ref hideEmpty, value))
            {
                OnPropertyChanged(nameof(HideEmptyChecked));
                Refilter();
            }
        }
    }

    public string Filter
    {
        get => filter;
        set
        {
            if (SetField(ref filter, value ?? string.Empty))
            {
                Refilter();
            }
        }
    }

    public string Summary
    {
        get => summary;
        private set => SetField(ref summary, value);
    }

    // The name column as laid out now: TitleWidth when the window has room, narrower (down to
    // MinTitleWidth, names trimmed) so the value columns stay in view instead of scrolling away.
    public double TitleColumnWidth
    {
        get => titleColumnWidth;
        private set
        {
            if (SetField(ref titleColumnWidth, value))
            {
                OnPropertyChanged(nameof(TableWidth));
            }
        }
    }

    public double TableWidth => TitleColumnWidth + ValueColumnsWidth;

    // Value columns, gaps, row padding and the revert button a changed row shows.
    private double ValueColumnsWidth => Columns.Sum(column => column.Width) + (Columns.Count * CellSpacing) + RowPadding + RevertButtonWidth;

    public void FitTo(double availableWidth) =>
        TitleColumnWidth = Math.Clamp(availableWidth - ValueColumnsWidth, Math.Min(MinTitleWidth, TitleWidth), TitleWidth);

    public TableRowViewModel AddRow(
        string title,
        IReadOnlyList<SaveFieldViewModel?> fields,
        string subtitle = "",
        Func<bool>? isEmpty = null,
        Func<string>? titleProvider = null,
        Func<string>? subtitleProvider = null)
    {
        var row = new TableRowViewModel(this, title, fields, subtitle, isEmpty, titleProvider, subtitleProvider);
        rows.Add(row);
        return row;
    }

    public void Refilter()
    {
        string query = filter.Trim();
        IEnumerable<TableRowViewModel> visible = rows;
        if (CanHideEmpty && hideEmpty)
        {
            visible = visible.Where(row => !row.IsEmpty || row.IsModified || row.IsHighlighted);
        }

        if (query.Length > 0)
        {
            visible = visible.Where(row => row.Matches(query));
        }

        TableRowViewModel[] list = visible.ToArray();
        if (!list.SequenceEqual(VisibleRows))
        {
            VisibleRows.Clear();
            foreach (TableRowViewModel row in list)
            {
                VisibleRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasNoVisibleRows));
        OnPropertyChanged(nameof(EmptyText));
        int hidden = rows.Count - list.Length;
        Summary = hidden == 0
            ? $"{rows.Count:N0} rows"
            : $"{list.Length:N0} of {rows.Count:N0} rows";
    }

    public void Reveal(TableRowViewModel row)
    {
        if (!VisibleRows.Contains(row))
        {
            filter = string.Empty;
            OnPropertyChanged(nameof(Filter));
            row.IsHighlighted = true;
            Refilter();
        }
    }
}

public sealed class CareerEventViewModel : ObservableObject
{
    private readonly CareerProgress progress;

    public CareerEventViewModel(Services.FlatOut4GameData.CareerEvent careerEvent, CareerProgress progress)
    {
        Event = careerEvent;
        this.progress = progress;
    }

    public Services.FlatOut4GameData.CareerEvent Event { get; }

    public string Title => Event.DisplayName;

    public string Subtitle => Event.Index;

    public string KindGlyph => Event.Kind switch
    {
        "Cup" => "\uE7C1",
        "Time trial" => "\uE916",
        _ => "\uE7BA"
    };

    public static IReadOnlyList<string> MedalOptions { get; } = ["Not completed", "Bronze", "Silver", "Gold"];

    public int MedalIndex
    {
        get => progress.GetMedalIndex(Event.Id);
        set
        {
            if (value >= 0 && value != MedalIndex)
            {
                progress.SetMedalIndex(Event.Id, value, $"{Event.ClassName} {Title} medal");
            }
        }
    }

    public bool IsNew
    {
        get => progress.GetFlag(Event.Id, popped: false);
        set => progress.SetFlag(Event.Id, popped: false, value);
    }

    public bool IsPopped
    {
        get => progress.GetFlag(Event.Id, popped: true);
        set => progress.SetFlag(Event.Id, popped: true, value);
    }

    public bool HasSlot => progress.FindSlot(Event.Id) >= 0;

    public string MedalGlyphColor => MedalIndex switch
    {
        1 => "#B87333",
        2 => "#9EA7B0",
        3 => "#D4A017",
        _ => "#00000000"
    };

    public bool IsHighlighted { get; set; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(MedalIndex));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsPopped));
        OnPropertyChanged(nameof(HasSlot));
        OnPropertyChanged(nameof(MedalGlyphColor));
    }
}

public sealed class CareerTierGroup(string title, IReadOnlyList<CareerEventViewModel> events)
{
    public string Title { get; } = title;

    public IReadOnlyList<CareerEventViewModel> Events { get; } = events;
}

public sealed class CareerEventsBlock : PageBlock
{
    private IReadOnlyList<CareerEventViewModel> events = [];
    private IReadOnlyList<CareerTierGroup> tiers = [];

    public IReadOnlyList<CareerEventViewModel> Events
    {
        get => events;
        init
        {
            events = value;
            tiers = value
                .GroupBy(careerEvent => careerEvent.Event.Tier)
                .OrderBy(group => group.Key)
                .Select(group => new CareerTierGroup($"Tier {group.Key + 1}", group.ToArray()))
                .ToArray();
        }
    }

    public IReadOnlyList<CareerTierGroup> Tiers => tiers;

    public string Progress => $"{Events.Count(e => e.MedalIndex == 3)} gold · {Events.Count(e => e.MedalIndex > 0)} of {Events.Count} completed";

    public void Refresh()
    {
        foreach (CareerEventViewModel careerEvent in Events)
        {
            careerEvent.Refresh();
        }

        OnPropertyChanged(nameof(Progress));
    }
}

// One unsaved change on the Review changes page.
public sealed class ChangeEntry(SaveFieldViewModel field, string title, string context, SearchResult? location)
{
    public SaveFieldViewModel Field { get; } = field;

    public string Title { get; } = title;

    public string Context { get; } = context;

    public SearchResult? Location { get; } = location;
}

// A tagged m_aOther value this editor does not know; always written back as it is.
public sealed record KeptSlot(string Title, string Value);

public sealed class HealthIssueViewModel(Services.SaveHealthIssue issue, ChangeEntry entry)
{
    public Services.SaveHealthIssue Issue { get; } = issue;

    public ChangeEntry Entry { get; } = entry;

    public string Title => Entry.Title;

    public string Context => Entry.Context;

    public string Problem => Issue.Problem;

    public string RepairText => Issue.RepairText;
}

// One kind of problem; long lists (a thousand debug-fill slots) show their first few entries.
public sealed class HealthGroup(string title, IReadOnlyList<HealthIssueViewModel> issues)
{
    private const int ShownLimit = 8;

    public string Title { get; } = title;

    public string Glyph => Title switch
    {
        Services.FlatOut4SaveHealth.UnknownControl => "\uE765",
        Services.FlatOut4SaveHealth.UninitialisedMemory => "\uE964",
        _ => "\uE7BA"
    };

    public IReadOnlyList<HealthIssueViewModel> Issues { get; } = issues;

    public IReadOnlyList<HealthIssueViewModel> Shown => Issues.Take(ShownLimit).ToArray();

    public string CountText => Issues.Count == 1 ? "1 value" : $"{Issues.Count:N0} values";

    public string RepairLabel => Issues.Count == 1 ? "Repair" : $"Repair {Issues.Count:N0}";

    public string MoreText => Issues.Count > ShownLimit ? $"…and {Issues.Count - ShownLimit:N0} more like these. Repair fixes them all." : string.Empty;

    public bool HasMore => Issues.Count > ShownLimit;
}

public sealed class ChangeGroup(string title, string glyph, IReadOnlyList<ChangeEntry> entries)
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public IReadOnlyList<ChangeEntry> Entries { get; } = entries;

    public string CountText => Entries.Count == 1 ? "1 change" : $"{Entries.Count:N0} changes";
}

// One pill in a page's "jump to" strip.
public sealed class JumpTarget(PageBlock block) : ObservableObject
{
    private bool isActive;

    public PageBlock Block { get; } = block;

    public string Title => Block.Title;

    public bool IsActive
    {
        get => isActive;
        set => SetField(ref isActive, value);
    }
}

public sealed class SearchResult
{
    public string Title { get; init; } = string.Empty;

    public string Context { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public PageViewModel Page { get; init; } = null!;

    public PageBlock? Block { get; init; }

    public object? Target { get; init; }

    public string SearchText { get; init; } = string.Empty;

    public int Rank { get; init; }

    public override string ToString() => Title;
}
