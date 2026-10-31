namespace Yeetle.Application.Library;

// Adding, changing and removing source folders (spec §4). Files are never touched.
public sealed class SourceFolders(ILibraryStore store, TimeProvider clock)
{
    public Task<IReadOnlyList<SourceFolder>> ListAsync() => Task.Run(store.Folders);

    // Null when the folder is already a source folder.
    public Task<SourceFolder?> AddAsync(string path, bool includeSubfolders) => Task.Run(() =>
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (store.Folders().Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase)))
            return null;
        return (SourceFolder?)store.AddFolder(full, includeSubfolders, clock.GetLocalNow().DateTime);
    });

    public Task SetIncludeSubfoldersAsync(SourceFolder folder, bool include) =>
        Task.Run(() => store.UpdateFolder(folder with { IncludeSubfolders = include }));

    // Turns a stopped or hidden folder back into a scanned one.
    public Task ResumeAsync(SourceFolder folder) =>
        Task.Run(() => store.UpdateFolder(folder with { Status = FolderStatus.Active }));

    public Task<int> CountClipsAsync(SourceFolder folder) => Task.Run(() => store.CountClips(folder.Id));

    // A folder without clips is removed silently; one with clips is hidden or stops scanning.
    public Task RemoveAsync(SourceFolder folder, FolderRemoval removal) => Task.Run(() =>
    {
        if (store.CountClips(folder.Id) == 0)
            store.DeleteFolder(folder.Id);
        else
            store.UpdateFolder(folder with
            {
                Status = removal == FolderRemoval.HideClips ? FolderStatus.Hidden : FolderStatus.ScanStopped,
            });
    });
}
