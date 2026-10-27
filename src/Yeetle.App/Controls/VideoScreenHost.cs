using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Yeetle.Application.Playback;

namespace Yeetle.App.Controls;

// A native child window inside the Avalonia window, handed to the player to draw video into.
// LibVLCSharp.Avalonia's VideoView is not used: it targets Avalonia 11, and on 12 it never
// hands its window over, so VLC opens a separate one (POC #3).
//
// All screens stay visible at full size, stacked; the player decides which one is on top.
// Hiding one (IsVisible) would shrink its window to 1x1, so VLC could not prepare the next clip
// in it.
public sealed class VideoScreenHost : NativeControlHost
{
    public static readonly StyledProperty<IVideoScreen?> ScreenProperty =
        AvaloniaProperty.Register<VideoScreenHost, IVideoScreen?>(nameof(Screen));

    private IPlatformHandle? _handle;

    public IVideoScreen? Screen
    {
        get => GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ScreenProperty)
            return;
        (change.OldValue as IVideoScreen)?.Detach();
        Attach();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _handle = base.CreateNativeControlCore(parent);
        Attach();
        return _handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Screen?.Detach();
        _handle = null;
        base.DestroyNativeControlCore(control);
    }

    // Avalonia shows and places the native window during layout, which may change the stacking.
    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        Screen?.Refresh();
    }

    private void Attach()
    {
        if (_handle is not null)
            Screen?.Attach(_handle.Handle);
    }
}
