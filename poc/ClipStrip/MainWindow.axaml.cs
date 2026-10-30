using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClipStrip.ViewModels;
using Yeetle.App.Input;
using Yeetle.App.Views;

namespace ClipStrip;

public partial class MainWindow : Window
{
    // One wheel notch scrolls about one card.
    private const double WheelStep = StripLayout.Pitch;

    public MainWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => ViewModel?.Player);
        Opened += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
                return;
            new VideoOverlayWindow { DataContext = viewModel.Player }.Follow(this, Player.Video);
            viewModel.Strip.PropertyChanged += OnStripChanged;
        };
        Closing += (_, _) => ViewModel?.Player.Stop();

        // Cards are made only when they scroll into view; that is when their thumbnail is wanted.
        Strip.ContainerPrepared += (_, e) => ViewModel?.Strip.Request(e.Index);
        // The strip only scrolls sideways, so the wheel does too.
        StripScroller.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            StripScroller.Offset += new Vector(-e.Delta.Y * WheelStep, 0);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnStripChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipStripViewModel.CurrentIndex) && ViewModel?.Strip.CurrentIndex is >= 0 and var index)
            CenterCard(index);
    }

    // Keeps the current card in the middle, so its previous and next clips show on both sides.
    private void CenterCard(int index)
    {
        var left = index * StripLayout.Pitch - (StripScroller.Viewport.Width - StripLayout.Pitch) / 2;
        StripScroller.Offset = new Vector(Math.Max(0, left), 0);
    }
}
