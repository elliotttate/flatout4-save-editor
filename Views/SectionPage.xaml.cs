using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace FlatOut4SaveEditor.Views;

public sealed partial class SectionPage : Page
{
    private const int MinimumBlocksForJumpStrip = 3;
    private SearchResult? pendingTarget;
    private IReadOnlyList<JumpTarget> jumpTargets = [];

    public SectionPage()
    {
        InitializeComponent();
    }

    public PageViewModel? ViewModel { get; private set; }

    public ScrollViewer Scroller => PageScroller;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is PageNavigation navigation)
        {
            ViewModel = navigation.Page;
            pendingTarget = navigation.Target;
            Bindings.Update();
            BuildJumpStrip();
            if (pendingTarget is not null)
            {
                // Let the repeaters realize their first items before scrolling.
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RevealAsync(pendingTarget));
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Ui.ReleaseBindings(this);
    }

    private void BuildJumpStrip()
    {
        jumpTargets = ViewModel?.Blocks
            .Where(block => !string.IsNullOrWhiteSpace(block.Title) && block is not NoteBlock)
            .Select(block => new JumpTarget(block))
            .ToArray() ?? [];

        bool show = jumpTargets.Count >= MinimumBlocksForJumpStrip;
        JumpScroller.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HeaderDivider.Visibility = JumpScroller.Visibility;
        JumpRepeater.ItemsSource = show ? jumpTargets : null;
        if (show)
        {
            jumpTargets[0].IsActive = true;
        }
    }

    private void OnJumpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: JumpTarget target } && ViewModel is not null)
        {
            int index = IndexOf(ViewModel.Blocks, target.Block);
            if (index < 0)
            {
                return;
            }

            UIElement element = BlocksRepeater.GetOrCreateElement(index);
            element.UpdateLayout();
            element.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0,
                VerticalOffset = -12
            });
            SetActiveJump(target);
        }
    }

    // Scroll-spy: the active pill is the last block whose top has scrolled past the header.
    private void OnPageScrolled(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (jumpTargets.Count == 0 || ViewModel is null)
        {
            return;
        }

        if (PageScroller.VerticalOffset >= PageScroller.ScrollableHeight - 2)
        {
            SetActiveJump(jumpTargets[^1]);
            return;
        }

        JumpTarget active = jumpTargets[0];
        foreach (JumpTarget target in jumpTargets)
        {
            int index = IndexOf(ViewModel.Blocks, target.Block);
            if (index < 0 || BlocksRepeater.TryGetElement(index) is not UIElement element)
            {
                continue;
            }

            double top = element.TransformToVisual(PageScroller).TransformPoint(default).Y;
            if (top <= PageScroller.ViewportHeight * 0.3)
            {
                active = target;
            }
        }

        SetActiveJump(active);
    }

    private void SetActiveJump(JumpTarget active)
    {
        foreach (JumpTarget target in jumpTargets)
        {
            target.IsActive = ReferenceEquals(target, active);
        }

        if (JumpRepeater.TryGetElement(IndexOf(jumpTargets, active)) is FrameworkElement pill)
        {
            pill.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, HorizontalAlignmentRatio = 0.5 });
        }
    }

    private void OnMedalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: CareerEventViewModel careerEvent } combo && combo.SelectedIndex >= 0 && Ui.IsUserChange(combo))
        {
            careerEvent.MedalIndex = combo.SelectedIndex;
        }
    }

    private void OnComboDropDownOpened(object sender, object e) => Ui.BeginInteraction((ComboBox)sender);

    private void OnRevertFieldClicked(object sender, RoutedEventArgs e)
    {
        // Tag, not DataContext: repeater-created cards carry their item only through x:Bind.
        if (sender is FrameworkElement { Tag: SaveFieldViewModel field })
        {
            field.Revert();
        }
    }

    private void OnRevertRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TableRowViewModel row })
        {
            row.Revert();
        }
    }

    private void OnComboDropDownClosed(object sender, object e) => Ui.EndInteraction((ComboBox)sender);

    public async Task RevealAsync(SearchResult target)
    {
        if (ViewModel is null || target.Block is null)
        {
            return;
        }

        int blockIndex = IndexOf(ViewModel.Blocks, target.Block);
        if (blockIndex < 0)
        {
            return;
        }

        UIElement blockElement = BlocksRepeater.GetOrCreateElement(blockIndex);
        blockElement.UpdateLayout();
        UIElement? targetElement = blockElement;

        switch (target.Target)
        {
            case TableRowViewModel row when target.Block is TableBlock table:
                table.Reveal(row);
                row.IsHighlighted = true;
                blockElement.UpdateLayout();
                if (FindNamed<ItemsRepeater>(blockElement, "Rows") is { } rows)
                {
                    int rowIndex = table.VisibleRows.IndexOf(row);
                    if (rowIndex >= 0)
                    {
                        targetElement = rows.GetOrCreateElement(rowIndex);
                    }
                }

                _ = ClearHighlightAsync(() => row.IsHighlighted = false);
                break;
            case SaveFieldViewModel field:
                targetElement = FindItem(blockElement, target.Block, field) ?? blockElement;
                field.IsHighlighted = true;
                _ = ClearHighlightAsync(() => field.IsHighlighted = false);
                break;
            case CareerEventViewModel careerEvent:
                targetElement = FindItem(blockElement, target.Block, careerEvent) ?? blockElement;
                break;
        }

        await Task.Yield();
        targetElement.UpdateLayout();
        targetElement.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.3
        });
    }

    private static UIElement? FindItem(UIElement blockElement, PageBlock block, object item)
    {
        if (block is CareerEventsBlock career && item is CareerEventViewModel careerEvent)
        {
            for (int tier = 0; tier < career.Tiers.Count; tier++)
            {
                int eventIndex = IndexOf(career.Tiers[tier].Events, careerEvent);
                if (eventIndex >= 0 &&
                    FindNamed<ItemsControl>(blockElement, "TierList")?.ContainerFromIndex(tier) is DependencyObject tierElement &&
                    FindNamed<ItemsControl>(tierElement, "Items") is { } events)
                {
                    return events.ContainerFromIndex(eventIndex) as UIElement;
                }
            }

            return null;
        }

        IReadOnlyList<object>? items = block switch
        {
            FieldGroupBlock group => group.Fields,
            TrophyGridBlock trophies => trophies.Trophies,
            CareerEventsBlock events => events.Events,
            _ => null
        };

        int index = items is null ? -1 : IndexOf(items, item);
        if (index < 0)
        {
            return null;
        }

        if (FindNamed<ItemsRepeater>(blockElement, "Items") is { } repeater)
        {
            return repeater.GetOrCreateElement(index);
        }

        return FindNamed<ItemsControl>(blockElement, "Items")?.ContainerFromIndex(index) as UIElement;
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, object item)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static async Task ClearHighlightAsync(Action clear)
    {
        await Task.Delay(2600);
        clear();
    }

    internal static T? FindNamed<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindNamed<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
