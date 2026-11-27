using Velopack;
using Velopack.Sources;
using Yeetle.Application.Updates;

namespace Yeetle.Infrastructure.Updates;

// Updates through Velopack from the repository's GitHub Releases (spec §12). The release workflow
// (.github/workflows/release.yml) publishes the packages this reads.
public sealed class VelopackUpdates : IAppUpdates
{
    private const string RepositoryUrl = "https://github.com/Zilounay/Yeetle";
    // Every release is a normal one, 0.x included (versions are plain X.Y.Z, no -beta).
    private const bool IncludePrereleases = false;

    // Public repository: no access token needed.
    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, IncludePrereleases));
    // What the last check found, kept for the download and the restart.
    private UpdateInfo? _found;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        _found = await _manager.CheckForUpdatesAsync();
        return _found is { } found ? new AvailableUpdate(found.TargetFullRelease.Version.ToString()) : null;
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation = default) =>
        _manager.DownloadUpdatesAsync(Found(update), progress, cancellation);

    public void RestartIntoUpdate(AvailableUpdate update) => _manager.ApplyUpdatesAndRestart(Found(update));

    private UpdateInfo Found(AvailableUpdate update) =>
        _found is { } found && found.TargetFullRelease.Version.ToString() == update.Version
            ? found
            : throw new InvalidOperationException($"Update {update.Version} was not found by the last check.");
}
