using Avalonia.Controls;
using Avalonia.Platform;
using LibVLCSharp.Shared;

namespace VlcBaseline;

// Hosts VLC's video output in a child HWND inside the Avalonia window (option A).
// Replaces LibVLCSharp.Avalonia's VideoView, which is built against Avalonia 11: on
// Avalonia 12 its HWND hand-off doesn't happen, so VLC opened its own top-level window.
public sealed class NativeVideoView : NativeControlHost
{
    private MediaPlayer? _mediaPlayer;
    private IPlatformHandle? _handle;

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        set
        {
            if (_mediaPlayer is not null)
                _mediaPlayer.Hwnd = IntPtr.Zero;
            _mediaPlayer = value;
            Attach();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _handle = base.CreateNativeControlCore(parent);
        Attach();
        return _handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_mediaPlayer is not null)
            _mediaPlayer.Hwnd = IntPtr.Zero;
        _handle = null;
        base.DestroyNativeControlCore(control);
    }

    private void Attach()
    {
        if (_mediaPlayer is not null && _handle is not null)
            _mediaPlayer.Hwnd = _handle.Handle;
    }
}
