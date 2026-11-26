using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Controls;

// The session at a glance (#68): one cell per clip, coloured by decision, the playing clip marked.
// What is behind is history, the dark part is what is left. Clicking a reached clip runs Command
// with its index. Drawn as one control, so hundreds of clips stay light: cells get thinner.
public sealed class ClipTimeline : Control
{
    // Below this many pixels per cell, cells touch instead of leaving a gap between them.
    private const double MinWidthForGap = 6;
    private const double Gap = 2;
    private const double CellRadius = 2;
    // Cells other than the playing one are this much shorter at top and bottom, so it stands out.
    private const double Inset = 3;

    public static readonly StyledProperty<IReadOnlyList<TimelineCell>> CellsProperty =
        AvaloniaProperty.Register<ClipTimeline, IReadOnlyList<TimelineCell>>(nameof(Cells), []);

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ClipTimeline, ICommand?>(nameof(Command));

    public static readonly StyledProperty<IBrush?> KeptBrushProperty =
        AvaloniaProperty.Register<ClipTimeline, IBrush?>(nameof(KeptBrush));

    public static readonly StyledProperty<IBrush?> YeetedBrushProperty =
        AvaloniaProperty.Register<ClipTimeline, IBrush?>(nameof(YeetedBrush));

    public static readonly StyledProperty<IBrush?> SkippedBrushProperty =
        AvaloniaProperty.Register<ClipTimeline, IBrush?>(nameof(SkippedBrush));

    public static readonly StyledProperty<IBrush?> CurrentBrushProperty =
        AvaloniaProperty.Register<ClipTimeline, IBrush?>(nameof(CurrentBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ClipTimeline, IBrush?>(nameof(TrackBrush));

    private int _hovered = -1;

    static ClipTimeline() =>
        AffectsRender<ClipTimeline>(CellsProperty, KeptBrushProperty, YeetedBrushProperty, SkippedBrushProperty,
            CurrentBrushProperty, TrackBrushProperty);

    public IReadOnlyList<TimelineCell> Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
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

    public IBrush? CurrentBrush
    {
        get => GetValue(CurrentBrushProperty);
        set => SetValue(CurrentBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var cells = Cells;
        if (cells.Count == 0)
            return;
        var (width, gap) = CellWidth(cells.Count);
        var radius = gap > 0 ? CellRadius : 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var isCurrent = cells[i].State == CardBadge.Playing;
            if (BrushFor(cells[i].State) is not { } brush)
                continue;
            var inset = isCurrent ? 0 : Math.Min(Inset, Bounds.Height / 4);
            var rect = new Rect(i * (width + gap), inset, width, Bounds.Height - 2 * inset);
            context.DrawRectangle(brush, null, new RoundedRect(rect, radius));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = IndexAt(e.GetPosition(this).X);
        if (index == _hovered)
            return;
        _hovered = index;
        var cell = index >= 0 ? Cells[index] : null;
        ToolTip.SetTip(this, cell?.Tip);
        Cursor = cell is { CanJump: true } ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = -1;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var index = IndexAt(e.GetPosition(this).X);
        if (index < 0 || !Cells[index].CanJump || Command is not { } command || !command.CanExecute(index))
            return;
        command.Execute(index);
        e.Handled = true;
    }

    private IBrush? BrushFor(CardBadge state) => state switch
    {
        CardBadge.Playing => CurrentBrush,
        CardBadge.Kept => KeptBrush,
        CardBadge.Yeeted => YeetedBrush,
        CardBadge.Skipped => SkippedBrush,
        _ => TrackBrush,
    };

    private (double Width, double Gap) CellWidth(int count)
    {
        var plain = Bounds.Width / count;
        if (plain < MinWidthForGap || count == 1)
            return (plain, 0);
        return ((Bounds.Width - Gap * (count - 1)) / count, Gap);
    }

    private int IndexAt(double x)
    {
        var count = Cells.Count;
        if (count == 0 || x < 0 || x >= Bounds.Width)
            return -1;
        return Math.Clamp((int)(x / (Bounds.Width / count)), 0, count - 1);
    }
}
