using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Navigation;

namespace FlatOut4SaveEditor.Views;

public sealed partial class ChangesPage : Page
{
    private MainPage? shell;
    private SaveSession? session;
    private bool refreshQueued;

    public ChangesPage()
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

        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (session is not null)
        {
            session.DataChanged -= OnDataChanged;
        }

        Ui.ReleaseBindings(this);
    }

    // Reverting from this page changes the data; coalesce bursts (Revert all) into one rebuild.
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
        IReadOnlyList<ChangeGroup> groups = shell?.Catalog?.ChangeGroups() ?? [];
        int changes = groups.Sum(group => group.Entries.Count);
        GroupsRepeater.ItemsSource = groups;
        EmptyState.Visibility = changes == 0 ? Visibility.Visible : Visibility.Collapsed;
        RevertAllButton.IsEnabled = changes > 0;
        SummaryText.Text = changes == 0
            ? "Everything matches the file on disk."
            : $"{Ui.Count(changes, "change", "changes")} in {Ui.Count(groups.Count, "section", "sections")}, compared with the file on disk. Select one to jump to it.";

        bool converted = session?.PendingConversion == true;
        ConversionNote.IsOpen = converted;
        ConversionNote.Visibility = converted ? Visibility.Visible : Visibility.Collapsed;
        ConversionNote.Message = converted
            ? $"This save is read from V{session!.Document.OriginalVersion}. Saving writes it in the current layout even if no value below changed."
            : string.Empty;
    }

    private void OnEntryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChangeEntry entry } && shell is not null)
        {
            if (entry.Location is { } location)
            {
                shell.Navigate(location.Page.Key, location);
            }
            else
            {
                shell.Navigate(EditorCatalog.AllFieldsKey);
            }
        }
    }

    private void OnRevertEntryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChangeEntry entry })
        {
            entry.Field.Revert();
        }
    }

    private void OnRevertAllClicked(object sender, RoutedEventArgs e) => _ = shell?.RevertAsync();
}
