namespace Yeetle.Application.Clips;

// Lists the video files of a folder, oldest first. Stands in for scanning source folders (#12).
public interface IClipFolderReader
{
    Task<IReadOnlyList<string>> ListClipsAsync(string folder, CancellationToken cancellationToken = default);
}
