namespace Yeetle.Application.Playback;

// One video area of the player. The UI creates a native child window for each screen, all
// stacked in the same place, and hands over its handle (an HWND on Windows). The player draws
// into these windows and decides which one is on top.
public interface IVideoScreen
{
    void Attach(nint windowHandle);
    void Detach();

    // Called by the UI after it laid the window out again, so the player can restore which
    // screen is on top.
    void Refresh();
}
