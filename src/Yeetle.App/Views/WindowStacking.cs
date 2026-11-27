using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Yeetle.App.Views;

// Win32 stacking for the controls window. Windows keeps an owned window above its owner only once
// the owner is reordered: a window shown later without being activated (ShowActivated="False")
// can stay below the owner, hidden behind it and behind other apps' windows.
internal static class WindowStacking
{
    private const string User32 = "user32.dll";
    private static readonly nint NoWindow = nint.Zero;
    private static readonly nint HwndTop = nint.Zero; // the top of the z-order

    // Win32 values, from WinUser.h.
    private const uint GwHwndPrev = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    // Puts the window directly above its owner, without activating it, moving or resizing it.
    public static void PlaceAbove(Window window, Window owner)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle
            || owner.TryGetPlatformHandle()?.Handle is not { } ownerHandle)
            return;
        // SetWindowPos inserts after (below) the given window: the one just above the owner.
        var above = GetWindow(ownerHandle, GwHwndPrev);
        if (above == handle)
            return;
        var insertAfter = above == NoWindow ? HwndTop : above;
        // Position and size are ignored: SwpNoMove and SwpNoSize keep them.
        SetWindowPos(handle, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [DllImport(User32)]
    private static extern nint GetWindow(nint hWnd, uint command);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy,
        uint flags);
}
