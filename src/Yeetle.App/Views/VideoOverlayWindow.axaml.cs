using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yeetle.App.Input;
using Yeetle.App.Services;
using Yeetle.App.Theming;
using Yeetle.App.ViewModels;
using Yeetle.Domain.Clips;

namespace Yeetle.App.Views;

// The player's controls, drawn over the video. The video is a native window, which covers
// anything Avalonia draws in the same window (POC #4), so the controls live in this separate
// transparent window, owned by the main window and kept exactly over the video area.
public partial class VideoOverlayWindow : Window
{
    private static readonly Point TopLeft = new(0, 0);

    private Window? _owner;
    private Control? _area;
    private bool _wanted = true;
    // Dialogs open right now; they can be nested (a result window after a prompt).
    private int _dialogs;

    public VideoOverlayWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => DataContext as IHotkeyTarget);
        // The slider's thumb handles pointer events itself, so handled events are listened to too.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => SetScrubbing(true), RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Tunnel, true);
        // The controls rest out of sight while the mouse rests (#93).
        _idleTimer = new DispatcherTimer { Interval = IdleDelay };
        _idleTimer.Tick += (_, _) => GoIdle();
        PointerMoved += (_, _) => WakeUp();
        PointerEntered += (_, _) => WakeUp();
        // Theatre mode (#95): the right edge slides the Tags panel in; leaving it slides it away.
        TagEdge.PointerEntered += (_, _) => TagDrawer.Classes.Set(OpenClass, true);
        TagDrawer.PointerExited += (_, _) => TagDrawer.Classes.Set(OpenClass, false);
        // A list opened from the sound block is a window of its own: the block stays open meanwhile.
        foreach (var list in new[] { AudioTracks, AudioDevices })
        {
            list.DropDownOpened += (_, _) => SetPinned(true);
            list.DropDownClosed += (_, _) => SetPinned(false);
        }
        DataContextChanged += (_, _) => FollowDecisions();
    }

    // How long the mouse may rest over the video before the controls fade out (#93).
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2.5);

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);

    private readonly DispatcherTimer _idleTimer;
    private bool _listOpen;

    private void WakeUp()
    {
        Root.Classes.Set(IdleClass, false);
        Cursor = Cursor.Default;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    // Not while the pointer is on a control, a list is open or the seek bar is being dragged: the
    // controls would vanish from under it.
    private void GoIdle()
    {
        _idleTimer.Stop();
        if (_listOpen || IsScrubbing() || Root.GetVisualDescendants().OfType<Control>()
                .Any(c => c.Classes.Contains(ChromeClass) && c.IsPointerOver))
        {
            _idleTimer.Start();
            return;
        }
        Root.Classes.Set(IdleClass, true);
        Cursor = HiddenCursor;
    }

    private void SetPinned(bool open)
    {
        _listOpen = open;
        SoundBlock.Classes.Set(PinnedClass, open);
        if (!open)
            WakeUp();
    }

    // The flash on Keep, Yeet and Skip (#69): the colour of the decision around the picture, gone in
    // about half a second. Nothing with Reduce motion.
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(560);
    private const double FlashPeak = 0.18;
    private static readonly BoxShadows KeepFlash = BoxShadows.Parse("inset 0 0 0 4 #2FBF71, inset 0 0 120 0 #8C2FBF71");
    private static readonly BoxShadows YeetFlash = BoxShadows.Parse("inset 0 0 0 4 #EF5B5B, inset 0 0 120 0 #8CEF5B5B");
    private static readonly BoxShadows SkipFlash = BoxShadows.Parse("inset 0 0 0 4 #8A8FA0, inset 0 0 100 0 #668A8FA0");
    private SessionViewModel? _session;
    private CancellationTokenSource? _flashing;

    private void FollowDecisions()
    {
        if (_session is not null)
            _session.Decided -= OnDecided;
        _session = (DataContext as MainWindowViewModel)?.Session;
        if (_session is not null)
            _session.Decided += OnDecided;
    }

    private void OnDecided(object? sender, Decision decision)
    {
        if (Appearance.Instance.ReduceMotion)
            return;
        _flashing?.Cancel();
        _flashing = new CancellationTokenSource();
        Flash.BoxShadow = decision switch
        {
            Decision.Kept => KeepFlash,
            Decision.Yeeted => YeetFlash,
            _ => SkipFlash,
        };
        var animation = new Animation
        {
            Duration = FlashTime,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(FlashPeak), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        _ = animation.RunAsync(Flash, _flashing.Token);
    }

    private bool IsScrubbing() => DataContext is MainWindowViewModel { Player.IsScrubbing: true };

    private const string IdleClass = "idle";
    private const string ChromeClass = "chrome";
    private const string PinnedClass = "pinned";
    private const string OpenClass = "open";

    // Shows the overlay over the area and keeps it there as the owner moves or resizes.
    public void Follow(Window owner, Control area)
    {
        _owner = owner;
        _area = area;
        owner.PositionChanged += (_, _) => FitToArea();
        area.LayoutUpdated += (_, _) => FitToArea();
        owner.Closed += (_, _) => Close();
        WindowDialogs.DialogOpening += OnDialogOpening;
        WindowDialogs.DialogClosed += OnDialogClosed;
        Closed += (_, _) =>
        {
            WindowDialogs.DialogOpening -= OnDialogOpening;
            WindowDialogs.DialogClosed -= OnDialogClosed;
        };
        if (!_wanted || _dialogs > 0)
            return;
        ShowOverOwner();
    }

    // Hidden while the video area is (home, the Explorer page): a window over them would take their clicks.
    public void SetShown(bool shown)
    {
        _wanted = shown;
        Update();
    }

    // A dialog is open over the main window. This window sits over the main one too and could cover
    // the dialog (after switching apps, say), leaving a window that seems frozen: it steps aside
    // until every dialog is closed.
    private void OnDialogOpening(object? sender, EventArgs e)
    {
        _dialogs++;
        Update();
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        _dialogs = Math.Max(0, _dialogs - 1);
        Update();
    }

    private void Update()
    {
        var shown = _wanted && _dialogs == 0;
        if (_owner is null || shown == IsVisible)
            return;
        if (shown)
            ShowOverOwner();
        else
            Hide();
    }

    // Shown without taking the focus, this window can stay below the main one and be hidden by it:
    // it is put back right above it.
    private void ShowOverOwner()
    {
        if (_owner is null)
            return;
        Show(_owner);
        WindowStacking.PlaceAbove(this, _owner);
        FitToArea();
        // The controls show when the video appears, then rest like after any mouse move (#93).
        WakeUp();
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
