using Dapper;
using Yeetle.Application.Library;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Tests.Data;

namespace Yeetle.Infrastructure.Tests.Clips;

// The scanner against real folders and a real database (spec §16, scanning).
public class ScannerTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 10, 11, 22, 15, 36);

    private readonly TestDataFolder _data = new();
    private readonly string _clips;
    private readonly SqliteLibraryStore _store;
    private readonly FakeRecorder _recorder = new();
    private readonly LibraryScanner _scanner;
    private readonly SourceFolders _folders;

    public ScannerTests()
    {
        _data.Startup.Prepare();
        _clips = Path.Combine(_data.Paths.Root, "Clips");
        Directory.CreateDirectory(_clips);
        _store = new SqliteLibraryStore(_data.Connections);
        _scanner = new LibraryScanner(_store, new FileSystemClipFiles(), _recorder, new LibrarySettings(), _data.Clock, _data.Log);
        _folders = new SourceFolders(_store, _data.Clock);
        _data.Clock.Now = new DateTimeOffset(Recorded.AddDays(1), TimeSpan.Zero);
    }

    public void Dispose() => _data.Dispose();

    private string AddFile(string relativePath, DateTime? modified = null)
    {
        var path = Path.Combine(_clips, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTime(path, modified ?? Recorded);
        return path;
    }

    private async Task<SourceFolder> AddFolder(bool includeSubfolders = false, string? path = null)
    {
        var folder = (await _folders.AddAsync(path ?? _clips, includeSubfolders))!;
        // Most tests are not about the first-scan choice.
        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.ReviewAll));
        return folder;
    }

    private List<(string Path, string State, string Decision)> Clips()
    {
        using var connection = _data.Connections.Open();
        return connection.Query<(string, string, string)>(
            "SELECT RelativePath, FileState, Decision FROM Clips ORDER BY RelativePath;").ToList();
    }

    [Fact]
    public async Task New_files_are_added_as_new_clips_with_relative_paths()
    {
        await AddFolder();
        AddFile("a.mp4");
        AddFile("notes.txt");

        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Added);
        Assert.Equal([("a.mp4", "Present", "New")], Clips());
    }

    [Fact]
    public async Task Subfolders_are_only_scanned_when_asked()
    {
        AddFile("a.mp4");
        AddFile(Path.Combine("Dota", "b.mp4"));

        await AddFolder();
        Assert.Single(Clips());

        var folder = (await _folders.ListAsync()).Single();
        await _folders.SetIncludeSubfoldersAsync(folder, true);
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a.mp4", Path.Combine("Dota", "b.mp4")], Clips().Select(c => c.Path));
    }

    [Fact]
    public async Task A_deleted_file_becomes_missing_and_comes_back_when_restored()
    {
        var path = AddFile("a.mp4");
        await AddFolder();

        File.Move(path, path + ".bak");
        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, report.Missing);
        Assert.Equal("Missing", Clips().Single().State);

        File.Move(path + ".bak", path);
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Present", Clips().Single().State);
    }

    [Fact]
    public async Task An_unreachable_folder_is_offline_and_its_clips_untouched()
    {
        AddFile("a.mp4");
        await AddFolder();
        Directory.Move(_clips, _clips + "-unplugged");

        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Single(report.Offline);
        Assert.True((await _folders.ListAsync()).Single().IsOffline);
        Assert.Equal("Present", Clips().Single().State);
    }

    [Fact]
    public async Task A_changed_file_at_the_same_path_stays_the_same_clip()
    {
        var path = AddFile("a.mp4");
        await AddFolder();

        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        File.SetLastWriteTime(path, Recorded.AddMinutes(1));
        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a.mp4"], report.Changed);
        Assert.Single(Clips());
    }

    [Fact]
    public async Task A_file_still_being_written_waits_for_a_later_scan()
    {
        await AddFolder();
        AddFile("live.mp4", _data.Clock.Now.DateTime.AddSeconds(-2));

        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, report.StillRecording);
        Assert.Empty(Clips());

        _data.Clock.Now = _data.Clock.Now.Add(LibraryScanner.StillWritingWindow);
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Single(Clips());
    }

    [Fact]
    public async Task New_files_wait_while_obs_is_running()
    {
        await AddFolder();
        AddFile("a.mp4");
        _recorder.Running = true;

        Assert.Equal(1, (await _scanner.ScanAsync(TestContext.Current.CancellationToken)).StillRecording);
        Assert.Empty(Clips());
    }

    [Fact]
    public async Task The_same_filename_in_two_folders_is_two_clips()
    {
        var other = Path.Combine(_data.Paths.Root, "Other");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "a.mp4"), [1]);
        File.SetLastWriteTime(Path.Combine(other, "a.mp4"), Recorded);
        AddFile("a.mp4");

        await AddFolder();
        await AddFolder(path: other);

        Assert.Equal(2, Clips().Count);
    }

    [Fact]
    public async Task A_first_scan_with_clips_waits_for_the_users_choice()
    {
        AddFile("a.mp4");
        var folder = (await _folders.AddAsync(_clips, includeSubfolders: false))!;

        var report = await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, report.PendingFirstScans.Single().ClipCount);
        Assert.Empty(Clips());
        Assert.False((await _folders.ListAsync()).Single(f => f.Id == folder.Id).FirstScanDone);
    }

    [Fact]
    public async Task First_scan_can_keep_everything()
    {
        AddFile("a.mp4");
        var folder = (await _folders.AddAsync(_clips, includeSubfolders: false))!;

        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.KeepAll));

        Assert.Equal("Kept", Clips().Single().Decision);
        Assert.Empty((await _scanner.ScanAsync(TestContext.Current.CancellationToken)).PendingFirstScans);
    }

    [Fact]
    public async Task First_scan_can_review_only_recent_clips()
    {
        AddFile("old.mp4", Recorded.AddDays(-10));
        AddFile("recent.mp4", Recorded);
        var folder = (await _folders.AddAsync(_clips, includeSubfolders: false))!;

        await _scanner.CompleteFirstScanAsync(folder.Id, new FirstScanChoice(FirstScanKind.ReviewFrom, Recorded.Date));

        Assert.Equal([("old.mp4", "Present", "Kept"), ("recent.mp4", "Present", "New")], Clips());
    }

    [Fact]
    public async Task Removing_a_folder_with_clips_keeps_them()
    {
        AddFile("a.mp4");
        var folder = await AddFolder();

        await _folders.RemoveAsync(folder, FolderRemoval.StopScanning);
        File.Delete(Path.Combine(_clips, "a.mp4"));
        await _scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(FolderStatus.ScanStopped, (await _folders.ListAsync()).Single().Status);
        Assert.Equal("Present", Clips().Single().State); // no longer scanned
    }

    [Fact]
    public async Task Removing_an_empty_folder_deletes_it()
    {
        var folder = await AddFolder();

        await _folders.RemoveAsync(folder, FolderRemoval.HideClips);

        Assert.Empty(await _folders.ListAsync());
    }

    [Fact]
    public async Task A_folder_is_only_added_once()
    {
        await AddFolder();

        Assert.Null(await _folders.AddAsync(_clips + Path.DirectorySeparatorChar, includeSubfolders: true));
    }

    [Fact]
    public async Task Focus_scans_run_at_most_once_a_minute()
    {
        var scheduler = new ScanScheduler(_scanner, _data.Clock);

        Assert.NotNull(await scheduler.RequestAsync(ScanTrigger.Focus));
        Assert.Null(await scheduler.RequestAsync(ScanTrigger.Focus));
        Assert.NotNull(await scheduler.RequestAsync(ScanTrigger.Rescan));

        _data.Clock.Now = _data.Clock.Now.Add(ScanScheduler.FocusInterval);
        Assert.NotNull(await scheduler.RequestAsync(ScanTrigger.Focus));
    }

    private sealed class FakeRecorder : IRecorderCheck
    {
        public bool Running { get; set; }

        public bool IsRecorderRunning() => Running;
    }
}
