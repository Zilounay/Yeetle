namespace Yeetle.Application.Shell;

// Hands things over to Windows itself.
public interface IShell
{
    void OpenFolder(string folder);

    // Opens Windows Explorer on the file's folder, with the file selected.
    void ShowInFolder(string path);

    // Opens the file with the app Windows uses for it, usually the default video player.
    void OpenWithDefaultApp(string path);

    // Shows Windows' own menu for these files at a screen point, with Yeetle's entries on top
    // (spec §8). Returns the index of the Yeetle entry chosen, or null when the user picked a
    // Windows entry (Windows runs it) or closed the menu. Entries that delete, cut or rename are
    // left out: only Yeetle changes files, after asking (spec §9). Must run on the UI thread.
    int? ShowFileMenu(nint window, int screenX, int screenY, IReadOnlyList<string> paths, IReadOnlyList<string> ownEntries);
}
