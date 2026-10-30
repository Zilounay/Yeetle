using Yeetle.Application.Diagnostics;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;

namespace Yeetle.Application.Library;

// Reconciles the database with the source folders (spec §4). One scan runs at a time; it reads
// the folders and never writes to them.
public sealed class LibraryScanner(
    ILibraryStore store, IClipFiles files, IRecorderCheck recorder, LibrarySettings settings, TimeProvider clock, ILog log)
{
    // A file changed more recently than this may still be being written.
    public static readonly TimeSpan StillWritingWindow = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<ScanReport> ScanAsync(CancellationToken cancellationToken = default) => Run(() =>
    {
        var recording = recorder.IsRecorderRunning();
        return store.Folders()
            .Where(f => f.Status == FolderStatus.Active)
            .Aggregate(ScanReport.Empty, (report, folder) => report.Plus(ScanFolder(folder, choice: null, recording)));
    }, cancellationToken);

    // Imports a first-scanned folder the way the user chose.
    public Task<ScanReport> CompleteFirstScanAsync(long folderId, FirstScanChoice choice) => Run(() =>
    {
        var folder = store.Folders().Single(f => f.Id == folderId);
        return ScanFolder(folder, choice, recorder.IsRecorderRunning());
    }, CancellationToken.None);

    private async Task<ScanReport> Run(Func<ScanReport> scan, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(scan, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ScanReport ScanFolder(SourceFolder folder, FirstScanChoice? choice, bool recording)
    {
        if (!files.FolderExists(folder.Path))
        {
            // An unreachable folder leaves its clips alone: they are not Missing (spec §4).
            if (!folder.IsOffline)
                store.UpdateFolder(folder with { IsOffline = true });
            log.Info($"Source folder offline: {folder.Path}");
            return ScanReport.Empty with { Offline = [folder] };
        }
        if (folder.IsOffline)
            store.UpdateFolder(folder = folder with { IsOffline = false });

        IReadOnlyList<DiskFile> onDisk;
        try
        {
            onDisk = files.List(folder.Path, folder.IncludeSubfolders, settings.VideoExtensions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error($"Source folder could not be read: {folder.Path}", e);
            return ScanReport.Empty with { Offline = [folder] };
        }

        var known = store.ClipsIn(folder.Id).ToDictionary(c => c.RelativePath, StringComparer.OrdinalIgnoreCase);
        var now = clock.GetLocalNow().DateTime;
        var fresh = onDisk.Where(f => !known.ContainsKey(f.RelativePath)).ToList();
        var ready = fresh.Where(f => !recording && now - f.ModifiedAt >= StillWritingWindow).ToList();
        var stillRecording = fresh.Count - ready.Count;

        if (!folder.FirstScanDone && choice is null && ready.Count > 0)
        {
            var dates = ready.Select(RecordedAt).ToList();
            return ScanReport.Empty with
            {
                StillRecording = stillRecording,
                PendingFirstScans = [new PendingFirstScan(folder, ready.Count, dates.Min(), dates.Max())],
            };
        }

        var onDiskByPath = onDisk.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var refreshed = new List<(ClipId, DiskFile)>();
        var found = new List<ClipId>();
        var missing = new List<ClipId>();
        foreach (var clip in known.Values)
        {
            if (onDiskByPath.TryGetValue(clip.RelativePath, out var file))
            {
                if (clip.FileState == FileState.Missing)
                    found.Add(clip.Id);
                if (file.SizeBytes != clip.SizeBytes || file.ModifiedAt != clip.ModifiedAt)
                    refreshed.Add((clip.Id, file));
            }
            else if (clip.FileState is FileState.Present or FileState.NeedsAttention)
            {
                missing.Add(clip.Id);
            }
        }

        var added = ready.Select(f => new NewClip(f, RecordedAt(f), DecisionFor(RecordedAt(f), choice))).ToList();
        var changes = new ScanChanges(added, refreshed, missing, found);
        if (!changes.IsEmpty)
            store.Apply(folder.Id, changes, now);
        if (!folder.FirstScanDone)
            store.UpdateFolder(folder with { FirstScanDone = true });
        if (!changes.IsEmpty)
            log.Info($"Scanned {folder.Path}: {added.Count} new, {refreshed.Count} changed, {missing.Count} missing, {found.Count} back.");

        return new ScanReport(added.Count, refreshed.Select(r => r.Item2.RelativePath).ToList(), missing.Count,
            stillRecording, [], []);
    }

    // The recording date is the file's last-modified date (#5: thumbnails and FFmpeg postponed).
    private static DateTime RecordedAt(DiskFile file) => file.ModifiedAt;

    private static Decision DecisionFor(DateTime recordedAt, FirstScanChoice? choice) => choice?.Kind switch
    {
        FirstScanKind.KeepAll => Decision.Kept,
        FirstScanKind.ReviewFrom when recordedAt < choice.From => Decision.Kept,
        _ => Decision.New,
    };
}
