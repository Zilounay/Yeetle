using Yeetle.Domain.Clips;

namespace Yeetle.Application.Files;

// The clips file operations act on, and the FileOperations log (spec §9). Blocking calls:
// FileOperations runs them in the background.
public interface IFileOperationStore
{
    IReadOnlyList<FileTarget> Targets(IReadOnlyCollection<ClipId> clips);

    // Whether another clip of the folder is recorded at this path (it may be Missing on disk).
    bool IsPathKnown(long folderId, string relativePath, ClipId except);

    // Journals an operation before it starts.
    long Plan(ClipId clip, FileOperationKind kind, string fromPath, string? toPath, DateTime now);

    // Records success and what it changed in one transaction: a deleted clip gets the Deleted
    // file state, a renamed one its new path.
    void Complete(long operationId, ClipId clip, FileOperationKind kind, string? newRelativePath, DateTime now);

    void Fail(long operationId, FileProblem problem, string? detail, DateTime now);

    IReadOnlyList<PlannedOperation> Unfinished();
}

// The only code that changes a user's video files (spec §9, CLAUDE.md invariant 2). Each method
// returns null when it worked, or why it did not.
public interface IFileSystem
{
    bool Exists(string path);

    long SizeOf(string path);

    // To the Recycle Bin where Windows allows it; network drives, USB drives and very large
    // files are deleted for good, as the prompt warns.
    FileProblem? Delete(string path, out string? detail);

    // Never replaces an existing file.
    FileProblem? Rename(string from, string to, out string? detail);
}
