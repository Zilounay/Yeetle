using Yeetle.Domain.Clips;

namespace Yeetle.Application.Library;

// Source folders and their clips in the database. Blocking calls: services run them off the UI thread.
public interface ILibraryStore
{
    IReadOnlyList<SourceFolder> Folders();

    SourceFolder AddFolder(string path, bool includeSubfolders, DateTime now);

    void UpdateFolder(SourceFolder folder);

    // Only for a folder without clips; one with clips is hidden or stops scanning instead.
    void DeleteFolder(long id);

    int CountClips(long folderId);

    IReadOnlyList<KnownClip> ClipsIn(long folderId);

    void Apply(long folderId, ScanChanges changes, DateTime now);

    // Present clips whose duration was never read, in folders that can be reached.
    IReadOnlyList<ClipFile> ClipsWithoutDuration(int limit);

    void SetDuration(ClipId clip, long durationMs);
}

// Reads source folders on disk; never writes to them (spec §4).
public interface IClipFiles
{
    bool FolderExists(string folder);

    // Video files only, paths relative to the folder.
    IReadOnlyList<DiskFile> List(string folder, bool includeSubfolders, IReadOnlyCollection<string> extensions);
}

// OBS writes the file it records; while it runs, new files may not be finished.
public interface IRecorderCheck
{
    bool IsRecorderRunning();
}
