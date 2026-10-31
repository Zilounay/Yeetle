using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Yeetle.Infrastructure.Shell;

// The genuine Windows context menu for files (Open with, Send to, Properties, shell extensions),
// with the caller's own entries on top. Built from the files' folder through IShellFolder and
// IContextMenu, as Windows Explorer does.
[SupportedOSPlatform("windows")]
internal static class ShellContextMenu
{
    // Command ids: the caller's entries first, then the shell's, which it numbers from ShellFirst.
    private const uint OwnFirst = 1;
    private const uint ShellFirst = 0x100;
    private const uint ShellLast = 0x7FFF;

    // Verbs left out of the menu: only Yeetle deletes or renames files, after asking (spec §9).
    private static readonly HashSet<string> RemovedVerbs = new(StringComparer.OrdinalIgnoreCase) { "delete", "cut", "rename" };

    private const uint CmfNormal = 0x0;
    private const uint MfByPosition = 0x400;
    private const uint MfByCommand = 0x0;
    private const uint MfString = 0x0;
    private const uint MfSeparator = 0x800;
    private const uint TpmReturnCmd = 0x100;
    private const uint TpmRightButton = 0x2;
    private const uint GcsVerbW = 0x4;
    private const int ShowNormal = 1;
    private const int CmicMaskUnicode = 0x4000;
    private const int CmicMaskPtInvoke = 0x20000000;
    // Longest verb read back from the shell.
    private const int VerbLength = 64;

    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214e4-0000-0000-c000-000000000046");

    public static int? Show(nint window, int x, int y, IReadOnlyList<string> paths, IReadOnlyList<string> ownEntries)
    {
        var menu = CreatePopupMenu();
        var pidls = new List<nint>();
        object? parent = null;
        IContextMenu? shellMenu = null;
        try
        {
            for (var i = 0; i < ownEntries.Count; i++)
                InsertMenu(menu, (uint)i, MfByPosition | MfString, OwnFirst + (uint)i, ownEntries[i]);
            shellMenu = ShellMenuFor(window, paths, pidls, out parent);
            if (shellMenu is not null)
            {
                InsertMenu(menu, (uint)ownEntries.Count, MfByPosition | MfSeparator, 0, null);
                shellMenu.QueryContextMenu(menu, (uint)ownEntries.Count + 1, ShellFirst, ShellLast, CmfNormal);
                RemoveVerbs(menu, shellMenu);
            }

            using var host = new MenuHost(shellMenu);
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton, x, y, host.Handle, nint.Zero);
            if (chosen >= OwnFirst && chosen < OwnFirst + ownEntries.Count)
                return (int)(chosen - OwnFirst);
            if (chosen >= ShellFirst && shellMenu is not null)
                Invoke(shellMenu, window, chosen - ShellFirst, x, y);
            return null;
        }
        finally
        {
            DestroyMenu(menu);
            if (shellMenu is not null)
                Marshal.ReleaseComObject(shellMenu);
            if (parent is not null)
                Marshal.ReleaseComObject(parent);
            foreach (var pidl in pidls)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    // Null when the files are not all in one folder, or Windows does not know them: then only the
    // caller's entries are shown.
    private static IContextMenu? ShellMenuFor(nint window, IReadOnlyList<string> paths, List<nint> pidls, out object? parent)
    {
        parent = null;
        var folders = paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0 || folders.Count != 1)
            return null;
        foreach (var path in paths)
        {
            if (SHParseDisplayName(path, nint.Zero, out var pidl, 0, out _) != 0)
                return null;
            pidls.Add(pidl);
        }
        var children = new nint[pidls.Count];
        for (var i = 0; i < pidls.Count; i++)
        {
            if (SHBindToParent(pidls[i], ShellFolderId, out var folder, out children[i]) != 0)
                return null;
            if (parent is null)
                parent = folder;
            else
                Marshal.ReleaseComObject(folder);
        }
        if (parent is not IShellFolder shellFolder)
            return null;
        var menuId = ContextMenuId;
        if (shellFolder.GetUIObjectOf(window, (uint)children.Length, children, ref menuId, nint.Zero, out var menu) != 0)
            return null;
        // The wrapper takes a reference of its own; the one GetUIObjectOf handed over is released.
        var wrapper = (IContextMenu)Marshal.GetObjectForIUnknown(menu);
        Marshal.Release(menu);
        return wrapper;
    }

