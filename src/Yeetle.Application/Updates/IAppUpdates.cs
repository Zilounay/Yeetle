namespace Yeetle.Application.Updates;

// A newer version published on GitHub Releases (spec §12). Version is shown to the user, e.g. "0.0.2".
public sealed record AvailableUpdate(string Version);

// One-click updates (spec §12): installed copies check GitHub Releases, download the new version
// and restart into it. The only network calls besides usage statistics (CLAUDE.md invariant 5).
public interface IAppUpdates
{
    // False when Yeetle runs from a build folder rather than from its installer: there is nothing
    // to update then, so no check is made.
    bool IsInstalled { get; }

    // Null when this is the newest version.
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation = default);

    // Reports progress from 0 to 100.
    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation = default);

    // Closes Yeetle, installs the downloaded update and starts the new version. Does not return.
    void RestartIntoUpdate(AvailableUpdate update);
}
