using Dapper;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Tests.Data;

// Sessions against a real database: order, journal, crash recovery, commit and discard (spec §5).
public class SessionTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 10, 11, 20, 0, 0);

    private readonly TestDataFolder _data = new();
    private readonly string _clips;
    private readonly SqliteLibraryStore _library;
    private readonly LibraryScanner _scanner;
    private readonly SqliteSessionStore _store;

    public SessionTests()
    {
        _data.Startup.Prepare();
        _data.Clock.Now = new DateTimeOffset(Recorded.AddDays(1), TimeSpan.Zero);
        _clips = Path.Combine(_data.Paths.Root, "Clips");
        Directory.CreateDirectory(_clips);
        _library = new SqliteLibraryStore(_data.Connections);
        _scanner = new LibraryScanner(_library, new FileSystemClipFiles(), new NotRecording(), new LibrarySettings(),
            _data.Clock, _data.Log);
        _store = new SqliteSessionStore(_data.Connections, _data.Clock);
    }

    public void Dispose() => _data.Dispose();

    private SessionService Service() => new(_store, new SqliteTagStore(_data.Connections), _data.Backups,
        new ScanScheduler(_scanner, _data.Clock), _data.Clock, _data.Log);

    // Clips named a, b, c... recorded one hour apart, in a scanned folder.
    private async Task AddClips(params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var path = Path.Combine(_clips, names[i] + ".mp4");
            await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
            File.SetLastWriteTime(path, Recorded.AddHours(i));
        }
        var folder = (await new SourceFolders(_library, _data.Clock).ListAsync()).SingleOrDefault()
                     ?? _library.AddFolder(_clips, includeSubfolders: false, Recorded);
        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.ReviewAll));
    }

    private string Decision(string name)
    {
        using var connection = _data.Connections.Open();
        return connection.ExecuteScalar<string>("SELECT Decision FROM Clips WHERE RelativePath = @p;", new { p = name + ".mp4" })!;
    }

    [Fact]
    public async Task New_clips_come_oldest_first()
    {
        await AddClips("b", "a", "c");

        Assert.Equal(["b.mp4", "a.mp4", "c.mp4"], _store.NewClips().Select(c => c.FileName));
    }

    [Fact]
    public async Task Missing_clips_are_not_offered()
    {
        await AddClips("a", "b");
        File.Delete(Path.Combine(_clips, "a.mp4"));
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["b.mp4"], _store.NewClips().Select(c => c.FileName));
        Assert.Equal(1, _store.CountNew());
    }

    [Fact]
    public async Task Commit_saves_decisions_and_skipped_clips_come_first_next_time()
    {
        await AddClips("a", "b", "c");
        var session = (await Service().StartAsync())!;
        session.Yeet();  // a
        session.Skip();  // b
        session.Keep();  // c

        await Service().CommitAsync(session.Id, session.Reviews);

        Assert.Equal(["Yeeted", "New", "Kept"], new[] { "a", "b", "c" }.Select(Decision));
        Assert.Equal(["b.mp4"], _store.NewClips().Select(c => c.FileName));
        Assert.Null(_store.FindUnfinished());
        Assert.Contains(_data.Backups.List(), b => b.Pool == Application.Data.BackupPool.Session);
    }

    [Fact]
    public async Task A_skipped_clip_is_offered_before_older_unseen_ones()
    {
        await AddClips("a", "b");
        var first = (await Service().StartAsync())!;
        first.Skip();   // a, seen
        first.Back();
        await Service().CommitAsync(first.Id, first.Reviews);
        await AddClips("a", "b", "z"); // z: a newer clip

        Assert.Equal("a.mp4", _store.NewClips().First().FileName);
    }

    [Fact]
    public async Task A_crashed_session_resumes_where_it_stopped()
    {
        await AddClips("a", "b", "c");
        var service = Service();
        var session = (await service.StartAsync())!;
        session.Keep();
        session.Yeet();
        await service.FlushJournalAsync();

        // The app is gone; a new one starts.
        var unfinished = _store.FindUnfinished()!;
        Assert.Equal(2, unfinished.Decided);
        var resumed = await Service().ResumeAsync(unfinished);

        Assert.Equal(2, resumed.Position);
        Assert.Equal([Domain.Clips.Decision.Kept, Domain.Clips.Decision.Yeeted, Domain.Clips.Decision.New],
            resumed.Reviews.Select(r => r.Decision));
        Assert.Equal(["New", "New", "New"], new[] { "a", "b", "c" }.Select(Decision)); // nothing final yet
    }

    [Fact]
    public async Task Markers_survive_a_crash_and_are_saved_with_the_clip()
    {
        await AddClips("a", "b");
        var service = Service();
        var session = (await service.StartAsync())!;
        session.AddMarker(TimeSpan.FromSeconds(87), n => $"Marker {n}");
        session.RenameMarker(1, "Smoke kill");
        session.Skip();
        await service.FlushJournalAsync();

        var resumed = await Service().ResumeAsync(_store.FindUnfinished()!);
        Assert.Equal([new Domain.Clips.Marker(1, TimeSpan.FromSeconds(87), "Smoke kill")], resumed.Reviews[0].Markers);
        await Service().CommitAsync(resumed.Id, resumed.Reviews);

        // The skipped clip comes back next time with its marker.
        var next = _store.NewClips().First();
        Assert.Equal("a.mp4", next.FileName);
        Assert.Equal("Smoke kill", Assert.Single(next.Markers).Label);
    }

    [Fact]
    public async Task The_summary_lists_kept_yeeted_and_skipped_clips()
    {
        await AddClips("a", "b", "c", "d");
        var session = (await Service().StartAsync())!;
        session.Keep();
        session.Yeet();
        session.Skip();

        var summary = SessionSummary.From(session);

        Assert.Equal(4, summary.Reviewed); // d is shown, so it counts as seen
        Assert.Equal(["a.mp4"], summary.Kept.Select(c => c.FileName));
        Assert.Equal(["b.mp4"], summary.Yeeted.Select(c => c.FileName));
        Assert.Equal(["c.mp4", "d.mp4"], summary.Skipped.Select(c => c.FileName));
        Assert.Equal(2, summary.Decisions);
    }

    [Fact]
    public async Task Discard_leaves_every_clip_new()
    {
        await AddClips("a", "b");
        var service = Service();
        var session = (await service.StartAsync())!;
        session.Keep();

        await service.DiscardAsync(session.Id);

        Assert.Equal(["New", "New"], new[] { "a", "b" }.Select(Decision));
        Assert.Null(_store.FindUnfinished());
    }

    [Fact]
    public async Task No_session_starts_without_new_clips() => Assert.Null(await Service().StartAsync());

    private sealed class NotRecording : IRecorderCheck
    {
        public bool IsRecorderRunning() => false;
    }
}
