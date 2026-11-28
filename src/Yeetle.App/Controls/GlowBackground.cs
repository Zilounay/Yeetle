using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Yeetle.App.Theming;

namespace Yeetle.App.Controls;

// The page behind every Yeetle screen (#69): the theme's dark colour with three soft lights,
// which drift slowly unless Animated background is off or Reduce motion is on. Put it first in a
// window's root panel; it takes no input.
//
// The drift moves the lights a little at a time from a timer, about twenty times a second: smooth
// enough for something this slow, and nearly free on the UI thread, so a clip switch never waits
// for it.
public sealed class GlowBackground : Control
{
    // One drift, there and back, as on the canvas (22 s each way).
    private static readonly TimeSpan DriftTime = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(50);
    // How far the lights travel, as a share of the page, and how much they grow.
    private const double DriftX = -0.06;
    private const double DriftY = 0.04;
    private const double DriftGrowth = 0.12;
    // Where a light fades to nothing, as a share of its radius.
    private const double FadeAt = 0.7;

    // The three lights: centre and radii as shares of the page (the canvas's radial gradients).
    private static readonly (double X, double Y, double RadiusX, double RadiusY)[] Lights =
    [
        (0.18, 0.22, 0.42, 0.46),
        (0.84, 0.18, 0.38, 0.42),
        (0.62, 0.92, 0.46, 0.50),
    ];

    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;
    private IBrush?[] _brushes = [];
    private IBrush? _page;

    public GlowBackground()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(FrameTime, DispatcherPriority.Background, (_, _) => InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Appearance.Instance.Changed += OnAppearanceChanged;
        OnAppearanceChanged(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Appearance.Instance.Changed -= OnAppearanceChanged;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        var palette = Appearance.Instance.Palette;
        _page = new SolidColorBrush(palette.Page);
        _brushes = [Light(palette.Glow1), Light(palette.Glow2), Light(palette.Glow3)];
        if (Appearance.Instance.AnimateBackground)
            _timer.Start();
        else
            _timer.Stop();
        InvalidateVisual();
    }

    private static IBrush Light(Color colour) => new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(colour, 0),
            new GradientStop(Colors.Transparent, FadeAt),
        },
    };

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (_page is { } page)
            context.FillRectangle(page, new Rect(size));
        // 0 to 1 and back, eased, over two drift times.
        var t = _timer.IsEnabled
            ? (1 - Math.Cos((DateTime.UtcNow - _start).TotalSeconds / DriftTime.TotalSeconds * Math.PI)) / 2
            : 0;
        var grow = 1 + DriftGrowth * t;
        for (var i = 0; i < Lights.Length && i < _brushes.Length; i++)
        {
            var (x, y, rx, ry) = Lights[i];
            var centre = new Point((x + DriftX * t) * size.Width, (y + DriftY * t) * size.Height);
            context.DrawEllipse(_brushes[i], null, centre, rx * size.Width * grow, ry * size.Height * grow);
        }
    }
}
