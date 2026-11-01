using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace FlatOut4SaveEditor.Views;

public sealed partial class MainPage : Page
{
    private readonly FlatOut4SaveSchema schema = FlatOut4SaveSchema.Create();
    private readonly Dictionary<string, NavigationViewItem> navItems = [];
    private readonly Dictionary<string, InfoBadge> navBadges = [];
    private CancellationTokenSource? statusTimer;
    private int expectedChanges;

    public MainPage()
    {
        InitializeComponent();
        BuildNavigation();
        Loaded += OnLoaded;
    }

    public SaveSession? Session { get; private set; }

    public EditorCatalog? Catalog { get; private set; }

    public OverviewModel? Overview { get; private set; }

    public IReadOnlyList<FlatOut4SaveCandidate> Candidates { get; private set; } = [];

    public bool HasScanned { get; private set; }

    public UIElement TitleBarElement => AppTitleBar;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        App.SnapshotLog("MainPage loaded");
        Navigate(EditorCatalog.OverviewKey);

        if (App.StartupPath is { } startupPath)
        {
            await OpenAsync(startupPath, null);
        }

        await ScanAsync();

        if (App.SnapshotDirectory is { } snapshotDirectory)
        {
            await RunSnapshotAsync(snapshotDirectory);
        }
    }

    private void BuildNavigation()
    {
        string category = string.Empty;
        foreach (EditorCatalog.SectionInfo info in EditorCatalog.Sections)
        {
            if (info.Category != category && info.Category.Length > 0)
            {
                Nav.MenuItems.Add(new NavigationViewItemHeader { Content = info.Category });
            }

            category = info.Category;
            var item = new NavigationViewItem
            {
                Content = info.Title,
                Tag = info.Key,
                Icon = new FontIcon { Glyph = info.Glyph }
            };
            Ui.SetToolTipText(item, info.Subtitle);
            navItems[info.Key] = item;
            Nav.MenuItems.Add(item);
        }

        UpdateChrome();
    }

    public void Navigate(string key, SearchResult? target = null)
    {
        if (key != EditorCatalog.OverviewKey && Catalog is null)
        {
            key = EditorCatalog.OverviewKey;
        }

        if (navItems.TryGetValue(key, out NavigationViewItem? item) && !ReferenceEquals(Nav.SelectedItem, item))
        {
            Nav.SelectedItem = item;

            // Search, Ctrl+Tab and links can open a section whose item is scrolled out of a short pane.
            item.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }

        if (target is not null && ContentFrame.Content is SectionPage current && current.ViewModel?.Key == key)
        {
            _ = current.RevealAsync(target);
            return;
        }

        var transition = new EntranceNavigationTransitionInfo();
        if (key == EditorCatalog.OverviewKey)
        {
            ContentFrame.Navigate(typeof(OverviewPage), this, transition);
        }
        else if (key == EditorCatalog.AllFieldsKey)
        {
            object parameter = target?.Target is SaveFieldViewModel field ? new AllFieldsNavigation(this, field.RawName) : this;
            ContentFrame.Navigate(typeof(AllFieldsPage), parameter, transition);
        }
        else if (key == EditorCatalog.ChangesKey)
        {
            ContentFrame.Navigate(typeof(ChangesPage), this, transition);
        }
        else if (key == EditorCatalog.HealthKey)
        {
            // The page shows the whole load report, so the banner would only repeat it.
            WarningBar.IsOpen = false;
            ContentFrame.Navigate(typeof(HealthPage), this, transition);
        }
        else if (Catalog?.Page(key) is { } page)
        {
            ContentFrame.Navigate(typeof(SectionPage), new PageNavigation(page, target, this), transition);
        }

        ContentFrame.BackStack.Clear();
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string key)
        {
            Navigate(key);
        }
    }

    public async Task OpenAsync(string path, FlatOut4SaveCandidate? candidate)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            candidate ??= Candidates.FirstOrDefault(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            (SaveSession session, EditorCatalog catalog, OverviewModel overview) = await Task.Run(() =>
            {
                FlatOut4SaveDocument document = FlatOut4SaveFile.Load(fullPath, schema);
                var newSession = new SaveSession(document, schema, candidate);
                var newCatalog = new EditorCatalog(newSession);
                return (newSession, newCatalog, new OverviewModel(newSession, newCatalog));
            });

            AttachSession(session, catalog, overview);
            FlatOut4RecentFiles.Add(fullPath);
            ShowLoadReport(session, catalog);
            Navigate(EditorCatalog.OverviewKey);
        }
        catch (Exception ex)
        {
            App.SnapshotLog(ex.ToString());
            await ShowMessageAsync("Could not open the save", ex.Message);
        }
    }

    // What happened while reading: a conversion, a recovery, or values the game cannot use.
    private void ShowLoadReport(SaveSession session, EditorCatalog catalog)
    {
        int issues = catalog.HealthIssues().Count;
        string warning = session.Document.Warning;
        FlatOut4SaveRecoveryReport? recovery = session.Document.Recovery;
        if (recovery is null && issues > 0)
        {
            warning = string.IsNullOrWhiteSpace(warning) ? string.Empty : warning + " ";
            warning += $"{Ui.Count(issues, "value in this save is", "values in this save are")} not something the game writes. Save health shows them and can repair them.";
        }

        WarningBar.Title = recovery?.Title ?? (issues > 0 ? "Damaged values" : "Converted on load");
        WarningBar.Message = warning;
        WarningBar.Severity = recovery is not null || issues > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        WarningBar.ActionButton = recovery is not null || issues > 0 ? HealthDetailsButton() : null;
        WarningBar.IsOpen = !string.IsNullOrWhiteSpace(warning);
        if (!WarningBar.IsOpen)
        {
            ShowStatus($"Opened {session.FileName}.", InfoBarSeverity.Success, autoHide: true);
        }
    }

    private Button HealthDetailsButton()
    {
        var button = new Button { Content = "Save health" };
        button.Click += (_, _) => Navigate(EditorCatalog.HealthKey);
        return button;
    }

    private void AttachSession(SaveSession session, EditorCatalog catalog, OverviewModel overview)
    {
        if (Session is not null)
        {
            Session.PropertyChanged -= OnSessionPropertyChanged;
            Catalog?.Detach();
        }

        Session = session;
        Catalog = catalog;
        Overview = overview;
        session.PropertyChanged += OnSessionPropertyChanged;
        SearchBox.Text = string.Empty;
        UpdateChrome();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChrome();

    private void UpdateChrome()
    {
        bool hasSession = Session is not null;
        foreach ((string key, NavigationViewItem item) in navItems)
        {
            item.IsEnabled = hasSession || key == EditorCatalog.OverviewKey;
        }

        UpdateBadges();
        SearchBox.IsEnabled = hasSession;
        UnlockAllButton.IsEnabled = hasSession;
        SaveAsItem.IsEnabled = hasSession;
        ReloadItem.IsEnabled = hasSession;
        RevealItem.IsEnabled = hasSession;
        RevertButton.IsEnabled = Session?.HasChanges == true;
        UndoButton.IsEnabled = Session?.CanUndo == true;
        RedoButton.IsEnabled = Session?.CanRedo == true;
        Ui.SetToolTipText(UndoButton, Session?.UndoLabel is { } undo ? $"Undo {undo} (Ctrl+Z)" : "Undo (Ctrl+Z)");
        Ui.SetToolTipText(RedoButton, Session?.RedoLabel is { } redo ? $"Redo {redo} (Ctrl+Y)" : "Redo (Ctrl+Y)");
        SaveButton.IsEnabled = Session?.NeedsWrite == true;

        if (Session is null)
        {
            ChangesPill.Visibility = Visibility.Collapsed;
            TitleFileText.Text = string.Empty;
            return;
        }

        int changes = Session.ModifiedCount;
        Visibility pill = Session.NeedsWrite ? Visibility.Visible : Visibility.Collapsed;
        if (ChangesPill.Visibility != pill)
        {
            ChangesPill.Visibility = pill;
            DispatcherQueue.TryEnqueue(UpdateToolbarDensity);
        }
        ChangesText.Text = changes > 0
            ? (changes == 1 ? "1 unsaved change" : $"{changes:N0} unsaved changes")
            : "Converted, not saved yet";

        string source = Session.Origin?.KindLabel ?? string.Empty;
        TitleFileText.Text = $"{(Session.HasChanges ? "* " : string.Empty)}{Session.FileName}{(source.Length > 0 ? $"  ·  {source}" : string.Empty)}";
        App.MainWindow.Title = $"{(Session.HasChanges ? "* " : string.Empty)}{Session.FileName} - FlatOut 4 Save Editor";
    }

    // A count on each section that holds unsaved edits.
    private void UpdateBadges()
    {
        foreach ((string key, NavigationViewItem item) in navItems)
        {
            string subtitle = NavToolTip(key);
            if (key is EditorCatalog.OverviewKey or EditorCatalog.AllFieldsKey)
            {
                Ui.SetToolTipText(item, subtitle);
                continue;
            }

            if (key == EditorCatalog.HealthKey)
            {
                int issues = Catalog?.HealthIssues().Count ?? 0;
                item.InfoBadge = issues == 0 ? null : HealthBadge(issues);
                Ui.SetToolTipText(item, issues == 0 ? subtitle : $"{subtitle}\n{Ui.Count(issues, "value needs", "values need")} repair");
                continue;
            }

            int count = key == EditorCatalog.ChangesKey ? Catalog?.ChangeCount ?? 0 : Catalog?.ModifiedCount(key) ?? 0;
            if (count == 0)
            {
                item.InfoBadge = null;
                Ui.SetToolTipText(item, subtitle);
                continue;
            }

            if (!navBadges.TryGetValue(key, out InfoBadge? badge))
            {
                badge = new InfoBadge();
                navBadges[key] = badge;
            }

            SetBadgeCount(badge, count);
            item.InfoBadge = badge;
            Ui.SetToolTipText(item, $"{subtitle}\n{Ui.Count(count, "unsaved change", "unsaved changes")}");
        }
    }

    // What a section is for. The compact pane shows icons only, so there the tooltip leads with its name.
    private string NavToolTip(string key)
    {
        EditorCatalog.SectionInfo info = EditorCatalog.Sections.First(section => section.Key == key);
        return Nav.DisplayMode == NavigationViewDisplayMode.Expanded ? info.Subtitle : $"{info.Title}\n{info.Subtitle}";
    }

    // In the compact pane a count past 99 would cover the icon; a dot plus the tooltip says enough there.
    private void SetBadgeCount(InfoBadge badge, int count) =>
        badge.Value = count > 99 && Nav.DisplayMode != NavigationViewDisplayMode.Expanded ? -1 : count;

    private InfoBadge HealthBadge(int issues)
    {
        if (!navBadges.TryGetValue(EditorCatalog.HealthKey, out InfoBadge? badge))
        {
            badge = new InfoBadge();
            if (Application.Current.Resources.TryGetValue("CautionValueInfoBadgeStyle", out object? style) && style is Style caution)
            {
                badge.Style = caution;
            }

            navBadges[EditorCatalog.HealthKey] = badge;
        }

        SetBadgeCount(badge, issues);
        return badge;
    }

    private async Task ScanAsync()
    {
        try
        {
            Candidates = await Task.Run(() => FlatOut4SaveFile.FindSaveCandidates(schema));
        }
        catch (Exception ex)
        {
            Candidates = [];
            ShowStatus($"Could not scan for saves: {ex.Message}", InfoBarSeverity.Warning);
        }

        HasScanned = true;
        App.SnapshotLog($"scan: {Candidates.Count} candidates");
        if (ContentFrame.Content is OverviewPage overviewPage)
        {
            overviewPage.Render();
        }
    }

    public async Task FindSavesAsync(bool openSingle)
    {
        await ScanAsync();
        if (Candidates.Count == 0)
        {
            string locations = string.Join(Environment.NewLine, FlatOut4SaveFile.GetCheckedSaveLocations());
            await ShowMessageAsync(
                "No FlatOut saves found",
                $"Checked Steam Cloud, the usual save folders and developer test slots:{Environment.NewLine}{Environment.NewLine}{locations}{Environment.NewLine}{Environment.NewLine}Use Open to browse to a file somewhere else.");
            return;
        }

        if (Session is null && !openSingle)
        {
            Navigate(EditorCatalog.OverviewKey);
            ShowStatus($"Found {Candidates.Count:N0} saves. Pick one below.", InfoBarSeverity.Informational, autoHide: true);
            return;
        }

        FlatOut4SaveCandidate? chosen = Candidates.Count == 1 && openSingle ? Candidates[0] : await PickCandidateAsync();
        if (chosen is not null)
        {
            await OpenAsync(chosen.Path, chosen);
        }
    }

    public async Task BrowseAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await OpenAsync(file.Path, null);
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (Session?.Document.Path is not { } path)
        {
            return false;
        }

        SaveFieldViewModel? invalid = Session.Fields.FirstOrDefault(field => field.HasError);
        if (invalid is not null)
        {
            await ShowMessageAsync("Fix invalid values first", $"{invalid.FullName}: {invalid.Error}");
            return false;
        }

        try
        {
            string? backup = FlatOut4SaveFile.Save(Session.Document, path, BackupToggle.IsOn);
            Session.MarkSaved();
            WarningBar.IsOpen = false;
            ShowStatus(
                backup is null ? $"Saved {Session.FileName}." : $"Saved {Session.FileName}. Previous file kept as {Path.GetFileName(backup)}.",
                InfoBarSeverity.Success,
                autoHide: true);
            RefreshOverviewPage();
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Save failed", ex.Message);
            return false;
        }
    }

    private async Task SaveAsAsync()
    {
        if (Session is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(Session.FileName) + " copy"
        };
        picker.FileTypeChoices.Add("FlatOut save", [".dat"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            FlatOut4SaveFile.Save(Session.Document, file.Path, createBackup: false);
            Session.MarkSaved();
            FlatOut4RecentFiles.Add(file.Path);
            ShowStatus($"Saved a copy to {file.Path}. Further saves go to the copy.", InfoBarSeverity.Success, autoHide: true);
            UpdateChrome();
            RefreshOverviewPage();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Save failed", ex.Message);
        }
    }

    public async Task RevertAsync()
    {
        if (Session?.HasChanges != true)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Revert all changes?",
            Content = $"{Ui.Count(Session.ModifiedCount, "value goes", "values go")} back to what was loaded from {Session.FileName}.",
            PrimaryButtonText = "Revert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Session.Revert();
            ShowStatus("Changes reverted.", InfoBarSeverity.Informational, autoHide: true);
        }
    }

    public async Task UnlockAllAsync()
    {
        if (Session is null)
        {
            return;
        }

        try
        {
            FlatOut4UnlockAllResult? unlocked = null;
            Session.ApplyExternal(() => unlocked = FlatOut4SaveUnlocker.ApplyDebugUnlockAll(Session.Document, schema), "Unlock all");
            FlatOut4UnlockAllResult result = unlocked!;
            ShowStatus(
                result.Changed
                    ? $"Unlock All: {result.CareerEventsCompleted:N0} career events at gold, {result.GarageEntriesUnlocked:N0} garage entries unlocked, {result.ChallengeValuesMaxed:N0} challenge values maxed. Save to write the file."
                    : "Everything Unlock All touches was already unlocked.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Unlock All failed", ex.Message);
        }
    }

    public void RevealInExplorer()
    {
        if (Session?.Document.Path is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    // Returns false when the user cancels; true when it is fine to drop the session.
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Session?.HasChanges != true)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = $"Save changes to {Session.FileName}?",
            Content = $"{Ui.Count(Session.ModifiedCount, "value has", "values have")} changed since the file was opened.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await SaveAsync(),
            ContentDialogResult.Secondary => true,
            _ => false
        };
    }

    private void RefreshOverviewPage()
    {
        if (ContentFrame.Content is OverviewPage overviewPage)
        {
            overviewPage.Render();
        }
    }

    private async Task<FlatOut4SaveCandidate?> PickCandidateAsync()
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 460
        };

        foreach (IGrouping<SaveLocationKind, FlatOut4SaveCandidate> group in Candidates.GroupBy(candidate => candidate.Kind).OrderBy(group => group.Key))
        {
            list.Items.Add(new ListViewItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = group.First().KindLabel,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    Margin = new Thickness(0, 8, 0, 0)
                }
            });

            foreach (FlatOut4SaveCandidate candidate in group.OrderByDescending(item => item.LastWriteTimeUtc))
            {
                var content = new StackPanel { Spacing = 1, Padding = new Thickness(0, 4, 0, 4) };
                content.Children.Add(new TextBlock { Text = candidate.Title, FontWeight = FontWeights.SemiBold });
                content.Children.Add(new TextBlock { Text = candidate.Details, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
                content.Children.Add(new TextBlock
                {
                    Text = candidate.Path,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                list.Items.Add(new ListViewItem { Content = content, Tag = candidate });
            }
        }

        list.SelectedIndex = 1;
        var dialog = new ContentDialog
        {
            Title = "Open a save",
            Content = list,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is ListViewItem { Tag: FlatOut4SaveCandidate chosen }
            ? chosen
            : null;
    }

    public void ShowStatus(string message, InfoBarSeverity severity, bool autoHide = false)
    {
        statusTimer?.Cancel();
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;

        if (!autoHide)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        statusTimer = cancellation;
        _ = HideStatusLaterAsync(cancellation.Token);
    }

    private async Task HideStatusLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(6000, token);
            StatusBar.IsOpen = false;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (App.SnapshotDirectory is not null)
        {
            App.SnapshotLog($"{title}: {message}");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };

        _ = await dialog.ShowAsync();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = Catalog?.Search(sender.Text) ?? [];
        }
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        SearchResult? result = args.ChosenSuggestion as SearchResult ?? Catalog?.Search(args.QueryText).FirstOrDefault();
        if (result is null)
        {
            if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                ShowStatus($"Nothing matches \"{args.QueryText.Trim()}\". Try All fields for raw names.", InfoBarSeverity.Informational, autoHide: true);
            }

            return;
        }

        Navigate(result.Page.Key, result.Block is null ? null : result);
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e) => _ = BrowseAsync();

    private void OnChangesPillClicked(object sender, RoutedEventArgs e) => Navigate(EditorCatalog.ChangesKey);

    private void OnFindClicked(object sender, RoutedEventArgs e) => _ = FindSavesAsync(openSingle: false);

    private void OnSaveClicked(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void OnSaveAsClicked(object sender, RoutedEventArgs e) => _ = SaveAsAsync();

    private void OnRevertClicked(object sender, RoutedEventArgs e) => _ = RevertAsync();

    private void OnUnlockAllClicked(object sender, RoutedEventArgs e) => _ = UnlockAllAsync();

    private void OnUndoClicked(SplitButton sender, SplitButtonClickEventArgs args) => Undo();

    private const int UndoHistoryLength = 15;

    // The arrow beside Undo lists recent changes, newest first; picking one undoes back to
    // before that change.
    private void OnUndoHistoryOpening(object? sender, object e)
    {
        UndoHistoryFlyout.Items.Clear();
        IReadOnlyList<string> labels = Session?.UndoLabels(UndoHistoryLength) ?? [];
        for (int i = 0; i < labels.Count; i++)
        {
            int steps = i + 1;
            var item = new MenuFlyoutItem { Text = labels[i] };
            item.Click += (_, _) => UndoSteps(steps);
            UndoHistoryFlyout.Items.Add(item);
        }

        if (labels.Count == 0)
        {
            UndoHistoryFlyout.Items.Add(new MenuFlyoutItem { Text = "Nothing to undo", IsEnabled = false });
        }
    }

    public void UndoSteps(int steps)
    {
        string? last = null;
        int undone = 0;
        while (undone < steps && Session?.Undo() is { } label)
        {
            last = label;
            undone++;
        }

        if (undone > 0)
        {
            ShowStatus(undone == 1 ? $"Undid {last}." : $"Undid {undone} changes, back to before {last}.", InfoBarSeverity.Informational, autoHide: true);
        }
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e) => Redo();

    public void Undo()
    {
        if (Session?.Undo() is { } label)
        {
            ShowStatus($"Undid {label}.", InfoBarSeverity.Informational, autoHide: true);
        }
    }

    public void Redo()
    {
        if (Session?.Redo() is { } label)
        {
            ShowStatus($"Redid {label}.", InfoBarSeverity.Informational, autoHide: true);
        }
    }

    private void OnReloadClicked(object sender, RoutedEventArgs e)
    {
        if (Session?.Document.Path is { } path)
        {
            _ = OpenAsync(path, Session.Origin);
        }
    }

    private void OnRevealClicked(object sender, RoutedEventArgs e) => RevealInExplorer();

    private void OnShortcutsClicked(object sender, RoutedEventArgs e) => _ = ShowShortcutsAsync();

    private void OnAboutClicked(object sender, RoutedEventArgs e) => _ = ShowAboutAsync();

    private const string ReleasesUrl = "https://github.com/elliotttate/flatout4-save-editor/releases";

    // "Version 0.4.7 (abc1234)", or "Development build (abc1234)" for a local build.
    public static string VersionText
    {
        get
        {
            string informational = typeof(App).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "0.0.0-dev";
            string[] parts = informational.Split('+', 2);
            string commit = parts.Length > 1 ? $" ({parts[1][..Math.Min(7, parts[1].Length)]})" : string.Empty;
            return parts[0].EndsWith("-dev", StringComparison.Ordinal) ? $"Development build{commit}" : $"Version {parts[0]}{commit}";
        }
    }

    private async Task ShowAboutAsync()
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
        content.Children.Add(new TextBlock { Text = VersionText, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        content.Children.Add(new TextBlock
        {
            Text = $"Reads FlatOut 4 and FlatOut VR profiles from V{FlatOut4SaveSchema.EarliestSupportedVersion} to V{FlatOut4SaveSchema.CurrentVersion}, converts the RC-only V{FlatOut4SaveSchema.RcTelemetryVersion}, and recovers cut-off, damaged or newer saves. Saves are written as V{FlatOut4SaveSchema.CurrentVersion}, the layout the live Steam build reads, except newer layouts, which keep their own.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton { Content = "Releases on GitHub", NavigateUri = new Uri(ReleasesUrl), Padding = new Thickness(0) });

        var dialog = new ContentDialog
        {
            Title = "FlatOut 4 Save Editor",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnMoreOpening(object? sender, object e) => BackupMenuItem.IsChecked = BackupToggle.IsOn;

    private void OnBackupMenuClicked(object sender, RoutedEventArgs e) => BackupToggle.IsOn = BackupMenuItem.IsChecked;

    // The backup switch in the pane footer only fits while the pane is open; the More menu has it too.
    private void OnNavPaneOpening(NavigationView sender, object args) => BackupFooter.Visibility = Visibility.Visible;

    private void OnNavPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => BackupFooter.Visibility = Visibility.Collapsed;

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        BackupFooter.Visibility = sender.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadges();
    }

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateToolbarDensity();

    // Narrow windows drop the labels of the secondary buttons (icons and tooltips stay) rather
    // than letting the left group slide under the unsaved-changes pill.
    private void UpdateToolbarDensity()
    {
        if (Toolbar.ActualWidth <= 0)
        {
            return;
        }

        SetToolbarLabels(true);
        var unbounded = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        ToolbarStart.Measure(unbounded);
        ToolbarEnd.Measure(unbounded);
        double available = Toolbar.ActualWidth - Toolbar.Padding.Left - Toolbar.Padding.Right - Toolbar.ColumnSpacing;
        SetToolbarLabels(ToolbarStart.DesiredSize.Width + ToolbarEnd.DesiredSize.Width <= available);
    }

    private void SetToolbarLabels(bool visible)
    {
        Visibility visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FindSavesLabel.Visibility = visibility;
        UnlockAllLabel.Visibility = visibility;
        RevertLabel.Visibility = visibility;
    }

    private static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl+O", "Open a save file"),
        ("Ctrl+S", "Save"),
        ("Ctrl+F", "Search the save"),
        ("Ctrl+Tab / Ctrl+Shift+Tab", "Next / previous section"),
        ("Ctrl+Z", "Undo (in a text box: undo typing)"),
        ("Ctrl+Y or Ctrl+Shift+Z", "Redo"),
        ("Enter", "Commit a typed value"),
        ("Esc", "Close a dialog or drop-down")
    ];

    private async Task ShowShortcutsAsync()
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new Border
            {
                Padding = new Thickness(8, 2, 8, 3),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Child = new TextBlock { Text = Shortcuts[i].Keys, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] }
            };
            var action = new TextBlock { Text = Shortcuts[i].Action, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(keys, i);
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            grid.Children.Add(keys);
            grid.Children.Add(action);
        }

        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            Content = grid,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnSaveAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (Session?.NeedsWrite == true)
        {
            _ = SaveAsync();
        }
    }

    private void OnNextSectionAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateRelative(1);
    }

    private void OnPreviousSectionAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateRelative(-1);
    }

    // Steps through the sections in navigation order, wrapping around and skipping any that are
    // unavailable (everything but Overview until a save is open). Returns the section shown.
    public string? NavigateRelative(int direction)
    {
        string[] keys = EditorCatalog.Sections.Select(section => section.Key).ToArray();
        int current = Array.FindIndex(keys, key => navItems.TryGetValue(key, out NavigationViewItem? item) && ReferenceEquals(Nav.SelectedItem, item));
        for (int step = 1; step < keys.Length; step++)
        {
            string key = keys[(((current + (direction * step)) % keys.Length) + keys.Length) % keys.Length];
            if (navItems.TryGetValue(key, out NavigationViewItem? item) && item.IsEnabled)
            {
                Navigate(key);
                return key;
            }
        }

        return null;
    }

    // Text boxes keep their own Ctrl+Z while focused; everywhere else it steps the save history.
    private void OnUndoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Undo();
    }

    private void OnRedoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Redo();
    }

    private void OnOpenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = BrowseAsync();
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SearchBox.IsEnabled)
        {
            SearchBox.Focus(FocusState.Keyboard);
        }
    }

    // --snapshot <dir>: render every page to PNG (off-screen, never activated) and exit.
    private async Task RunSnapshotAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        App.SnapshotLog($"snapshot start, session={(Session is not null)}");
        SaveSession.WriteTrace = App.SnapshotLog;
        // Mica is not captured by RenderTargetBitmap; paint the theme's base colour instead.
        RootGrid.Background = new SolidColorBrush(ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20)
            : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        await Task.Delay(1200);
        if (App.SnapshotUnlock && Session is not null)
        {
            await UnlockAllAsync();
            await Task.Delay(600);
        }

        if (App.SnapshotSelfTest)
        {
            await RunSelfTestAsync();
            Application.Current.Exit();
            return;
        }

        expectedChanges = Session?.ModifiedCount ?? 0;
        await CaptureAsync(Path.Combine(directory, "00-overview.png"));

        if (Session is not null)
        {
            if (ContentFrame.Content is OverviewPage)
            {
                await CaptureScrolledAsync(directory, "00-overview");
            }

            int index = 1;
            foreach (EditorCatalog.SectionInfo info in EditorCatalog.Sections.Skip(1))
            {
                Navigate(info.Key);
                await Task.Delay(1200);
                string name = $"{index:D2}-{info.Key}";
                await CaptureAsync(Path.Combine(directory, $"{name}.png"));
                await CaptureScrolledAsync(directory, name);
                index++;
            }

            if (App.SnapshotSearch is { } query && Catalog?.Search(query).FirstOrDefault() is { } result)
            {
                Navigate(result.Page.Key, result);
                await Task.Delay(1800);
                await CaptureAsync(Path.Combine(directory, "90-search.png"));
            }
        }

        Application.Current.Exit();
    }

    private async Task CaptureScrolledAsync(string directory, string name)
    {
        ScrollViewer? scroller = ContentFrame.Content switch
        {
            SectionPage section => section.Scroller,
            _ => FindScroller(ContentFrame.Content as DependencyObject)
        };

        if (scroller is null)
        {
            return;
        }

        for (int step = 1; step <= App.SnapshotScrollSteps; step++)
        {
            double offset = scroller.ViewportHeight * 0.85 * step;
            if (offset > scroller.ScrollableHeight + (scroller.ViewportHeight * 0.85))
            {
                break;
            }

            scroller.ChangeView(null, Math.Min(offset, scroller.ScrollableHeight), null, true);
            await Task.Delay(700);
            await CaptureAsync(Path.Combine(directory, $"{name}-{step}.png"));
            if (offset >= scroller.ScrollableHeight)
            {
                break;
            }
        }
    }

    private static ScrollViewer? FindScroller(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollViewer scroller)
        {
            return scroller;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindScroller(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private async Task CaptureAsync(string path, UIElement? element = null)
    {
        App.SnapshotLog($"capture {Path.GetFileName(path)} root={RootGrid.ActualWidth}x{RootGrid.ActualHeight}");
        if (element is null && Session is not null && Session.ModifiedCount != expectedChanges)
        {
            // Snapshot mode makes no edits after the optional Unlock All, so any change here is a binding writing back.
            foreach (SaveFieldViewModel field in Session.Fields.Where(field => field.IsModified))
            {
                App.SnapshotLog($"  UNEXPECTED CHANGE {field.RawName} = {field.DisplayValue}");
            }
        }
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element ?? RootGrid);
        IBuffer pixels = await bitmap.GetPixelsAsync();

        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        StorageFile file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            96,
            96,
            pixels.ToArray());
        await encoder.FlushAsync();
    }
}
