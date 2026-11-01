using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FlatOut4SaveEditor.Views;

// --snapshot <dir> --selftest: drives the real editors in the off-screen window the way a user
// would (focus, then change) and checks what reached the save bytes. Results go to snapshot.log.
public sealed partial class MainPage
{
    private int selfTestFailures;

    private async Task RunSelfTestAsync()
    {
        if (Session is null || Catalog is null)
        {
            App.SnapshotLog("SELFTEST FAIL no save open");
            return;
        }

        SaveSession session = Session;
        App.SnapshotLog("SELFTEST start");

        // Toggle (two-way bound switch on a settings card).
        SaveFieldViewModel invertX = session.Field("m_oGameOptions.m_oGameplayOptions.m_uCamXInversed");
        bool invertBefore = invertX.BoolValue;
        if (await FindEditorAsync<ToggleSwitch>("options", invertX) is { } toggle)
        {
            toggle.IsOn = !toggle.IsOn;
            Expect(invertX.BoolValue != invertBefore, "toggle switch writes Invert camera X");
        }

        // Picker on a settings card: a programmatic change while unfocused must not write...
        SaveFieldViewModel difficulty = session.Field("m_oGameOptions.m_oGameplayOptions.m_uDifficulty");
        if (await FindEditorAsync<ComboBox>("options", difficulty) is { } difficultyBox)
        {
            long before = difficulty.ReadInteger();
            difficultyBox.SelectedIndex = before == 2 ? 0 : 2;
            Expect(difficulty.ReadInteger() == before, "unfocused picker change is ignored (binding guard)");
            difficultyBox.SelectedIndex = (int)before;

            // ...while the same change with the picker focused does.
            bool focused = difficultyBox.Focus(FocusState.Programmatic);
            difficultyBox.SelectedIndex = before == 2 ? 0 : 2;
            Expect(focused && difficulty.ReadInteger() == (before == 2 ? 0 : 2), "focused picker writes AI difficulty");
        }

        // Slider + number box on a range card.
        SaveFieldViewModel ffb = session.Field("m_oGameOptions.m_oGameplayOptions.m_uFFBStrength");
        if (await FindEditorAsync<Slider>("options", ffb) is { } slider)
        {
            slider.Focus(FocusState.Programmatic);
            slider.Value = 150;
            Expect(ffb.ReadInteger() == 150, $"focused slider writes FFB strength (got {ffb.ReadInteger()})");
        }

        if (await FindEditorAsync<NumberBox>("options", ffb) is { } ffbBox)
        {
            ffbBox.Value = 175;
            Expect(ffb.ReadInteger() == 175, $"number box writes FFB strength (got {ffb.ReadInteger()})");
            ffbBox.Value = 999;
            Expect(ffb.ReadInteger() == 200 && ffbBox.Value == 200, $"number box clamps to 200 and shows it (field {ffb.ReadInteger()}, box {ffbBox.Value})");
        }

        // Review changes lists the edit as old -> new, and the card's revert button undoes just it.
        long ffbOnDisk = BitConverter.ToUInt32(session.BaselineBytes, ffb.Definition.Offset);
        ChangeEntry? ffbEntry = Catalog.ChangeGroups().SelectMany(group => group.Entries).FirstOrDefault(entry => entry.Field == ffb);
        Expect(ffbEntry is not null && ffbEntry.Field.OldValueText == $"{ffbOnDisk} %" && ffbEntry.Field.NewValueText == "200 %" && ffbEntry.Location?.Page.Key == "options",
            $"Review changes lists FFB strength as {ffbEntry?.Field.OldValueText} -> {ffbEntry?.Field.NewValueText} on {ffbEntry?.Location?.Page.Key}");
        Navigate(EditorCatalog.ChangesKey);
        await Task.Delay(700);
        Expect(ContentFrame.Content is ChangesPage, "the Review changes page opens");
        Navigate("options", ffbEntry?.Location);
        await Task.Delay(900);
        if (await FindElementAsync<Button>("options", button => ReferenceEquals(button.Tag, ffb) && AutomationProperties.GetName(button) == "Revert", ffbEntry?.Location) is { } revertButton)
        {
            new ButtonAutomationPeer(revertButton).Invoke();
            Expect(ffb.ReadInteger() == ffbOnDisk && !ffb.IsModified, $"the card's revert button restores FFB strength ({ffb.ReadInteger()})");
        }

        // Text editor with validation.
        SaveFieldViewModel workshop = session.Field("m_aOther[0]");
        if (await FindEditorAsync<TextBox>("extensions", workshop) is { } workshopBox)
        {
            long before = workshop.ReadInteger();
            workshopBox.Text = "0xZZ";
            Expect(workshop.HasError && workshop.ReadInteger() == before, "invalid text shows an error and writes nothing");
            workshopBox.Text = "0x42";
            Expect(!workshop.HasError && workshop.ReadInteger() == 0x42, "valid hex text writes the Workshop ID");
        }

        // Table cell picker (garage car state).
        TableBlock cars = Catalog.Page("garage")!.Blocks.OfType<TableBlock>().First(table => table.Title == "Cars");
        SaveFieldViewModel carState = cars.AllRows.First(row => !row.IsEmpty).Cells[0].Field!;
        if (await FindEditorAsync<ComboBox>("garage", carState) is { } stateBox)
        {
            stateBox.Focus(FocusState.Programmatic);
            stateBox.SelectedIndex = carState.Options.ToList().FindIndex(option => option.Label == "Owned");
            Expect(carState.ReadInteger() == 3, $"table picker sets the car to Owned (got {carState.ReadInteger()})");
        }

        // Career medal picker (derived from the reward slots).
        CareerEventViewModel cup = Catalog.Page("career")!.Blocks.OfType<CareerEventsBlock>().First().Events[0];
        if (await FindElementAsync<ComboBox>("career", element => element.DataContext == cup, Catalog.Search(cup.Event.FullName).FirstOrDefault()) is { } medalBox)
        {
            medalBox.Focus(FocusState.Programmatic);
            medalBox.SelectedIndex = 2;
            Expect(Catalog.Career.GetMedalIndex(cup.Event.Id) == 2, "medal picker gives the first Derby cup silver");
        }

        // Trophy switch in the grid.
        SaveFieldViewModel trophy = session.Field("m_oTrophies.Unlocked[FO_TROPHY_GOLD_CRUSHER]");
        bool trophyBefore = trophy.BoolValue;
        if (await FindEditorAsync<ToggleSwitch>("trophies", trophy) is { } trophySwitch)
        {
            trophySwitch.IsOn = !trophySwitch.IsOn;
            Expect(trophy.BoolValue != trophyBefore, "trophy switch flips the Gold Crusher bit");
        }

        // Undo / Redo on the toolbar step the history (the trophy switch was the last edit).
        bool trophyNow = trophy.BoolValue;
        Expect(UndoButton.IsEnabled && !RedoButton.IsEnabled, "Undo is enabled and Redo is not after edits");
        new ButtonAutomationPeer(UndoButton).Invoke();
        Expect(trophy.BoolValue != trophyNow && RedoButton.IsEnabled, "the Undo button puts the trophy back and enables Redo");
        if (await FindEditorAsync<ToggleSwitch>("trophies", trophy) is { } undoneSwitch)
        {
            Expect(undoneSwitch.IsOn == trophy.BoolValue, "the trophy switch on screen shows the undone value");
        }

        new ButtonAutomationPeer(RedoButton).Invoke();
        Expect(trophy.BoolValue == trophyNow, "the Redo button applies it again");

        // Save health lists a damaged value; its Repair button fixes it and Undo brings it back.
        SaveFieldViewModel speedUnit = session.Field("m_oGameOptions.m_oGameplayOptions.m_uSpeedUnit");
        long speedBefore = speedUnit.ReadInteger();
        session.WriteUInt32(speedUnit.Definition.Offset, 7);
        Navigate(EditorCatalog.HealthKey);
        await Task.Delay(700);
        Button? repairButton = ContentFrame.Content is HealthPage healthPage
            ? FindAll<Button>(healthPage).FirstOrDefault(button => button.Tag is HealthIssueViewModel issue && issue.Issue.Field == speedUnit.Definition)
            : null;
        Expect(repairButton is not null, "Save health lists the damaged speed unit with a Repair button");
        if (repairButton is not null)
        {
            new ButtonAutomationPeer(repairButton).Invoke();
            Expect(speedUnit.ReadInteger() == 0 && Catalog.HealthIssues().Count == 0, $"its Repair button sets MPH ({speedUnit.ReadInteger()})");
            new ButtonAutomationPeer(UndoButton).Invoke();
            Expect(speedUnit.ReadInteger() == 7, "Undo brings the damaged value back");
        }

        session.WriteUInt32(speedUnit.Definition.Offset, (uint)speedBefore);

        // The More menu's Keyboard shortcuts dialog.
        Task shortcuts = ShowShortcutsAsync();
        await Task.Delay(600);
        ContentDialog? shortcutsDialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
            .Select(popup => popup.Child)
            .OfType<ContentDialog>()
            .FirstOrDefault();
        Expect(shortcutsDialog?.Title as string == "Keyboard shortcuts", "the Keyboard shortcuts dialog opens");
        if (shortcutsDialog is not null)
        {
            if (App.SnapshotDirectory is { } directory)
            {
                await CaptureAsync(Path.Combine(directory, "zz-shortcuts.png"), shortcutsDialog);
            }

            shortcutsDialog.Hide();
        }

        await shortcuts;

        // Revert puts every editor back, and scrolling long tables afterwards writes nothing.
        session.Revert();
        Expect(session.ModifiedCount == 0, "Revert clears every change");
        foreach (string key in new[] { "garage", "controls", "stats", "options", "career" })
        {
            Navigate(key);
            await Task.Delay(700);
            if (ContentFrame.Content is SectionPage page)
            {
                for (int i = 1; i <= 6; i++)
                {
                    page.Scroller.ChangeView(null, page.Scroller.ScrollableHeight * i / 6, null, true);
                    await Task.Delay(120);
                }

                page.Scroller.ChangeView(null, 0, null, true);
                await Task.Delay(200);
            }
        }

        Expect(session.ModifiedCount == 0, $"scrolling every long page after Revert writes nothing ({session.ModifiedCount} changes)");
        App.SnapshotLog(selfTestFailures == 0 ? "SELFTEST ALL PASSED" : $"SELFTEST {selfTestFailures} FAILED");
    }

