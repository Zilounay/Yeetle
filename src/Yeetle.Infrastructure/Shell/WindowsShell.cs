using System.Diagnostics;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Shell;

namespace Yeetle.Infrastructure.Shell;

public sealed class WindowsShell(ILog log) : IShell
{
    private const string Explorer = "explorer.exe";

    public void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Explorer) { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error($"Could not open folder {folder}", e);
        }
    }
}
