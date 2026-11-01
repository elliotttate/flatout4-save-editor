using System.Runtime.CompilerServices;
using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FlatOut4SaveEditor.Views;

// Small helpers for x:Bind function bindings.
public static class Ui
{
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    private static readonly ConditionalWeakTable<ComboBox, object> InteractingCombos = new();

    // True while the user has the dropdown open (and just after it closes, when the pick is
    // committed) or the combo has keyboard focus; false for selection changes made by bindings.
    public static bool IsUserChange(ComboBox combo) =>
        combo.IsDropDownOpen || combo.FocusState != FocusState.Unfocused || InteractingCombos.TryGetValue(combo, out _);

    public static void BeginInteraction(ComboBox combo) => InteractingCombos.AddOrUpdate(combo, true);

    public static void EndInteraction(ComboBox combo) =>
        combo.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => InteractingCombos.Remove(combo));

    // Stops every x:Bind template under root from listening to its view model. The view models
    // outlive the pages, and a template binding only lets go of them once the element is garbage
    // collected, so a page that was left would keep receiving every refresh (and failing inside
    // ToolTipService/DataContext on half-torn-down cells). Pages call this when navigated away from.
    public static void ReleaseBindings(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            DependencyObject element = pending.Pop();
            XamlBindingHelper.GetDataTemplateComponent(element)?.Recycle();
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(element, i));
            }
        }
    }

    // Tooltips that change with the save: register one ToolTip per element and only change its
    // text afterwards. Calling ToolTipService.SetToolTip again re-registers it each time, and that
    // path intermittently fails with E_FAIL when many elements refresh at once.
    public static void SetToolTipText(DependencyObject element, string text)
    {
        if (ToolTipService.GetToolTip(element) is ToolTip tip)
        {
            tip.Content = text;
        }
        else
        {
            ToolTipService.SetToolTip(element, new ToolTip { Content = text });
        }
    }

    public static string OnOff(bool value) => value ? "On" : "Off";

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfText(string? text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    public static double Opacity(bool enabled) => enabled ? 1.0 : 0.45;

    public static Brush HighlightBrush(bool highlighted) =>
        highlighted ? Resource("AccentFillColorDefaultBrush", 0.18) : Transparent;

    public static Brush AccentWhen(bool value) =>
        value ? Resource("AccentFillColorDefaultBrush", 1) : Resource("TextFillColorTertiaryBrush", 1);

    public static Brush MedalBrush(int medal) => medal switch
    {
        1 => new SolidColorBrush(Color.FromArgb(255, 0xB0, 0x6A, 0x3B)),
        2 => new SolidColorBrush(Color.FromArgb(255, 0x8E, 0x99, 0xA4)),
        3 => new SolidColorBrush(Color.FromArgb(255, 0xD9, 0xA4, 0x0E)),
        _ => Resource("ControlFillColorTertiaryBrush", 1)
    };

    public static Brush MedalForeground(int medal) => medal > 0
        ? new SolidColorBrush(Colors.White)
        : Resource("TextFillColorTertiaryBrush", 1);

    public static InfoBarSeverity Severity(string severity) => severity switch
    {
        "Success" => InfoBarSeverity.Success,
        "Warning" => InfoBarSeverity.Warning,
        "Error" => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational
    };

    public static object? Tip(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    public static string Count(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count:N0} {plural}";

    private static Brush Resource(string key, double opacity)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? value) && value is SolidColorBrush brush)
        {
            return opacity >= 1 ? brush : new SolidColorBrush(brush.Color) { Opacity = opacity };
        }

        return Transparent;
    }
}

public sealed partial class FieldEditorSelector : DataTemplateSelector
{
    public DataTemplate? Toggle { get; set; }

    public DataTemplate? Choice { get; set; }

    public DataTemplate? Number { get; set; }

    public DataTemplate? Range { get; set; }

    public DataTemplate? Text { get; set; }

    public DataTemplate? ReadOnly { get; set; }

    public DataTemplate? Empty { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        SaveFieldViewModel field => field.Editor switch
        {
            FieldEditorKind.Toggle => Toggle,
            FieldEditorKind.Choice => Choice,
            FieldEditorKind.Number => Number,
            FieldEditorKind.Range => Range ?? Number,
            FieldEditorKind.Text => Text,
            _ => ReadOnly
        },
        _ => Empty
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

public sealed partial class BlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FieldGroup { get; set; }

    public DataTemplate? Table { get; set; }

    public DataTemplate? CareerEvents { get; set; }

    public DataTemplate? TrophyGrid { get; set; }

    public DataTemplate? Note { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        FieldGroupBlock => FieldGroup,
        TableBlock => Table,
        CareerEventsBlock => CareerEvents,
        TrophyGridBlock => TrophyGrid,
        NoteBlock => Note,
        _ => null
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
