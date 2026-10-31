using Dapper;
using Yeetle.Application.Library;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Tests.Data;

namespace Yeetle.Infrastructure.Tests.Clips;

// Durations are filled in after scans, unreadable files are not tried again, and a changed file
// is read again (spec §4).
public class DurationTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 10, 11, 22, 15, 36);

    private readonly TestDataFolder _data = new();
    private readonly string _clips;
    private readonly SqliteLibraryStore _store;
    private readonly LibraryScanner _scanner;
    private readonly FakeReader _reader = new();

    public DurationTests()
    {
        _data.Startup.Prepare();
        _data.Clock.Now = new DateTimeOffset(Recorded.AddDays(1), TimeSpan.Zero);
        _clips = Path.Combine(_data.Paths.Root, "Clips");
        Directory.CreateDirectory(_clips);
        _store = new SqliteLibraryStore(_data.Connections);
        _scanner = new LibraryScanner(_store, new FileSystemClipFiles(), new NotRecording(), new LibrarySettings(),
            _data.Clock, _data.Log);
    }

    public void Dispose() => _data.Dispose();

    private async Task AddClips(params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(_clips, name);
            await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
            File.SetLastWriteTime(path, Recorded);
        }
        var folder = _store.Folders().SingleOrDefault() ?? _store.AddFolder(_clips, includeSubfolders: false, Recorded);
        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.ReviewAll));
    }

    private Dictionary<string, long?> Durations()
    {
        using var connection = _data.Connections.Open();
        return connection.Query<(string Path, long? Ms)>("SELECT RelativePath, DurationMs FROM Clips;")
            .ToDictionary(r => r.Path, r => r.Ms);
    }

    [Fact]
    public async Task Fills_in_durations_and_marks_unreadable_files()
    {
        await AddClips("a.mp4", "broken.mp4");
        _reader.Durations["a.mp4"] = TimeSpan.FromSeconds(83);

        await new ClipDurations(_store, _reader, _scanner, _data.Log).FillMissingAsync();

        Assert.Equal(83_000, Durations()["a.mp4"]);
        Assert.Equal(ClipDurations.Unreadable, Durations()["broken.mp4"]);
        Assert.Equal(2, _reader.Reads);
    }

    [Fact]
    public async Task A_changed_file_is_read_again()
    {
        await AddClips("a.mp4");
        _reader.Durations["a.mp4"] = TimeSpan.FromSeconds(10);
        var durations = new ClipDurations(_store, _reader, _scanner, _data.Log);
        await durations.FillMissingAsync();

        File.SetLastWriteTime(Path.Combine(_clips, "a.mp4"), Recorded.AddMinutes(1));
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        _reader.Durations["a.mp4"] = TimeSpan.FromSeconds(20);
        await durations.FillMissingAsync();

        Assert.Equal(20_000, Durations()["a.mp4"]);
    }

    private sealed class FakeReader : IDurationReader
    {
        public Dictionary<string, TimeSpan> Durations { get; } = [];

        public int Reads { get; private set; }

        public TimeSpan? Read(string path)
        {
            Reads++;
            return Durations.TryGetValue(Path.GetFileName(path), out var duration) ? duration : null;
        }
    }

    private sealed class NotRecording : IRecorderCheck
    {
        public bool IsRecorderRunning() => false;
    }
}
