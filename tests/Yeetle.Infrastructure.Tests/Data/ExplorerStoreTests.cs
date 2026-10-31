using Dapper;
using Yeetle.Application.Library;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Tests.Data;

// The Explorer's queries and edits against a real database (spec §8).
public class ExplorerStoreTests : IDisposable
{
    private readonly TestDataFolder _data = new();
    private readonly SqliteExplorerStore _store;
    private readonly TagId _funny;

    public ExplorerStoreTests()
    {
        _data.Startup.Prepare();
        _store = new SqliteExplorerStore(_data.Connections);
        var tags = new SqliteTagStore(_data.Connections);
        _funny = tags.AddTag(tags.AddBox(null, "Moments", BoxRule.PickMany), "Funny");
        using var connection = _data.Connections.Open();
        connection.Execute(
            """
            INSERT INTO SourceFolders (Id, Path, AddedAt) VALUES (1, 'D:\Clips', '2026-10-30');
            INSERT INTO SourceFolders (Id, Path, AddedAt, IsOffline) VALUES (2, 'E:\Usb', '2026-10-30', 1);
            INSERT INTO SourceFolders (Id, Path, AddedAt, Status) VALUES (3, 'F:\Old', '2026-10-30', 'Hidden');
            INSERT INTO Clips (Id, FolderId, RelativePath, OriginalFileName, SizeBytes, ModifiedAt, RecordedAt, AddedAt, Decision, DurationMs)
            VALUES (1, 1, 'a.mp4', 'a.mp4', 1, '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', 'Kept', 83000),
                   (2, 2, 'b.mp4', 'b.mp4', 1, '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', 'Kept', 0),
                   (3, 3, 'c.mp4', 'c.mp4', 1, '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', '2026-10-30 20:00:00.000', 'Kept', NULL);
            """);
        connection.Execute("INSERT INTO ClipTags (ClipId, TagId) VALUES (1, @tag);", new { tag = _funny.Value });
        connection.Execute("INSERT INTO Markers (ClipId, PositionMs, Label) VALUES (1, 5000, 'Clutch');");
    }

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Lists_clips_with_tags_markers_and_offline_folders_but_not_hidden_ones()
    {
        var clips = _store.LoadClips().OrderBy(c => c.Id.Value).ToList();

        Assert.Equal([1L, 2L], clips.Select(c => c.Id.Value));
        Assert.Equal(TimeSpan.FromSeconds(83), clips[0].Duration);
        Assert.Equal([_funny], clips[0].Tags);
        Assert.Equal("Clutch", Assert.Single(clips[0].Markers).Label);
        Assert.Equal(FileState.Offline, clips[1].State);
        Assert.Null(clips[1].Duration); // unreadable
    }

    [Fact]
    public void Wipe_removes_tags_markers_and_decision()
    {
        _store.Wipe([new ClipId(1)]);

        var clip = _store.LoadClip(new ClipId(1))!;
        Assert.Equal(Decision.New, clip.Decision);
        Assert.Empty(clip.Tags);
        Assert.Empty(clip.Markers);
    }

    [Fact]
    public void An_edit_is_saved_whole()
    {
        _store.SaveEdit(new ClipId(2), Decision.Yeeted, new HashSet<TagId> { _funny },
            [new Marker(1, TimeSpan.FromSeconds(3), "Fail")]);
        _store.SetDecision([new ClipId(1)], Decision.Yeeted);

        var edited = _store.LoadClip(new ClipId(2))!;
        Assert.Equal(Decision.Yeeted, edited.Decision);
        Assert.Equal([_funny], edited.Tags);
        Assert.Equal("Fail", Assert.Single(edited.Markers).Label);
        Assert.Equal(Decision.Yeeted, _store.LoadClip(new ClipId(1))!.Decision);
    }

    [Fact]
    public async Task Wiping_takes_a_backup_first()
    {
        var library = new ClipLibrary(_store, new SqliteTagStore(_data.Connections), _data.Backups, _data.Log);

        await library.WipeAsync([new ClipId(1)]);

        Assert.Contains(_data.Backups.List(), b => b.Pool == Application.Data.BackupPool.Safety);
    }
}
