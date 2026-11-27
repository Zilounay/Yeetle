using Avalonia.Controls.ApplicationLifetimes;
using Yeetle.App.Services;

namespace Yeetle.Desktop;

// Closes the windows; Program starts the new copy once this one has let go of its single-instance
// lock, so the new one does not hand over to the old one and quit.
internal sealed class DesktopRestart : IAppRestart
{
    public static bool Requested { get; private set; }

    public void Restart()
    {
        Requested = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }
}
