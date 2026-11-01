using Windows.Foundation;

namespace FlatOut4SaveEditor.Views;

// Equal-width columns that wrap: as many columns of at least MinItemWidth as fit, each row
// as tall as its tallest item. Used instead of UniformGridLayout, which fail-fasts in
// GetMajorSize on this Windows App SDK build.
public sealed partial class AdaptiveGridPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGridPanel), new PropertyMetadata(280.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(AdaptiveGridPanel), new PropertyMetadata(8.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(AdaptiveGridPanel), new PropertyMetadata(8.0, OnLayoutPropertyChanged));

    private readonly List<double> rowHeights = [];
    private int columns = 1;
    private double itemWidth;

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        columns = Math.Max(1, (int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing)));
        itemWidth = Math.Max(0, (width - (ColumnSpacing * (columns - 1))) / columns);

        rowHeights.Clear();
        double rowHeight = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((i % columns) == columns - 1 || i == Children.Count - 1)
            {
                rowHeights.Add(rowHeight);
                rowHeight = 0;
            }
        }

        double height = rowHeights.Sum() + (RowSpacing * Math.Max(0, rowHeights.Count - 1));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            if (column == 0 && row > 0)
            {
                y += rowHeights[row - 1] + RowSpacing;
            }

            double x = column * (itemWidth + ColumnSpacing);
            Children[i].Arrange(new Rect(x, y, itemWidth, row < rowHeights.Count ? rowHeights[row] : 0));
        }

        return finalSize;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AdaptiveGridPanel)d).InvalidateMeasure();
}
