using Windows.Foundation;

namespace FlatOut4SaveEditor.Views;

// Lays children out left to right at their natural size and wraps to a new line when the
// next one does not fit. Used for the info chips on the overview.
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(6.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double widest = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
            }

            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width), y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        foreach (UIElement child in Children)
        {
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return finalSize;
    }
}
