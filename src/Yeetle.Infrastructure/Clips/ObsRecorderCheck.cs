using System.Diagnostics;
using Yeetle.Application.Library;

namespace Yeetle.Infrastructure.Clips;

// OBS Studio's process names, 64- and 32-bit.
public sealed class ObsRecorderCheck : IRecorderCheck
{
    private static readonly string[] ProcessNames = ["obs64", "obs32"];

    public bool IsRecorderRunning() => ProcessNames.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    });
}
