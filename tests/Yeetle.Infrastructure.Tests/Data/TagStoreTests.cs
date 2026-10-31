using Dapper;
using Yeetle.Application.Data;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Tagging;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Tests.Data;

public class TagStoreTests : IDisposable
{
    private readonly TestDataFolder _data = new();

    public TagStoreTests() => _data.Startup.Prepare();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Loads_the_tree_with_hotkeys_and_usage()
    {
        using (var connection = _data.Connections.Open())
        {
            connection.Execute(
                """
                INSERT INTO Nodes (Id, Kind, ParentId, Name, SortOrder, Rule, Required) VALUES (1, 'Box', NULL, 'Game', 0, 'PickOne', 1);
                INSERT INTO Nodes (Id, Kind, ParentId, Name, SortOrder, Hotkey) VALUES (10, 'Tag', 1, 'Dota 2', 1, 'Digit2');
                INSERT INTO Nodes (Id, Kind, ParentId, Name, SortOrder) VALUES (11, 'Tag', 1, 'Counter-Strike 2', 0);
                INSERT INTO Nodes (Id, Kind, ParentId, Name, SortOrder, Rule) VALUES (2, 'Box', 11, 'Map', 0, 'PickOne');
                INSERT INTO Nodes (Id, Kind, ParentId, Name, SortOrder) VALUES (20, 'Tag', 2, 'Dust2', 0);
                INSERT INTO SourceFolders (Id, Path, AddedAt) VALUES (1, 'D:\Clips', '2026-10-30');
                INSERT INTO Clips (Id, FolderId, RelativePath, OriginalFileName, SizeBytes, ModifiedAt, RecordedAt, AddedAt)
                VALUES (1, 1, 'a.mp4', 'a.mp4', 1, '2026-10-30', '2026-10-30', '2026-10-30');
                INSERT INTO ClipTags (ClipId, TagId) VALUES (1, 11), (1, 20);
                """);
        }

        var catalog = new SqliteTagStore(_data.Connections).LoadCatalog();

        Assert.Equal([new TagId(11), new TagId(20), new TagId(10)], catalog.Vocabulary.InManagerOrder);
        Assert.Equal(BoxRule.PickOne, catalog.Vocabulary.Box(new BoxId(1)).Rule);
        Assert.True(catalog.Vocabulary.Box(new BoxId(1)).Required);
        Assert.Equal("Digit2", catalog.Hotkeys[new TagId(10)]);
        Assert.Equal(1, catalog.Usage[new TagId(20)]);
    }

    [Fact]
    public void Creates_renames_and_orders_boxes_and_tags()
    {
        var store = new SqliteTagStore(_data.Connections);
        var game = store.AddBox(null, "Game", BoxRule.PickOne);
        var dota = store.AddTag(game, "Dota");
        var cs = store.AddTag(game, "CS2");
        var hero = store.AddBox(dota, "Hero", BoxRule.PickMany);
        var pudge = store.AddTag(hero, "Pudge");

        store.RenameTag(dota, "Dota 2");
        store.MoveTag(cs, -1);
        store.SetBoxRule(hero, BoxRule.PickOne, required: true);
        store.SetColour(pudge, "#E5534B");
        var catalog = store.LoadCatalog();

        Assert.Equal([cs, dota, pudge], catalog.Vocabulary.InManagerOrder);
        Assert.Equal("Dota 2", catalog.Vocabulary.Tag(dota).Name);
        Assert.Equal(new Box(hero, "Hero", BoxRule.PickOne, true, dota), catalog.Vocabulary.Box(hero));
        Assert.Equal("#E5534B", catalog.Colours[pudge]);
    }

    [Fact]
    public void A_hotkey_belongs_to_one_tag_only()
    {
        var store = new SqliteTagStore(_data.Connections);
        var box = store.AddBox(null, "Moments", BoxRule.PickMany);
        var funny = store.AddTag(box, "Funny");
        var fail = store.AddTag(box, "Fail");

        Assert.Equal(HotkeyChange.Set, store.SetHotkey(funny, "Digit1"));
        Assert.Equal(HotkeyChange.Taken, store.SetHotkey(fail, "Digit1"));
        store.SetHotkey(funny, null);
        Assert.Equal(HotkeyChange.Set, store.SetHotkey(fail, "Digit1"));
    }

