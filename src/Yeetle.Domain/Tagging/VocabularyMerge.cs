namespace Yeetle.Domain.Tagging;

public enum NodeKind
{
    Box,
    Tag,
}

// A box or tag already in the library, retired ones included.
public sealed record VocabularyNode(long Id, NodeKind Kind, long? ParentId, string Name, bool Retired);

// Where a planned node goes: under a node already in the library, under one planned earlier in the
// same plan (its index in NewNodes), or at the top when both are null.
public readonly record struct PlannedParent(long? ExistingId, int? PlannedIndex)
{
    public static PlannedParent Top => default;

    public bool IsTop => ExistingId is null && PlannedIndex is null;
}

public sealed record PlannedNode(NodeKind Kind, PlannedParent Parent, string Name, BoxRule Rule, bool Required, string Path);

// NewNodes are in creation order, parents first. MatchedPaths are template paths found in the
// library for the first time, to remember along with the new nodes' paths.
public sealed record VocabularyMergePlan(IReadOnlyList<PlannedNode> NewNodes, IReadOnlyDictionary<string, long> MatchedPaths)
{
    public int NewTagCount => NewNodes.Count(n => n.Kind == NodeKind.Tag);

    public bool IsEmpty => NewNodes.Count == 0 && MatchedPaths.Count == 0;
}

// Adds a tags file to the library without ever taking anything away (spec §6): what the library
// already has is left as it is, and only what is missing is created.
//
// Every box and tag a file brings is remembered by its path (its names from the top). A path seen
// before is never created again, so a tag the user renamed or deleted in the Tag Manager stays
// renamed or deleted, whatever the file says. A path seen for the first time is matched by name
// (ignoring case, as search does) under the same parent, so a tag the user made by hand is reused.
// A retired match counts as deleted: nothing is added below it.
public static class VocabularyMerge
{
    // Joins the names of a path. A control character, so no real name contains it.
    public const char PathSeparator = '\u001F';

    public static string PathOf(IEnumerable<string> names) => string.Join(PathSeparator, names);

    public static VocabularyMergePlan Plan(VocabularyTemplate template, IReadOnlyCollection<VocabularyNode> nodes,
        IReadOnlyDictionary<string, long> knownPaths)
    {
        var planner = new Planner(nodes, knownPaths);
        planner.MergeBoxes(template.Boxes, PlannedParent.Top, []);
        return new VocabularyMergePlan(planner.NewNodes, planner.Matched);
    }

    private sealed class Planner(IReadOnlyCollection<VocabularyNode> nodes, IReadOnlyDictionary<string, long> knownPaths)
    {
        private readonly Dictionary<long, VocabularyNode> _byId = nodes.ToDictionary(n => n.Id);
        private readonly ILookup<long?, VocabularyNode> _children = nodes.ToLookup(n => n.ParentId);
        private readonly Dictionary<string, long> _known = new(knownPaths, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _planned = new(StringComparer.OrdinalIgnoreCase);

        public List<PlannedNode> NewNodes { get; } = [];

        public Dictionary<string, long> Matched { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void MergeBoxes(IEnumerable<TemplateBox> boxes, PlannedParent parent, IReadOnlyList<string> path)
        {
            foreach (var box in boxes)
            {
                var boxPath = Append(path, box.Name);
                if (Resolve(NodeKind.Box, parent, box.Name, box.Rule, box.Required, boxPath) is not { } boxNode)
                    continue;
                foreach (var tag in box.Tags)
                {
                    var tagPath = Append(boxPath, tag.Name);
                    if (Resolve(NodeKind.Tag, boxNode, tag.Name, BoxRule.PickMany, false, tagPath) is { } tagNode)
                        MergeBoxes(tag.Boxes, tagNode, tagPath);
                }
            }
        }

        // The node the path stands for, planned if missing; null when it was deleted, so nothing
        // goes below it.
        private PlannedParent? Resolve(NodeKind kind, PlannedParent parent, string name, BoxRule rule, bool required,
            IReadOnlyList<string> path)
        {
            var key = PathOf(path);
            if (_planned.TryGetValue(key, out var index))
                return new PlannedParent(null, index);
            if (_known.TryGetValue(key, out var id))
                return _byId.TryGetValue(id, out var known) && !known.Retired ? new PlannedParent(id, null) : null;
            // A parent planned in this run has nothing below it yet.
            if (parent.PlannedIndex is null)
            {
                var sameName = _children[parent.ExistingId]
                    .Where(n => n.Kind == kind && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n.Retired)
                    .FirstOrDefault();
                if (sameName is not null)
                {
                    _known[key] = sameName.Id;
                    Matched[key] = sameName.Id;
                    return sameName.Retired ? null : new PlannedParent(sameName.Id, null);
                }
            }
            NewNodes.Add(new PlannedNode(kind, parent, name, rule, required, key));
            _planned[key] = NewNodes.Count - 1;
            return new PlannedParent(null, NewNodes.Count - 1);
        }

        private static List<string> Append(IReadOnlyList<string> path, string name) => [.. path, name];
    }
}
