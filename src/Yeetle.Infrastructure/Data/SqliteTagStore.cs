using Dapper;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

// Tags and boxes live in one Nodes tree (spec §10): a box under a tag or at the top, a tag in a box.
public sealed class SqliteTagStore(SqliteConnectionFactory connections) : ITagStore
{
    private const string BoxKind = "Box";

    public TagVocabulary LoadVocabulary()
    {
        using var connection = connections.Open();
        var nodes = connection.Query<NodeRow>(
            "SELECT Id, Kind, ParentId, Name, Rule, Required FROM Nodes ORDER BY SortOrder, Id;").ToList();
        var boxes = nodes.Where(n => n.Kind == BoxKind).Select(n => new Box(new BoxId(n.Id), n.Name,
            Enum.TryParse<BoxRule>(n.Rule, out var rule) ? rule : BoxRule.PickMany, n.Required,
            n.ParentId is { } parent ? new TagId(parent) : null));
        var tags = nodes.Where(n => n.Kind != BoxKind).Select(n => new Tag(new TagId(n.Id), n.Name, new BoxId(n.ParentId ?? 0)));
        return new TagVocabulary(boxes, tags);
    }

    private sealed class NodeRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = string.Empty;
        public long? ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Rule { get; init; }
        public bool Required { get; init; }
    }
}
