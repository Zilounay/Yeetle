using Yeetle.Domain.Clips;

namespace Yeetle.Application.Library;

// Hidden: its clips are hidden. ScanStopped: its clips stay visible but it is not scanned (spec §4).
public enum FolderStatus
{
    Active,
    ScanStopped,
    Hidden,
}

public sealed record SourceFolder(
    long Id, string Path, bool IncludeSubfolders, FolderStatus Status, bool IsOffline, bool FirstScanDone);

// A video file as found on disk, its path relative to the source folder.
public sealed record DiskFile(string RelativePath, long SizeBytes, DateTime ModifiedAt);

// A clip as the database knows it.
public sealed record KnownClip(ClipId Id, string RelativePath, long SizeBytes, DateTime ModifiedAt, FileState FileState);

public sealed record ClipFile(ClipId Id, string FullPath);

public sealed record NewClip(DiskFile File, DateTime RecordedAt, Decision Decision);

// What one folder scan changes, written in one transaction.
public sealed record ScanChanges(
    IReadOnlyList<NewClip> Added,
    IReadOnlyList<(ClipId Id, DiskFile File)> Refreshed,
    IReadOnlyList<ClipId> Missing,
    IReadOnlyList<ClipId> Found)
{
    public bool IsEmpty => Added.Count == 0 && Refreshed.Count == 0 && Missing.Count == 0 && Found.Count == 0;
}

public enum FirstScanKind
{
    ReviewAll,
    KeepAll,
    // Clips recorded from the date on are reviewed; older ones are kept.
    ReviewFrom,
}

public sealed record FirstScanChoice(FirstScanKind Kind, DateTime From = default);

// A folder seen for the first time with clips in it: nothing is imported until the user chooses.
public sealed record PendingFirstScan(SourceFolder Folder, int ClipCount, DateTime Oldest, DateTime Newest);

public sealed record ScanReport(
    int Added,
    IReadOnlyList<string> Changed,
    int Missing,
    int StillRecording,
    IReadOnlyList<SourceFolder> Offline,
    IReadOnlyList<PendingFirstScan> PendingFirstScans)
{
    public static ScanReport Empty { get; } = new(0, [], 0, 0, [], []);

    public ScanReport Plus(ScanReport other) => new(
        Added + other.Added,
        [.. Changed, .. other.Changed],
        Missing + other.Missing,
        StillRecording + other.StillRecording,
        [.. Offline, .. other.Offline],
        [.. PendingFirstScans, .. other.PendingFirstScans]);
}

public enum FolderRemoval
{
    // The folder is marked Hidden and its clips disappear from view.
    HideClips,
    // The clips stay visible; the folder is no longer scanned.
    StopScanning,
}
