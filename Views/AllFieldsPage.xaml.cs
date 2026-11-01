using System.Collections.ObjectModel;
using FlatOut4SaveEditor.Models;
using Microsoft.UI.Xaml.Navigation;

namespace FlatOut4SaveEditor.Views;

public sealed partial class AllFieldsPage : Page
{
    private readonly ObservableCollection<SaveFieldViewModel> visible = [];
    private SaveSession? session;
    private CancellationTokenSource? pendingFilter;

    public AllFieldsPage()
    {
        InitializeComponent();
        FieldsList.ItemsSource = visible;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        session = (e.Parameter as MainPage)?.Session;
        string[] sections = ["All sections", .. (session?.Fields.Select(field => field.Section).Distinct() ?? [])];
        SectionBox.ItemsSource = sections;
        SectionBox.SelectedIndex = 0;
        ApplyFilter();
    }

    private async void OnFilterChanged(object sender, object e)
    {
        // Debounce typing: the list holds thousands of rows.
        pendingFilter?.Cancel();
        var cancellation = new CancellationTokenSource();
        pendingFilter = cancellation;
        try
        {
            await Task.Delay(ReferenceEquals(sender, FilterBox) ? 180 : 0, cancellation.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        ApplyFilter();
    }

    private void OnRevertFieldClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SaveFieldViewModel field })
        {
            field.Revert();
        }
    }

    private void ApplyFilter()
    {
        if (session is null)
        {
            visible.Clear();
            CountText.Text = string.Empty;
            return;
        }

        string query = FilterBox.Text.Trim();
        string? section = SectionBox.SelectedIndex > 0 ? SectionBox.SelectedItem as string : null;
        bool modifiedOnly = ModifiedOnlyBox.IsChecked == true;

        IEnumerable<SaveFieldViewModel> fields = session.Fields;
        if (section is not null)
        {
            fields = fields.Where(field => field.Section == section);
        }

        if (modifiedOnly)
        {
            fields = fields.Where(field => field.IsModified);
        }

        if (query.Length > 0)
        {
            fields = fields.Where(field =>
                field.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                field.RawName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                field.Offset.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                field.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                field.ValueLabel.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        visible.Clear();
        foreach (SaveFieldViewModel field in fields)
        {
            visible.Add(field);
        }

        CountText.Text = $"{visible.Count:N0} of {session.Fields.Count:N0} fields";
    }
}