    private static void RemoveVerbs(nint menu, IContextMenu shellMenu)
    {
        var verb = new char[VerbLength];
        for (var i = GetMenuItemCount(menu) - 1; i >= 0; i--)
        {
            var id = GetMenuItemID(menu, i);
            if (id < ShellFirst || id > ShellLast)
                continue;
            Array.Clear(verb);
            if (shellMenu.GetCommandString((nuint)(id - ShellFirst), GcsVerbW, nint.Zero, verb, (uint)verb.Length) != 0)
                continue;
            if (RemovedVerbs.Contains(new string(verb).TrimEnd('\0')))
                DeleteMenu(menu, id, MfByCommand);
        }
    }

    private static void Invoke(IContextMenu shellMenu, nint window, uint offset, int x, int y)
    {
        var info = new CmInvokeCommandInfoEx
        {
            cbSize = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
            fMask = CmicMaskUnicode | CmicMaskPtInvoke,
            hwnd = window,
            lpVerb = (nint)offset,
            lpVerbW = (nint)offset,
            nShow = ShowNormal,
            ptInvoke = new Point { X = x, Y = y },
        };
        shellMenu.InvokeCommand(ref info);
    }

    // A hidden window that owns the menu and passes its messages to the shell, which fills the
    // "Open with" and "Send to" submenus only when they open (IContextMenu2/3).
    private sealed class MenuHost : IDisposable
    {
        private const string ClassName = "Yeetle.ShellMenuHost";
        private const uint WmInitMenuPopup = 0x0117;
        private const uint WmDrawItem = 0x002B;
        private const uint WmMeasureItem = 0x002C;
        private const uint WmMenuChar = 0x0120;
        private static readonly nint MessageOnlyParent = -3;

        private static readonly WndProc Procedure = OnMessage;
        private static readonly Lazy<bool> Registered = new(() =>
            RegisterClassEx(new WndClassEx
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
                hInstance = Marshal.GetHINSTANCE(typeof(MenuHost).Module),
                lpszClassName = ClassName,
            }) != 0);

        [ThreadStatic]
        private static IContextMenu? _current;

        public MenuHost(IContextMenu? menu)
        {
            _current = menu;
            Handle = Registered.Value
                ? CreateWindowEx(0, ClassName, string.Empty, 0, 0, 0, 0, 0, MessageOnlyParent, nint.Zero, nint.Zero, nint.Zero)
                : nint.Zero;
        }

        public nint Handle { get; }

        public void Dispose()
        {
            _current = null;
            if (Handle != nint.Zero)
                DestroyWindow(Handle);
        }

        private static nint OnMessage(nint window, uint message, nint wParam, nint lParam)
        {
            if (_current is not null && message is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
            {
                if (_current is IContextMenu3 menu3 && menu3.HandleMenuMsg2(message, wParam, lParam, out var result) == 0)
                    return result;
                if (_current is IContextMenu2 menu2 && menu2.HandleMenuMsg(message, wParam, lParam) == 0)
                    return nint.Zero;
            }
            return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, int flags, out nint enumIdList);
        [PreserveSig] int BindToObject(nint pidl, nint bindContext, ref Guid riid, out nint result);
        [PreserveSig] int BindToStorage(nint pidl, nint bindContext, ref Guid riid, out nint result);
        [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
        [PreserveSig] int CreateViewObject(nint hwndOwner, ref Guid riid, out nint result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] nint[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint count, [MarshalAs(UnmanagedType.LPArray)] nint[] pidls, ref Guid riid, nint reserved, out nint result);
        [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint name);
        [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint pidlOut);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, [MarshalAs(UnmanagedType.LPArray)] char[] name, uint maxLength);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, [MarshalAs(UnmanagedType.LPArray)] char[] name, uint maxLength);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, [MarshalAs(UnmanagedType.LPArray)] char[] name, uint maxLength);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmInvokeCommandInfoEx
    {
        public int cbSize;
        public int fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public int dwHotKey;
        public nint hIcon;
        public nint lpTitle;
        public nint lpVerbW;
        public nint lpParametersW;
        public nint lpDirectoryW;
        public nint lpTitleW;
        public Point ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint resultAttributes);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(nint pidl, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object folder, out nint childPidl);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(nint menu, uint position, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DeleteMenu(nint menu, uint position, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(nint menu, int position);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(in WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
}
