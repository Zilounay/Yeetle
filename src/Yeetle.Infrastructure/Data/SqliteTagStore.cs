using Dapper;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Sessions;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

// Tags and boxes live in one Nodes tree (spec §10): a box under a tag or at the top, a tag in a box.
public sealed class SqliteTagStore(SqliteConnectionFactory connections) : ITagStore, ITagEditor
{
    private const string BoxKind = "Box";
    private const string TagKind = "Tag";

    // SQLite's code for a broken constraint, here the unique hotkey.
    private const int ConstraintError = 19;

    // The node and everything below it.
    private const string Below = """
        WITH RECURSIVE Below(Id) AS (
            SELECT @id
            UNION ALL
            SELECT n.Id FROM Nodes n JOIN Below b ON n.ParentId = b.Id)
        """;

    public TagCatalog LoadCatalog()
    {
        using var connection = connections.Open();
        var nodes = connection.Query<NodeRow>(
            "SELECT Id, Kind, ParentId, Name, Colour, Hotkey, Rule, Required FROM Nodes ORDER BY SortOrder, Id;").ToList();
        var boxes = nodes.Where(n => n.Kind == BoxKind).Select(n => new Box(new BoxId(n.Id), n.Name,
            Enum.TryParse<BoxRule>(n.Rule, out var rule) ? rule : BoxRule.PickMany, n.Required,
            n.ParentId is { } parent ? new TagId(parent) : null));
        var tags = nodes.Where(n => n.Kind != BoxKind).ToList();
        var vocabulary = new TagVocabulary(boxes, tags.Select(n => new Tag(new TagId(n.Id), n.Name, new BoxId(n.ParentId ?? 0))));
        var hotkeys = tags.Where(n => !string.IsNullOrWhiteSpace(n.Hotkey)).ToDictionary(n => new TagId(n.Id), n => n.Hotkey!);
        var colours = tags.Where(n => !string.IsNullOrWhiteSpace(n.Colour)).ToDictionary(n => new TagId(n.Id), n => n.Colour!);
        var usage = connection.Query<(long TagId, int Count)>("SELECT TagId, COUNT(*) FROM ClipTags GROUP BY TagId;")
            .ToDictionary(u => new TagId(u.TagId), u => u.Count);
        return new TagCatalog(vocabulary, hotkeys, usage) { Colours = colours };
    }

    public BoxId AddBox(TagId? parent, string name, BoxRule rule) =>
        new(Add(BoxKind, parent?.Value, name, rule.ToString()));

    public TagId AddTag(BoxId box, string name) => new(Add(TagKind, box.Value, name, rule: null));

    public void RenameBox(BoxId box, string name) => Rename(box.Value, name);

    public void RenameTag(TagId tag, string name) => Rename(tag.Value, name);

    public void SetBoxRule(BoxId box, BoxRule rule, bool required)
    {
        using var connection = connections.Open();
        connection.Execute("UPDATE Nodes SET Rule = @rule, Required = @required WHERE Id = @id AND Kind = 'Box';",
            new { id = box.Value, rule = rule.ToString(), required });
    }

    public void SetColour(TagId tag, string? colour)
    {
        using var connection = connections.Open();
        connection.Execute("UPDATE Nodes SET Colour = @colour WHERE Id = @id;", new { id = tag.Value, colour });
    }

    public HotkeyChange SetHotkey(TagId tag, string? hotkey)
    {
        using var connection = connections.Open();
        try
        {
            connection.Execute("UPDATE Nodes SET Hotkey = @hotkey WHERE Id = @id;",
                new { id = tag.Value, hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey });
            return HotkeyChange.Set;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == ConstraintError)
        {
            return HotkeyChange.Taken;
        }
    }

    public void MoveBox(BoxId box, int step) => Move(box.Value, step);

    public void MoveTag(TagId tag, int step) => Move(tag.Value, step);

    public int CountClipsBelowTag(TagId tag) => CountClips(tag.Value);

    public int CountClipsInBox(BoxId box) => CountClips(box.Value);

    public void DeleteTag(TagId tag) => Delete(tag.Value);

    public void DeleteBox(BoxId box) => Delete(box.Value);

    // A new box or tag goes last among its siblings.
    private long Add(string kind, long? parentId, string name, string? rule)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Nodes (Kind, ParentId, Name, SortOrder, Rule)
            VALUES (@kind, @parentId, @name,
                    (SELECT COALESCE(MAX(SortOrder) + 1, 0) FROM Nodes WHERE ParentId IS @parentId AND Kind = @kind),
                    @rule)
            RETURNING Id;
            """,
            new { kind, parentId, name, rule });
    }

    private void Rename(long id, string name)
    {
        using var connection = connections.Open();
        connection.Execute("UPDATE Nodes SET Name = @name WHERE Id = @id;", new { id, name });
    }

    // Swaps places with the sibling step positions away, then numbers the siblings again.
    private void Move(long id, int step)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var siblings = connection.Query<long>(
            """
            SELECT n.Id FROM Nodes n JOIN Nodes me ON me.Id = @id
            WHERE n.ParentId IS me.ParentId AND n.Kind = me.Kind
            ORDER BY n.SortOrder, n.Id;
            """, new { id }, transaction).ToList();
        var from = siblings.IndexOf(id);
        var to = Math.Clamp(from + step, 0, siblings.Count - 1);
        if (from < 0 || from == to)
            return;
        siblings.RemoveAt(from);
        siblings.Insert(to, id);
        connection.Execute("UPDATE Nodes SET SortOrder = @order WHERE Id = @id;",
            siblings.Select((sibling, order) => new { id = sibling, order }), transaction);
        transaction.Commit();
    }

    private int CountClips(long id)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<int>(
            Below + " SELECT COUNT(DISTINCT ClipId) FROM ClipTags WHERE TagId IN (SELECT Id FROM Below);", new { id });
    }

    // Children first is not needed: SQLite checks the parent links when the statement ends.
    private void Delete(long id)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(Below + " DELETE FROM ClipTags WHERE TagId IN (SELECT Id FROM Below);", new { id }, transaction);
        connection.Execute(Below + " DELETE FROM Nodes WHERE Id IN (SELECT Id FROM Below);", new { id }, transaction);
        transaction.Commit();
    }

    private sealed class NodeRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = string.Empty;
        public long? ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Colour { get; init; }
        public string? Hotkey { get; init; }
        public string? Rule { get; init; }
        public bool Required { get; init; }
    }
}
