using System.Runtime.InteropServices;

namespace Yeetle.Media.Vlc;

// Win32 calls for the video windows. The screens' windows are stacked in the same place inside
// the app's top-level window; the one whose branch is on top is the one seen.
internal static class NativeWindows
{
    private const string User32 = "user32.dll";
    private static readonly nint NoWindow = nint.Zero;
    private static readonly nint HwndTop = nint.Zero; // the top of the z-order

    // Win32 values, from WinUser.h.
    private const uint GaRoot = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    // Raises the branch holding the window (the UI may wrap it in holder windows) above its
    // siblings under the top-level window. Called from another thread, this waits until the
    // UI thread has applied it.
    public static void BringToFront(nint window)
    {
        var root = GetAncestor(window, GaRoot);
        if (window == NoWindow || root == NoWindow)
            return;
        var branch = window;
        while (GetParent(branch) is var parent && parent != NoWindow && parent != root)
            branch = parent;
        // Position and size are ignored: SwpNoMove and SwpNoSize keep them.
        SetWindowPos(branch, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [DllImport(User32)]
    private static extern nint GetAncestor(nint hWnd, uint flags);

    [DllImport(User32)]
    private static extern nint GetParent(nint hWnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy,
        uint flags);
}
