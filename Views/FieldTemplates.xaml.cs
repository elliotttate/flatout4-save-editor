using FlatOut4SaveEditor.Models;
using Windows.Globalization.NumberFormatting;

namespace FlatOut4SaveEditor.Views;

public sealed partial class FieldTemplates : ResourceDictionary
{
    public FieldTemplates()
    {
        InitializeComponent();
    }

    // Bound one-way: a recycled slider gets new Minimum/Maximum before its new Value, and the
    // coerced old value must not be written into the newly bound field. Only user drags (the
    // slider has focus) write.
    private void OnSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { DataContext: SaveFieldViewModel field } slider && slider.FocusState != FocusState.Unfocused)
        {
            field.NumberValue = e.NewValue;
        }
    }

    // Pickers are bound one-way for the same reason: only a selection the user made writes.
    private void OnChoiceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: SaveFieldViewModel field } combo && combo.SelectedIndex >= 0 && Ui.IsUserChange(combo))
        {
            field.SelectedIndex = combo.SelectedIndex;
        }
    }

    private void OnComboDropDownOpened(object sender, object e) => Ui.BeginInteraction((ComboBox)sender);

    private void OnComboDropDownClosed(object sender, object e) => Ui.EndInteraction((ComboBox)sender);

    // Floats show up to six decimals; integers none. Set per data context because recycled
    // boxes move between integer and float fields.
    private void OnNumberBoxDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not NumberBox box || args.NewValue is not SaveFieldViewModel field)
        {
            return;
        }

        var formatter = new DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 0,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = field.IsFloat ? 0.000001 : 1,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp
            }
        };
        box.NumberFormatter = formatter;
    }
}
