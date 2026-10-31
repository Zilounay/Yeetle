using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Yeetle.App.Controls;

// The session's progress (#62): one kept, one yeeted and one skipped segment, in that order, each
// as long as its share of all the clips. The rest of the track is what is left to review.
public sealed class DecisionBar : Control
{
    public static readonly StyledProperty<int> KeptProperty = AvaloniaProperty.Register<DecisionBar, int>(nameof(Kept));

    public static readonly StyledProperty<int> YeetedProperty = AvaloniaProperty.Register<DecisionBar, int>(nameof(Yeeted));

    public static readonly StyledProperty<int> SkippedProperty = AvaloniaProperty.Register<DecisionBar, int>(nameof(Skipped));

    public static readonly StyledProperty<int> TotalProperty = AvaloniaProperty.Register<DecisionBar, int>(nameof(Total));

    public static readonly StyledProperty<IBrush?> KeptBrushProperty =
        AvaloniaProperty.Register<DecisionBar, IBrush?>(nameof(KeptBrush));

    public static readonly StyledProperty<IBrush?> YeetedBrushProperty =
        AvaloniaProperty.Register<DecisionBar, IBrush?>(nameof(YeetedBrush));

    public static readonly StyledProperty<IBrush?> SkippedBrushProperty =
        AvaloniaProperty.Register<DecisionBar, IBrush?>(nameof(SkippedBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<DecisionBar, IBrush?>(nameof(TrackBrush));

    static DecisionBar() =>
        AffectsRender<DecisionBar>(KeptProperty, YeetedProperty, SkippedProperty, TotalProperty, KeptBrushProperty,
            YeetedBrushProperty, SkippedBrushProperty, TrackBrushProperty);

    public int Kept
    {
        get => GetValue(KeptProperty);
        set => SetValue(KeptProperty, value);
    }

    public int Yeeted
    {
        get => GetValue(YeetedProperty);
        set => SetValue(YeetedProperty, value);
    }

    public int Skipped
    {
        get => GetValue(SkippedProperty);
        set => SetValue(SkippedProperty, value);
    }

    public int Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public IBrush? KeptBrush
    {
        get => GetValue(KeptBrushProperty);
        set => SetValue(KeptBrushProperty, value);
    }

    public IBrush? YeetedBrush
    {
        get => GetValue(YeetedBrushProperty);
        set => SetValue(YeetedBrushProperty, value);
    }

    public IBrush? SkippedBrush
    {
        get => GetValue(SkippedBrushProperty);
        set => SetValue(SkippedBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        // Fully rounded ends, like a progress bar.
        var radius = bounds.Height / 2;
        using (context.PushClip(new RoundedRect(bounds, radius)))
        {
            if (TrackBrush is { } track)
                context.FillRectangle(track, bounds);
            if (Total <= 0)
                return;
            var left = 0.0;
            foreach (var (count, brush) in new[] { (Kept, KeptBrush), (Yeeted, YeetedBrush), (Skipped, SkippedBrush) })
            {
                var width = bounds.Width * Math.Clamp(count, 0, Total) / Total;
                if (brush is not null && width > 0)
                    context.FillRectangle(brush, new Rect(left, 0, Math.Min(width, bounds.Width - left), bounds.Height));
                left += width;
            }
        }
    }
}
