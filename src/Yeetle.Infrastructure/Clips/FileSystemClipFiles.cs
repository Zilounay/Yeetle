using Yeetle.Application.Library;
using Yeetle.Domain.Clips;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Clips;

// Lists video files under a source folder; only reads (spec §4).
public sealed class FileSystemClipFiles : IClipFiles
{
    public bool FolderExists(string folder) => Directory.Exists(folder);

    public IReadOnlyList<DiskFile> List(string folder, bool includeSubfolders, IReadOnlyCollection<string> extensions)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            // A subfolder Yeetle may not read is skipped rather than failing the whole scan.
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        return new DirectoryInfo(folder).EnumerateFiles("*", options)
            .Where(f => VideoFileTypes.IsVideo(f.Name, extensions))
            .Select(f => new DiskFile(Path.GetRelativePath(folder, f.FullName), f.Length, DbTime.Truncate(f.LastWriteTime)))
            .ToList();
    }
}