    private void Expect(bool condition, string what)
    {
        App.SnapshotLog($"SELFTEST {(condition ? "PASS" : "FAIL")} {what}");
        if (!condition)
        {
            selfTestFailures++;
        }
    }

    private Task<T?> FindEditorAsync<T>(string pageKey, SaveFieldViewModel field)
        where T : FrameworkElement
    {
        SearchResult? target = Catalog!.Search(field.Label, max: 200).FirstOrDefault(result =>
            result.Page.Key == pageKey &&
            (ReferenceEquals(result.Target, field) ||
             (result.Target is TableRowViewModel row && row.Cells.Any(cell => ReferenceEquals(cell.Field, field)))));
        target ??= Catalog.Page(pageKey)!.Blocks
            .OfType<TableBlock>()
            .SelectMany(table => table.AllRows.Select(row => (table, row)))
            .Where(pair => pair.row.Cells.Any(cell => ReferenceEquals(cell.Field, field)))
            .Select(pair => new SearchResult { Title = pair.row.Title, Context = pageKey, Glyph = string.Empty, Page = Catalog.Page(pageKey)!, Block = pair.table, Target = pair.row })
            .FirstOrDefault();
        return FindElementAsync<T>(pageKey, element => ReferenceEquals(element.DataContext, field), target);
    }

    private async Task<T?> FindElementAsync<T>(string pageKey, Func<T, bool> match, SearchResult? reveal)
        where T : FrameworkElement
    {
        if (ContentFrame.Content is not SectionPage { ViewModel: { } current } || current.Key != pageKey)
        {
            Navigate(pageKey);
            await Task.Delay(700);
        }

        if (reveal is not null && ContentFrame.Content is SectionPage page)
        {
            await page.RevealAsync(reveal);
            await Task.Delay(500);
        }

        T? found = FindDescendant(ContentFrame, match);
        if (found is null)
        {
            Expect(false, $"found a {typeof(T).Name} on {pageKey}");
        }

        return found;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Visibility == Visibility.Visible && match(element))
            {
                return element;
            }

            if (FindDescendant(child, match) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
