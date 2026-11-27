using Dapper;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

// Adds a tags file to the Nodes tree (spec §6), remembering every path it brings in ImportedPaths.
public sealed class SqliteVocabularyImportStore(SqliteConnectionFactory connections) : IVocabularyImportStore
{
    public int AddMissing(VocabularyTemplate template)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var nodes = connection.Query<NodeRow>("SELECT Id, Kind, ParentId, Name, Retired FROM Nodes;", transaction: transaction)
            .Select(n => new VocabularyNode(n.Id, Enum.Parse<NodeKind>(n.Kind), n.ParentId, n.Name, n.Retired))
            .ToList();
        var paths = connection.Query<(string Path, long NodeId)>("SELECT Path, NodeId FROM ImportedPaths;", transaction: transaction)
            .ToDictionary(p => p.Path, p => p.NodeId);
        var plan = VocabularyMerge.Plan(template, nodes, paths);
        if (plan.IsEmpty)
            return 0;

        var created = new List<long>(plan.NewNodes.Count);
        foreach (var node in plan.NewNodes)
        {
            var parentId = node.Parent.PlannedIndex is { } index ? created[index] : node.Parent.ExistingId;
            var isBox = node.Kind == NodeKind.Box;
            // Last among its siblings, as a node added in the Tag Manager.
            created.Add(connection.ExecuteScalar<long>(
                """
                INSERT INTO Nodes (Kind, ParentId, Name, SortOrder, Rule, Required)
                VALUES (@kind, @parentId, @name,
                        (SELECT COALESCE(MAX(SortOrder) + 1, 0) FROM Nodes WHERE ParentId IS @parentId AND Kind = @kind),
                        @rule, @required)
                RETURNING Id;
                """,
                new
                {
                    kind = node.Kind.ToString(),
                    parentId,
                    name = node.Name,
                    rule = isBox ? node.Rule.ToString() : null,
                    required = isBox && node.Required,
                },
                transaction));
        }
        var remembered = plan.NewNodes.Select((n, i) => new { path = n.Path, nodeId = created[i] })
            .Concat(plan.MatchedPaths.Select(m => new { path = m.Key, nodeId = m.Value }));
        connection.Execute("INSERT OR REPLACE INTO ImportedPaths (Path, NodeId) VALUES (@path, @nodeId);", remembered, transaction);
        transaction.Commit();
        return plan.NewTagCount;
    }

    private sealed class NodeRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = string.Empty;
        public long? ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool Retired { get; init; }
    }
}
