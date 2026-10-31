using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace Yeetle.App.Controls;

// Lays out marker ticks along the seek bar: each child is centred at its Fraction (0 = start,
// 1 = end) of the width. In an ItemsControl, set Fraction on the item template's root.
public sealed class MarkerTickPanel : Panel
{
    public static readonly AttachedProperty<double> FractionProperty =
        AvaloniaProperty.RegisterAttached<MarkerTickPanel, Control, double>("Fraction");

    public static double GetFraction(Control control) => control.GetValue(FractionProperty);

    public static void SetFraction(Control control, double value) => control.SetValue(FractionProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var x = Math.Clamp(FractionOf(child), 0, 1) * finalSize.Width - size.Width / 2;
            child.Arrange(new Rect(x, 0, size.Width, size.Height));
        }
        return finalSize;
    }

    // An ItemsControl wraps each item in a ContentPresenter; the template's root carries Fraction.
    private static double FractionOf(Control child) =>
        child is ContentPresenter { Child: { } content } ? GetFraction(content) : GetFraction(child);
}
