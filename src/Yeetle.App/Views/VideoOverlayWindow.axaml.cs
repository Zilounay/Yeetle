using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yeetle.App.Input;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

// The player's controls, drawn over the video. The video is a native window, which covers
// anything Avalonia draws in the same window (POC #4), so the controls live in this separate
// transparent window, owned by the main window and kept exactly over the video area.
public partial class VideoOverlayWindow : Window
{
    private static readonly Point TopLeft = new(0, 0);

    private Window? _owner;
    private Control? _area;

    public VideoOverlayWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => DataContext as IHotkeyTarget);
        // The slider's thumb handles pointer events itself, so handled events are listened to too.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => SetScrubbing(true), RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Tunnel, true);
    }

    // Shows the overlay over the area and keeps it there as the owner moves or resizes.
    public void Follow(Window owner, Control area)
    {
        _owner = owner;
        _area = area;
        owner.PositionChanged += (_, _) => FitToArea();
        area.LayoutUpdated += (_, _) => FitToArea();
        owner.Closed += (_, _) => Close();
        Show(owner);
        FitToArea();
    }

    // Hidden while the video area is (the Explorer page): a window over the list would take its clicks.
    public void SetShown(bool shown)
    {
        if (_owner is null || shown == IsVisible)
            return;
        if (shown)
        {
            Show(_owner);
            FitToArea();
        }
        else
        {
            Hide();
        }
    }

    private void FitToArea()
    {
        if (_owner is null || _area is null || !_area.IsVisible || !IsLaidOut(_area))
            return;
        var position = _area.PointToScreen(TopLeft);
        var size = _area.Bounds.Size;
        if (Position != position)
            Position = position;
        if (Width != size.Width || Height != size.Height)
        {
            Width = size.Width;
            Height = size.Height;
        }
    }

    // Before its first layout a control has no size yet.
    private static bool IsLaidOut(Control control) => control.Bounds.Width > 0 && control.Bounds.Height > 0;

    private void SetScrubbing(bool scrubbing)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.Player.IsScrubbing = scrubbing;
    }
}
