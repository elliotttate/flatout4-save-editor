using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Xaml.Navigation;

namespace FlatOut4SaveEditor.Views;

public sealed partial class OverviewPage : Page
{
    private const int TestSlotPreviewCount = 4;
    private MainPage? shell;

    public OverviewPage()
    {
        InitializeComponent();
    }

    public static string KindGlyph(SaveLocationKind kind) => kind switch
    {
        SaveLocationKind.SteamCloud => "\uE753",
        SaveLocationKind.Local => "\uE8B7",
        SaveLocationKind.TestSlot => "\uEC7A",
        _ => "\uE8A5"
    };

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        shell = e.Parameter as MainPage;
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Ui.ReleaseBindings(this);
    }

    public void Render()
    {
        OverviewModel? overview = shell?.Overview;
        WelcomePanel.Visibility = overview is null ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = overview is null ? Visibility.Collapsed : Visibility.Visible;

        if (overview is null)
        {
            RenderRecent();
            RenderCandidates(shell?.Candidates ?? []);
            return;
        }

        FileNameText.Text = overview.FileName;
        FilePathText.Text = PathDisplay.CompactFolder(overview.FilePath);
        Ui.SetToolTipText(FilePathText, overview.FilePath);
        SourceGlyph.Glyph = KindGlyph(overview.Session.Origin?.Kind ?? GuessKind(overview.FilePath));
        ChipsRepeater.ItemsSource = new[]
        {
            overview.SourceLabel,
            overview.VersionLabel,
            overview.SizeLabel,
            overview.ModifiedLabel,
            overview.ExtensionLabel
        }.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();

        bool needsRepair = overview.HealthIssueCount > 0;
        HealthText.Text = overview.HealthLabel;
        HealthGlyph.Glyph = needsRepair ? "\uE7BA" : overview.Session.Document.Recovery is null ? "\uE73E" : "\uE95E";
        HealthGlyph.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[needsRepair ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush"];

        TilesRepeater.ItemsSource = overview.Tiles;
        QuickActionsRepeater.ItemsSource = QuickActions();
        SectionsRepeater.ItemsSource = overview.Sections;
    }

    private static SaveLocationKind GuessKind(string path) =>
        path.Contains(@"\userdata\", StringComparison.OrdinalIgnoreCase) ? SaveLocationKind.SteamCloud
        : System.IO.Path.GetFileName(path).StartsWith("DEBUGSave", StringComparison.OrdinalIgnoreCase) ? SaveLocationKind.TestSlot
        : SaveLocationKind.Local;

    private void RenderCandidates(IReadOnlyList<FlatOut4SaveCandidate> candidates)
    {
        CandidatesPanel.Children.Clear();
        NoCandidatesText.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (candidates.Count == 0)
        {
            NoCandidatesText.Text = shell?.HasScanned == true
                ? "No FlatOut saves were found in Steam Cloud or the usual save folders. Use Browse to open a file from somewhere else."
                : "Looking for saves...";
            return;
        }

        foreach (IGrouping<SaveLocationKind, FlatOut4SaveCandidate> group in candidates.GroupBy(candidate => candidate.Kind).OrderBy(group => group.Key))
        {
            FlatOut4SaveCandidate[] items = group.OrderByDescending(candidate => candidate.LastWriteTimeUtc).ToArray();
            var section = new StackPanel { Spacing = 8 };
            section.Children.Add(new TextBlock
            {
                Text = $"{GroupTitle(group.Key)} ({items.Length})",
                Style = (Style)Application.Current.Resources["BlockTitleStyle"]
            });

            if (group.Key == SaveLocationKind.TestSlot && items.Length > TestSlotPreviewCount)
            {
                section.Children.Add(MakeList(items.Take(TestSlotPreviewCount)));
                var expander = new Expander
                {
                    Header = $"Show {items.Length - TestSlotPreviewCount} older test slots",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = MakeList(items.Skip(TestSlotPreviewCount))
                };
                section.Children.Add(expander);
            }
            else
            {
                section.Children.Add(MakeList(items));
            }

            CandidatesPanel.Children.Add(section);
        }
    }

    private void RenderRecent()
    {
        IReadOnlyList<RecentSave> recent = FlatOut4RecentFiles.Load();
        RecentRepeater.ItemsSource = recent;
        RecentPanel.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRecentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecentSave recent })
        {
            _ = shell?.OpenAsync(recent.Path, null);
        }
    }

    private void OnClearRecentClicked(object sender, RoutedEventArgs e)
    {
        FlatOut4RecentFiles.Clear();
        RenderRecent();
    }

    private ItemsRepeater MakeList(IEnumerable<FlatOut4SaveCandidate> items) => new()
    {
        ItemsSource = items.ToArray(),
        ItemTemplate = (DataTemplate)Resources["CandidateTemplate"],
        Layout = new StackLayout { Spacing = 6 }
    };

    private static string GroupTitle(SaveLocationKind kind) => kind switch
    {
        SaveLocationKind.SteamCloud => "Steam Cloud",
        SaveLocationKind.Local => "Local saves",
        SaveLocationKind.TestSlot => "Developer test slots",
        _ => "Other files"
    };

    private void OnCandidateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FlatOut4SaveCandidate candidate })
        {
            _ = shell?.OpenAsync(candidate.Path, candidate);
        }
    }

    private void OnTileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            shell?.Navigate(key);
        }
    }

    private void OnSectionClicked(object sender, RoutedEventArgs e) => OnTileClicked(sender, e);

    private void OnFindClicked(object sender, RoutedEventArgs e) => _ = shell?.FindSavesAsync(openSingle: false);

    private void OnBrowseClicked(object sender, RoutedEventArgs e) => _ = shell?.BrowseAsync();

    private void OnRevealClicked(object sender, RoutedEventArgs e) => shell?.RevealInExplorer();

    private void OnHealthClicked(object sender, RoutedEventArgs e) => shell?.Navigate(EditorCatalog.HealthKey);

    // Unlock everything (the game's debug Unlock All) first, then the catalog's single edits.
    private IReadOnlyList<QuickAction> QuickActions() =>
    [
        new QuickAction(
            "Unlock everything",
            "\uE785",
            "Gold medals, the whole garage and maxed challenges, like the game's debug Unlock All.",
            new RelayCommand(() => _ = shell?.UnlockAllAsync())),
        .. shell?.Catalog?.QuickActions ?? []
    ];

    private void OnQuickActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickAction action })
        {
            action.Command.Execute(null);
            if (!string.IsNullOrEmpty(action.DoneMessage))
            {
                shell?.ShowStatus($"{action.DoneMessage} Ctrl+Z undoes it.", InfoBarSeverity.Success, autoHide: true);
            }
        }
    }
}
