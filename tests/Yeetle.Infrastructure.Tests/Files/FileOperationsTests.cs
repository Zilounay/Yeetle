using Dapper;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Files;
using Yeetle.Infrastructure.Tests.Data;

namespace Yeetle.Infrastructure.Tests.Files;

// Delete, rename and restore against real files in a temporary folder (spec §9).
public class FileOperationsTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 10, 11, 22, 15, 36);

    private readonly TestDataFolder _data = new();
    private readonly string _clips;
    private readonly SqliteLibraryStore _library;
    private readonly LibraryScanner _scanner;
    private readonly SqliteTagStore _tags;
    private readonly SqliteFileOperationStore _store;

    public FileOperationsTests()
    {
        _data.Startup.Prepare();
        _data.Clock.Now = new DateTimeOffset(Recorded.AddDays(1), TimeSpan.Zero);
        _clips = Path.Combine(_data.Paths.Root, "Clips");
        Directory.CreateDirectory(_clips);
        _library = new SqliteLibraryStore(_data.Connections);
        _scanner = new LibraryScanner(_library, new FileSystemClipFiles(), new NotRecording(), new LibrarySettings(),
            _data.Clock, _data.Log);
        _tags = new SqliteTagStore(_data.Connections);
        _store = new SqliteFileOperationStore(_data.Connections);
    }

    public void Dispose() => _data.Dispose();

    private FileOperations Operations(IFileSystem? files = null) =>
        new(_store, files ?? new WindowsFileSystem(), _tags, _data.Backups, _scanner, _data.Clock, _data.Log);

    private async Task<ClipId> AddClip(string name, DateTime? recorded = null)
    {
        var path = Path.Combine(_clips, name);
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        File.SetLastWriteTime(path, recorded ?? Recorded);
        var folder = _library.Folders().SingleOrDefault() ?? _library.AddFolder(_clips, includeSubfolders: false, Recorded);
        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.ReviewAll));
        using var connection = _data.Connections.Open();
        return new ClipId(connection.ExecuteScalar<long>("SELECT Id FROM Clips WHERE RelativePath = @name;", new { name }));
    }

    private void Tag(ClipId clip, params TagId[] tags)
    {
        using var connection = _data.Connections.Open();
        connection.Execute("INSERT INTO ClipTags (ClipId, TagId) VALUES (@clip, @tag);",
            tags.Select(t => new { clip = clip.Value, tag = t.Value }));
    }

    private (string Path, string State, string Original) Clip(ClipId clip)
    {
        using var connection = _data.Connections.Open();
        return connection.QuerySingle<(string, string, string)>(
            "SELECT RelativePath, FileState, OriginalFileName FROM Clips WHERE Id = @id;", new { id = clip.Value });
    }

    private List<(string Status, string? Error)> Log()
    {
        using var connection = _data.Connections.Open();
        return connection.Query<(string, string?)>("SELECT Status, Error FROM FileOperations ORDER BY Id;").ToList();
    }

    [Fact]
    public async Task Renames_by_tags_in_manager_order_then_restores_the_original_name()
    {
        var clip = await AddClip("2026-10-11 22-15-36.mp4");
        var moments = _tags.AddBox(null, "Moments", BoxRule.PickMany);
        var game = _tags.AddBox(null, "Game", BoxRule.PickOne);
        _tags.MoveBox(game, -1);
        var funny = _tags.AddTag(moments, "Funny");
        var cs = _tags.AddTag(game, "Counter-Strike 2");
        Tag(clip, funny, cs);
        var operations = Operations();

        var preview = await operations.PlanRenameAsync([clip]);
        var outcomes = await operations.RenameAsync(preview);

        Assert.Equal("CounterStrike2_Funny_2026-10-11_22-15-36.mp4", Assert.Single(preview.ToRename).NewName);
        Assert.True(Assert.Single(outcomes).Succeeded);
        Assert.True(File.Exists(Path.Combine(_clips, "CounterStrike2_Funny_2026-10-11_22-15-36.mp4")));
        Assert.Equal(("CounterStrike2_Funny_2026-10-11_22-15-36.mp4", "Present", "2026-10-11 22-15-36.mp4"), Clip(clip));

        await operations.RestoreNamesAsync([clip]);

        Assert.True(File.Exists(Path.Combine(_clips, "2026-10-11 22-15-36.mp4")));
        Assert.Equal("2026-10-11 22-15-36.mp4", Clip(clip).Path);
        Assert.All(Log(), l => Assert.Equal("Done", l.Status));
        Assert.Contains(_data.Backups.List(), b => b.Pool == Application.Data.BackupPool.Safety);
    }

    [Fact]
    public async Task Two_clips_with_the_same_tags_and_time_get_numbered_names()
    {
        var first = await AddClip("a.mp4");
        var second = await AddClip("b.mp4");

        var preview = await Operations().PlanRenameAsync([first, second]);

        Assert.Equal(["2026-10-11_22-15-36.mp4", "2026-10-11_22-15-36_2.mp4"], preview.Items.Select(i => i.NewName));
        Assert.True(preview.Items[1].Numbered);
    }

    [Fact]
    public async Task A_locked_file_is_reported_and_left_as_it_was()
    {
        var clip = await AddClip("a.mp4");
        var operations = Operations();
        var preview = await operations.PlanRenameAsync([clip]);

        IReadOnlyList<FileOutcome> outcomes;
        await using (File.Open(Path.Combine(_clips, "a.mp4"), FileMode.Open, FileAccess.Read, FileShare.None))
            outcomes = await operations.RenameAsync(preview);

        Assert.Equal(FileProblem.InUse, Assert.Single(outcomes).Problem);
        Assert.Equal("a.mp4", Clip(clip).Path);
        Assert.Equal("Failed", Assert.Single(Log()).Status);
    }

    [Fact]
    public async Task Deleting_marks_the_clip_deleted_and_keeps_its_record()
    {
        var gone = await AddClip("a.mp4");
        var locked = await AddClip("b.mp4");
        var files = new FakeFiles { Locked = { Path.Combine(_clips, "b.mp4") } };
        var operations = Operations(files);

        var plan = await operations.PlanDeleteAsync([gone, locked]);
        var outcomes = await operations.DeleteAsync(plan);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(6, plan.TotalBytes);
        Assert.Equal([null, FileProblem.InUse], outcomes.Select(o => o.Problem));
        Assert.Equal("Deleted", Clip(gone).State);
        Assert.Equal("Present", Clip(locked).State);
    }

    [Fact]
    public async Task An_operation_cut_short_by_a_crash_is_recorded_as_it_really_ended()
    {
        var deleted = await AddClip("a.mp4");
        var untouched = await AddClip("b.mp4");
        var from = Path.Combine(_clips, "a.mp4");
        _store.Plan(deleted, FileOperationKind.Delete, from, null, Recorded);
        _store.Plan(untouched, FileOperationKind.Rename, Path.Combine(_clips, "b.mp4"), Path.Combine(_clips, "c.mp4"), Recorded);
        File.Delete(from);

        await Operations().RecoverInterruptedAsync();

        Assert.Equal("Deleted", Clip(deleted).State);
        Assert.Equal("b.mp4", Clip(untouched).Path);
        Assert.Equal([("Done", null), ("Failed", "Interrupted")], Log());
    }

    private sealed class NotRecording : IRecorderCheck
    {
        public bool IsRecorderRunning() => false;
    }

    // Deletes without the Recycle Bin, so tests leave nothing in it.
    private sealed class FakeFiles : IFileSystem
    {
        private readonly WindowsFileSystem _real = new();

        public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path) => File.Exists(path);

        public long SizeOf(string path) => new FileInfo(path).Length;

        public FileProblem? Delete(string path, out string? detail)
        {
            detail = null;
            if (Locked.Contains(path))
                return FileProblem.InUse;
            File.Delete(path);
            return null;
        }

        public FileProblem? Rename(string from, string to, out string? detail) => _real.Rename(from, to, out detail);
    }
}
