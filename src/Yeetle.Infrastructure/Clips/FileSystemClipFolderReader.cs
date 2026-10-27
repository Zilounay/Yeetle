using Yeetle.Application.Clips;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;

namespace Yeetle.Infrastructure.Clips;

// Reads the folder's root only, ordered by last-modified date until the recording date is
// read from the file (#5, #12).
public sealed class FileSystemClipFolderReader(AppSettings settings) : IClipFolderReader
{
    public Task<IReadOnlyList<string>> ListClipsAsync(string folder, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() => new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(f => VideoFileTypes.IsVideo(f.Name, settings.Library.VideoExtensions))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.FullName)
            .ToList(), cancellationToken);
}
