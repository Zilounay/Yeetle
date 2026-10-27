using LibVLCSharp.Shared;
using Yeetle.Application.Playback;

namespace Yeetle.Media.Vlc;

// One LibVLC media player and the native window it draws into.
internal sealed class VlcScreen(MediaPlayer player) : IVideoScreen
{
    private volatile bool _isActive;
    private volatile bool _isReady;
    private nint _window;

    public MediaPlayer Player { get; } = player;

    // The screen playing the current clip. It is brought to the front once its picture moves.
    public bool IsActive
    {
        get => _isActive;
        set => _isActive = value;
    }

    // The clip loaded in this screen, or null.
    public string? Clip { get; set; }

    // True once a preloaded clip reached its first frame, paused.
    public bool IsReady
    {
        get => _isReady;
        set => _isReady = value;
    }

    // Setting the handle only stores it in LibVLC; it is used when the next clip starts.
    public void Attach(nint windowHandle)
    {
        _window = windowHandle;
        Player.Hwnd = windowHandle;
        Refresh();
    }

    public void Detach()
    {
        _window = nint.Zero;
        Player.Hwnd = IntPtr.Zero;
    }

    public void Refresh()
    {
        if (IsActive)
            BringToFront();
    }

    public void BringToFront() => NativeWindows.BringToFront(_window);
}
