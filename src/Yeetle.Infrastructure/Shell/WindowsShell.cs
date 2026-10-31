using System.Diagnostics;
using System.Runtime.InteropServices;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Shell;

namespace Yeetle.Infrastructure.Shell;

public sealed class WindowsShell(ILog log) : IShell
{
    private const string Explorer = "explorer.exe";
    private const string SelectArgument = "/select,";

    public void OpenFolder(string folder) => Start(new ProcessStartInfo(Explorer) { ArgumentList = { folder }, UseShellExecute = false },
        folder);

    // Explorer reads "/select," and the path as one argument.
    public void ShowInFolder(string path) =>
        Start(new ProcessStartInfo(Explorer) { Arguments = $"{SelectArgument}\"{path}\"", UseShellExecute = false }, path);

    public void OpenWithDefaultApp(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true }, path);

    public int? ShowFileMenu(nint window, int screenX, int screenY, IReadOnlyList<string> paths, IReadOnlyList<string> ownEntries)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return ShellContextMenu.Show(window, screenX, screenY, paths, ownEntries);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            log.Error("The Windows context menu could not be shown.", e);
            return null;
        }
    }

    private void Start(ProcessStartInfo start, string target)
    {
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error($"Could not open {target}", e);
        }
    }
}
