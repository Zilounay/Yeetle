namespace Yeetle.Domain.Clips;

// Which files count as clips (spec §4). The list becomes editable in Settings later.
public static class VideoFileTypes
{
    public static IReadOnlyList<string> DefaultExtensions { get; } =
        [".mp4", ".mkv", ".mov", ".flv", ".ts", ".webm", ".avi"];

    public static bool IsVideo(string path, IEnumerable<string> extensions)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
