using Yeetle.Application.Diagnostics;

namespace Yeetle.Application.Updates;

// What a check found, for Settings' Check now (#20).
public enum UpdateOutcome
{
    UpToDate,
    Found,
    Failed,
    // Running from a build folder rather than the installer: there is nothing to update.
    NotInstalled,
}

public sealed record UpdateCheck(UpdateOutcome Outcome, AvailableUpdate? Update = null);

// The update flow the main window drives (spec §12): a check in the background at startup (unless
// turned off in Settings), or from Settings at any time, then, on the user's click, a download and
// a restart. A failure (offline, GitHub unreachable) never interrupts the user: the check just
// finds nothing, and the reason goes to the log.
public sealed class UpdateChecker(IAppUpdates updates, ILog log)
{
    // Null when there is no update, the check failed, or Yeetle is not installed.
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation = default) =>
        (await CheckNowAsync(cancellation)).Update;

    public async Task<UpdateCheck> CheckNowAsync(CancellationToken cancellation = default)
    {
        if (!updates.IsInstalled)
            return new UpdateCheck(UpdateOutcome.NotInstalled);
        try
        {
            var update = await updates.CheckAsync(cancellation);
            if (update is null)
                return new UpdateCheck(UpdateOutcome.UpToDate);
            log.Info($"Update available: {update.Version}.");
            return new UpdateCheck(UpdateOutcome.Found, update);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Error("Update check failed.", e);
            return new UpdateCheck(UpdateOutcome.Failed);
        }
    }

    // False when the download failed; the reason goes to the log.
    public async Task<bool> DownloadAsync(AvailableUpdate update, Action<int> progress,
        CancellationToken cancellation = default)
    {
        try
        {
            await updates.DownloadAsync(update, progress, cancellation);
            log.Info($"Update {update.Version} downloaded.");
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Error($"Update {update.Version} could not be downloaded.", e);
            return false;
        }
    }

    public void RestartIntoUpdate(AvailableUpdate update)
    {
        log.Info($"Restarting into update {update.Version}.");
        updates.RestartIntoUpdate(update);
    }
}
