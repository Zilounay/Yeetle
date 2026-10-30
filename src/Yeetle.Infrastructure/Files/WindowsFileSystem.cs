using System.Runtime.InteropServices;
using Yeetle.Application.Files;

namespace Yeetle.Infrastructure.Files;

// Deletes through the Windows shell, so files go to the Recycle Bin when Windows allows it, and
// renames with File.Move, which never overwrites (spec §9).
public sealed class WindowsFileSystem : IFileSystem
{
    private const string Shell32 = "shell32.dll";

    // SHFileOperation: delete, allow undo (the Recycle Bin), and never show a window of its own,
    // since Yeetle reports every outcome itself.
    private const uint FoDelete = 0x3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;

    // Win32 error codes, as found in IOException.HResult.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int FileExists = unchecked((int)0x80070050);
    private const int AlreadyExists = unchecked((int)0x800700B7);
    private const int NotReady = unchecked((int)0x80070015);

    public bool Exists(string path) => File.Exists(path);

    public long SizeOf(string path) => new FileInfo(path).Length;

    public FileProblem? Delete(string path, out string? detail)
    {
        detail = null;
        if (Missing(path) is { } missing)
            return missing;
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            // The list of files ends with an extra null character.
            pFrom = path + '\0',
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        var result = SHFileOperation(ref operation);
        if (!File.Exists(path))
            return null;
        detail = $"SHFileOperation 0x{result:X}";
        // Still there: find out why by opening it the way the delete would.
        return WhyLocked(path) ?? FileProblem.Other;
    }

    public FileProblem? Rename(string from, string to, out string? detail)
    {
        detail = null;
        if (Missing(from) is { } missing)
            return missing;
        try
        {
            File.Move(from, to, overwrite: false);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            detail = e.Message;
            return Classify(e);
        }
    }

    private static FileProblem? Missing(string path)
    {
        if (File.Exists(path))
            return null;
        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) || Directory.Exists(root) ? FileProblem.Gone : FileProblem.DriveMissing;
    }

    private static FileProblem? WhyLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Classify(e);
        }
    }

    private static FileProblem Classify(Exception e) => e switch
    {
        PathTooLongException => FileProblem.PathTooLong,
        FileNotFoundException or DirectoryNotFoundException => FileProblem.Gone,
        DriveNotFoundException => FileProblem.DriveMissing,
        UnauthorizedAccessException => FileProblem.AccessDenied,
        IOException { HResult: SharingViolation or LockViolation } => FileProblem.InUse,
        IOException { HResult: FileExists or AlreadyExists } => FileProblem.NameTaken,
        IOException { HResult: NotReady } => FileProblem.DriveMissing,
        _ => FileProblem.Other,
    };

    [DllImport(Shell32, CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
}
