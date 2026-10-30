using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Files;

public enum FileOperationKind
{
    Delete,
    Rename,
    RestoreName,
}

// Why a file operation failed, in words the user understands (spec §9).
public enum FileProblem
{
    // The file is no longer where Yeetle expects it.
    Gone,
    // Another app has it open, e.g. an editor or a player.
    InUse,
    // Read-only, or Windows refused access.
    AccessDenied,
    // The drive or network share is not connected.
    DriveMissing,
    // A file with the new name is already there.
    NameTaken,
    // The new name does not fit in the Windows path limit.
    PathTooLong,
    // The app closed before the operation finished, and the file is still as it was.
    Interrupted,
    Other,
}

// A clip that a file operation may act on, as stored in the database.
public sealed record FileTarget(
    ClipId Clip,
    long FolderId,
    string FolderPath,
    string RelativePath,
    string OriginalFileName,
    DateTime RecordedAt,
    IReadOnlySet<TagId> Tags)
{
    public string FullPath => Path.Combine(FolderPath, RelativePath);

    public string FileName => Path.GetFileName(RelativePath);
}

public sealed record DeleteItem(ClipId Clip, string FullPath, long SizeBytes);

// What "Delete the yeeted files?" shows: the exact count, the total size and every file.
public sealed record DeletePlan(IReadOnlyList<DeleteItem> Items)
{
    public long TotalBytes => Items.Sum(i => i.SizeBytes);
}

// One line of the rename preview. NewName is null when even the date alone does not fit the path
// limit; such a clip is left alone.
public sealed record RenameItem(
    ClipId Clip, string FullPath, string OldName, string? NewName, string? NewRelativePath, bool Shortened, bool Numbered)
{
    public bool WillRename => NewName is not null && !string.Equals(NewName, OldName, StringComparison.Ordinal);
}

public sealed record RenamePreview(IReadOnlyList<RenameItem> Items)
{
    public IReadOnlyList<RenameItem> ToRename => Items.Where(i => i.WillRename).ToList();
}

// What happened to one file. Problem is null when it worked.
public sealed record FileOutcome(ClipId Clip, string FileName, string? NewName, FileProblem? Problem, string? Detail)
{
    public bool Succeeded => Problem is null;
}

// An operation journaled as planned and never finished, found at the next start (spec §9).
public sealed record PlannedOperation(long Id, ClipId Clip, FileOperationKind Kind, string FromPath, string? ToPath);
