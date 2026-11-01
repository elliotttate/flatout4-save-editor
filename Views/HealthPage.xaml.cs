using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Navigation;

namespace FlatOut4SaveEditor.Views;

public sealed partial class HealthPage : Page
{
    private const int KeptShown = 12;

    private MainPage? shell;
    private SaveSession? session;
    private IReadOnlyList<SaveHealthIssue> issues = [];
    private bool refreshQueued;

    public HealthPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        shell = e.Parameter as MainPage;
        session = shell?.Session;
        if (session is not null)
        {
            session.DataChanged += OnDataChanged;
        }

        RenderRecovery();
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (session is not null)
        {
            session.DataChanged -= OnDataChanged;
        }
    }

    // Repairs, edits and undo all change what is healthy; coalesce bursts into one rebuild.
    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (refreshQueued)
        {
            return;
        }

        refreshQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            refreshQueued = false;
            Render();
        });
    }

    private void Render()
    {
        if (shell?.Catalog is not { } catalog || session is null)
        {
            return;
        }

        issues = catalog.HealthIssues();
        IReadOnlyList<HealthGroup> groups = catalog.HealthGroups(issues);
        GroupsRepeater.ItemsSource = groups;
        HealthyState.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RepairAllButton.IsEnabled = issues.Count > 0;
        SummaryText.Text = issues.Count == 0
            ? "Every value is one the game writes itself."
            : $"{Ui.Count(issues.Count, "value is", "values are")} not something the game writes. The game loads them as they are; repairing sets each one to what a new profile has.";

        KeptSlot[] kept = FlatOut4SaveHealth.UnknownExtensionSlots(session.Bytes, session.Schema)
            .Select(slot => new KeptSlot($"Slot {slot.Slot}", $"0x{slot.Value:X8}  ({unchecked((int)slot.Value)})"))
            .ToArray();
        KeptRepeater.ItemsSource = kept.Take(KeptShown).ToArray();
        KeptCount.Text = Ui.Count(kept.Length, "slot", "slots");
        KeptMore.Text = kept.Length > KeptShown ? $"…and {kept.Length - KeptShown:N0} more. All fields lists every slot." : string.Empty;
        KeptMore.Visibility = kept.Length > KeptShown ? Visibility.Visible : Visibility.Collapsed;
        KeptSection.Visibility = kept.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // How the file was read only changes when another file is opened.
    private void RenderRecovery()
    {
        FlatOut4SaveRecoveryReport? report = session?.Document.Recovery;
        RecoveryCard.Visibility = report is null ? Visibility.Collapsed : Visibility.Visible;
        if (report is null)
        {
            return;
        }

        RecoveryTitle.Text = report.Title;
        RecoverySummary.Text = report.Summary;
        RecoveryNotes.ItemsSource = report.Notes;
        RecoveryIcon.Glyph = report.PreservesLayout ? "" : "";
        RecoveryFootnote.Text = report.CheckedValues > 0
            ? $"Matched against the V{FlatOut4SaveSchema.CurrentVersion} layout: {report.CheckedValues:N0} values checked, {report.ImplausibleValues:N0} looked wrong. The automatic backup keeps the original file."
            : "The automatic backup keeps the original file.";
    }

    private void OnShowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HealthIssueViewModel issue } && shell is not null)
        {
            if (issue.Entry.Location is { } location)
            {
                shell.Navigate(location.Page.Key, location);
            }
            else
            {
                shell.Navigate(EditorCatalog.AllFieldsKey);
            }
        }
    }

    private void OnRepairIssueClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HealthIssueViewModel issue })
        {
            Repair([issue.Issue], $"Repair {issue.Issue.Field.ShortName}");
        }
    }

    private void OnRepairGroupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HealthGroup group })
        {
            Repair(group.Issues.Select(issue => issue.Issue).ToArray(), $"Repair {group.CountText}");
        }
    }

    private void OnRepairAllClicked(object sender, RoutedEventArgs e) => Repair(issues, $"Repair {Ui.Count(issues.Count, "value", "values")}");

    private void Repair(IReadOnlyList<SaveHealthIssue> repairs, string label)
    {
        if (shell?.Catalog is not { } catalog || repairs.Count == 0)
        {
            return;
        }

        catalog.Repair(repairs, label);
        shell.ShowStatus($"Repaired {Ui.Count(repairs.Count, "value", "values")}. Save to write the file; Ctrl+Z undoes it.", InfoBarSeverity.Success, autoHide: true);
    }
}
