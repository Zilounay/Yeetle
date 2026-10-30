using System.Globalization;
using System.Text.Json;

namespace ClipStrip.Ffmpeg;

public enum RecordingTimeSource
{
    // creation_time written by the recorder (OBS, ShadowPlay...) into the container.
    File,
    // Nothing usable in the file: the file's last-modified date (spec §4).
    LastModified,
}

public sealed record ClipInfo(TimeSpan? Duration, DateTime RecordedUtc, RecordingTimeSource Source, DateTime LastModifiedUtc);

// ffprobe for the recording time and duration, ffmpeg for one thumbnail. Both run as processes
// (spec §13); every call has a timeout so a broken file is an error, never a hang.
public sealed class MediaTools(string? folder)
{
    // A healthy local clip answers in well under a second; a corrupt one usually fails at once.
    // The margin is for cold network drives.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public const int ThumbnailWidth = 320;

    // The thumbnail is taken a little into the clip, since the first frame is often black or a
    // fade-in, but early enough that seeking stays cheap.
    private const double ThumbnailFraction = 0.1;
    private static readonly TimeSpan MaxThumbnailPosition = TimeSpan.FromSeconds(10);

    // MP4 stores "no date" as 1904-01-01 and some muxers write 1970-01-01: anything this old is
    // treated as missing.
    private static readonly DateTime OldestPlausibleRecording = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string CreationTimeTag = "creation_time";
    private const string ExeExtension = ".exe";

    public string Ffprobe { get; } = Locate("ffprobe", folder);

    public string Ffmpeg { get; } = Locate("ffmpeg", folder);

    public async Task<ClipInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var lastModified = File.GetLastWriteTimeUtc(path);
        var result = await ProcessRunner.RunAsync(Ffprobe,
        [
            "-v", "error",
            "-print_format", "json",
            "-show_entries", "format=duration:format_tags=creation_time:stream=codec_type:stream_tags=creation_time",
            path,
        ], Timeout, cancellationToken);
        if (result.ExitCode != 0)
            throw new MediaToolException(FirstLine(result.Errors, "ffprobe failed"));

        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        var format = root.TryGetProperty("format", out var f) ? f : default;
        var duration = format.ValueKind == JsonValueKind.Object
                       && format.TryGetProperty("duration", out var d)
                       && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;

        // The container's date first, then the video stream's (MKV from some muxers only has the latter).
        var recorded = CreationTime(format) ?? VideoStreamCreationTime(root);
        return recorded is { } time
            ? new ClipInfo(duration, time, RecordingTimeSource.File, lastModified)
            : new ClipInfo(duration, lastModified, RecordingTimeSource.LastModified, lastModified);
    }

    // One JPEG, ThumbnailWidth wide.
    public async Task<byte[]> ThumbnailAsync(string path, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        var position = duration is { } length
            ? TimeSpan.FromTicks(Math.Min((long)(length.Ticks * ThumbnailFraction), MaxThumbnailPosition.Ticks))
            : TimeSpan.Zero;
        if (position > TimeSpan.Zero)
        {
            try
            {
                var image = await ExtractFrameAsync(path, position, cancellationToken);
                if (image.Length > 0)
                    return image;
            }
            catch (MediaToolException)
            {
                // Retried from the start below.
            }
        }
        // A cut-off file (an MKV left by a crash) still states its full duration in its header,
        // so the position can be past the end of its data; the start always has a picture.
        var first = await ExtractFrameAsync(path, TimeSpan.Zero, cancellationToken);
        return first.Length > 0 ? first : throw new MediaToolException("ffmpeg found no picture");
    }

    private async Task<byte[]> ExtractFrameAsync(string path, TimeSpan position, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Ffmpeg,
        [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            // Decode keyframes only and seek straight to one: a thumbnail needs no exact frame,
            // and this skips decoding everything between the keyframe and the position.
            "-skip_frame", "nokey",
            "-noaccurate_seek",
            "-ss", position.TotalSeconds.ToString(CultureInfo.InvariantCulture),
            "-i", path,
            "-an", "-sn", "-dn",
            "-frames:v", "1",
            "-vf", $"scale={ThumbnailWidth}:-2",
            "-c:v", "mjpeg",
            "-f", "image2pipe",
            "pipe:1",
        ], Timeout, cancellationToken);
        if (result.ExitCode != 0)
            throw new MediaToolException(FirstLine(result.Errors, "ffmpeg failed"));
        return result.Output;
    }

    private static DateTime? VideoStreamCreationTime(JsonElement root) =>
        root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray()
                .Where(s => s.TryGetProperty("codec_type", out var type) && type.GetString() == "video")
                .Select(CreationTime)
                .FirstOrDefault(t => t is not null)
            : null;

    private static DateTime? CreationTime(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("tags", out var tags)
            || !tags.TryGetProperty(CreationTimeTag, out var value)
            || !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            return null;
        return time >= OldestPlausibleRecording ? time : null;
    }

    private static string FirstLine(string text, string fallback) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? fallback;

    // The tool in the given folder, or by name so Windows finds it on the PATH.
    private static string Locate(string tool, string? folder) =>
        folder is null ? tool : Path.Combine(folder, tool + ExeExtension);
}
