using System.Diagnostics;
using System.Globalization;
using ClipStrip.Ffmpeg;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Clips;

namespace ClipStrip;

// --bench: probes and thumbnails every clip of the folder one after the other, prints one line
// per clip and a summary, and writes the lines to a CSV next to the exe.
internal static class Bench
{
    private const string CsvSeparator = ";";
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const string CsvNamePattern = "bench-{0:yyyyMMdd-HHmmss}.csv";
    private const int NameWidth = 40;
    private const double BytesPerMegabyte = 1024 * 1024;

    public static async Task<int> RunAsync(string? folder, MediaTools tools, AppSettings settings)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Usage: ClipStrip <folder> --bench [--ffmpeg <folder>]");
            return 1;
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var clips = await new FileSystemClipFolderReader(settings).ListClipsAsync(folder);
        Console.WriteLine($"{clips.Count} clips in {folder}");
        Console.WriteLine($"ffprobe: {tools.Ffprobe}");
        Console.WriteLine($"ffmpeg:  {tools.Ffmpeg}");
        Console.WriteLine();

        var probeTimes = new Timings();
        var frameTimes = new Timings();
        var fromFile = 0;
        var failed = 0;
        var csv = new List<string>
        {
            Csv("file", "size_mb", "probe_ms", "frame_ms", "thumb_bytes", "date_source", "recorded", "modified",
                "modified_minus_recorded_s", "error"),
        };

        foreach (var clip in clips)
        {
            var name = Path.GetFileName(clip);
            var size = new FileInfo(clip).Length / BytesPerMegabyte;
            var clock = Stopwatch.StartNew();
            try
            {
                var info = await tools.ProbeAsync(clip);
                var probe = clock.Elapsed;
                clock.Restart();
                var jpeg = await tools.ThumbnailAsync(clip, info.Duration);
                var frame = clock.Elapsed;

                probeTimes.Add(probe);
                frameTimes.Add(frame);
                if (info.Source == RecordingTimeSource.File)
                    fromFile++;
                var gap = (info.LastModifiedUtc - info.RecordedUtc).TotalSeconds;
                Console.WriteLine($"{Short(name)}  probe {probe.TotalMilliseconds,5:0} ms  frame {frame.TotalMilliseconds,5:0} ms  "
                                  + $"{Source(info.Source),-5} {info.RecordedUtc.ToLocalTime().ToString(DateFormat)}  mtime-rec {gap,7:0} s");
                csv.Add(Csv(name, Number(size), Number(probe.TotalMilliseconds), Number(frame.TotalMilliseconds),
                    jpeg.Length.ToString(CultureInfo.InvariantCulture), Source(info.Source),
                    info.RecordedUtc.ToString("O"), info.LastModifiedUtc.ToString("O"), Number(gap), string.Empty));
            }
            catch (Exception e) when (e is MediaToolException or IOException or System.Text.Json.JsonException)
            {
                failed++;
                Console.WriteLine($"{Short(name)}  FAILED after {clock.Elapsed.TotalMilliseconds:0} ms: {e.Message}");
                csv.Add(Csv(name, Number(size), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, e.Message));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"ffprobe:       {probeTimes.Summary()}");
        Console.WriteLine($"ffmpeg frame:  {frameTimes.Summary()}");
        Console.WriteLine($"date in file:  {fromFile} / {clips.Count - failed}   failed: {failed}");

        var csvPath = Path.Combine(AppContext.BaseDirectory, string.Format(CultureInfo.InvariantCulture, CsvNamePattern, DateTime.Now));
        await File.WriteAllLinesAsync(csvPath, csv);
        Console.WriteLine($"CSV:           {csvPath}");
        return 0;
    }

    private static string Source(RecordingTimeSource source) => source == RecordingTimeSource.File ? "file" : "mtime";

    private static string Short(string name) =>
        (name.Length > NameWidth ? name[..(NameWidth - 1)] + "…" : name).PadRight(NameWidth);

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Csv(params string[] values) => string.Join(CsvSeparator, values.Select(v => v.Replace(CsvSeparator, ",")));
}