    [Fact]
    public async Task Deleting_a_tag_retires_it_and_what_is_below_it_after_a_backup()
    {
        var store = new SqliteTagStore(_data.Connections);
        var game = store.AddBox(null, "Game", BoxRule.PickOne);
        var cs = store.AddTag(game, "CS2");
        store.SetHotkey(cs, "Digit1");
        var dota = store.AddTag(game, "Dota 2");
        var map = store.AddBox(cs, "Map", BoxRule.PickOne);
        var dust = store.AddTag(map, "Dust2");
        using (var connection = _data.Connections.Open())
        {
            connection.Execute(
                """
                INSERT INTO SourceFolders (Id, Path, AddedAt) VALUES (1, 'D:Clips', '2026-10-30');
                INSERT INTO Clips (Id, FolderId, RelativePath, OriginalFileName, SizeBytes, ModifiedAt, RecordedAt, AddedAt)
                VALUES (1, 1, 'a.mp4', 'a.mp4', 1, '2026-10-30', '2026-10-30', '2026-10-30'),
                       (2, 1, 'b.mp4', 'b.mp4', 1, '2026-10-30', '2026-10-30', '2026-10-30');
                """);
            connection.Execute("INSERT INTO ClipTags (ClipId, TagId) VALUES (1, @cs), (1, @dust), (2, @dota);",
                new { cs = cs.Value, dust = dust.Value, dota = dota.Value });
        }
        var manager = new TagManager(store, store, _data.Backups, _data.Log);
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        Assert.Equal(1, await manager.CountClipsBelowTagAsync(cs));
        await manager.DeleteTagAsync(cs);

        var catalog = store.LoadCatalog();
        // Gone from the Tag Manager and its hotkey free, but still on the clips under its old name.
        Assert.Equal([dota], catalog.Vocabulary.InManagerOrder);
        Assert.Empty(catalog.Hotkeys);
        Assert.Equal(3, catalog.Usage.Values.Sum());
        Assert.Equal(["CS2", "Dust2"], catalog.Vocabulary.NamesOf(new HashSet<TagId> { cs, dust }));
        Assert.Equal(HotkeyChange.Set, store.SetHotkey(dota, "Digit1"));
        // A new tag never gets a retired tag's id.
        Assert.True(store.AddTag(game, "CS2").Value > dust.Value);
        Assert.Contains(_data.Backups.List(), b => b.Pool == BackupPool.Safety);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Tags_know_when_they_were_last_used()
    {
        var store = new SqliteTagStore(_data.Connections);
        var box = store.AddBox(null, "Moments", BoxRule.PickMany);
        var funny = store.AddTag(box, "Funny");
        using (var connection = _data.Connections.Open())
        {
            connection.Execute(
                """
                INSERT INTO SourceFolders (Id, Path, AddedAt) VALUES (1, 'D:Clips', '2026-10-30');
                INSERT INTO Clips (Id, FolderId, RelativePath, OriginalFileName, SizeBytes, ModifiedAt, RecordedAt, AddedAt)
                VALUES (1, 1, 'a.mp4', 'a.mp4', 1, '2026-10-30', '2026-10-20 21:00:00.000', '2026-10-30'),
                       (2, 1, 'b.mp4', 'b.mp4', 1, '2026-10-30', '2026-10-28 22:15:36.000', '2026-10-30');
                """);
            connection.Execute("INSERT INTO ClipTags (ClipId, TagId) VALUES (1, @funny), (2, @funny);", new { funny = funny.Value });
        }

        Assert.Equal(new DateTime(2026, 10, 28, 22, 15, 36), store.LoadCatalog().LastUsed[funny]);
    }

    [Fact]
    public void An_empty_library_has_an_empty_vocabulary() =>
        Assert.Empty(new SqliteTagStore(_data.Connections).LoadCatalog().Vocabulary.InManagerOrder);
}
