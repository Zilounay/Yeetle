using Dapper;
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
    public void An_empty_library_has_an_empty_vocabulary() =>
        Assert.Empty(new SqliteTagStore(_data.Connections).LoadCatalog().Vocabulary.InManagerOrder);
}
