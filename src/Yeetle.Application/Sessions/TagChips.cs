using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// The vocabulary with what the session screen needs on top: each tag's hotkey and how often it
// is used.
public sealed record TagCatalog(
    TagVocabulary Vocabulary, IReadOnlyDictionary<TagId, string> Hotkeys, IReadOnlyDictionary<TagId, int> Usage)
{
    public static TagCatalog Empty { get; } = new(TagVocabulary.Empty, new Dictionary<TagId, string>(), new Dictionary<TagId, int>());
}

public sealed record Chip(TagId Tag, string Name, string? Hotkey, bool IsSelected, bool IsCarriedOver);

// One box on the session screen: its path ("Counter-Strike 2 › Map"), its rules and its chips.
public sealed record ChipGroup(Box Box, IReadOnlyList<string> Path, IReadOnlyList<Chip> Chips, bool IsMissingRequired);

// Which chips the session screen shows (spec §6): tags with a hotkey, tags on the clip (carried
// over or not), the most used tags, and every tag of a box under a picked tag. The exact mix is
// tuned in beta (#25).
public static class TagChips
{
    public const int MostUsedCount = 8;

    public static IReadOnlyList<ChipGroup> For(TagCatalog catalog, IReadOnlySet<TagId> tags, IReadOnlySet<TagId> carriedOver)
    {
        var vocabulary = catalog.Vocabulary;
        var rules = new TagRules(vocabulary);
        var mostUsed = catalog.Usage.Where(u => u.Value > 0).OrderByDescending(u => u.Value)
            .Take(MostUsedCount).Select(u => u.Key).ToHashSet();
        var missing = rules.MissingRequired(tags).Select(b => b.Id).ToHashSet();
        var groups = new List<ChipGroup>();
        foreach (var box in rules.VisibleBoxes(tags))
        {
            // A box under a picked tag shows all its tags; a top-level box only the likely ones.
            var showAll = box.ParentTag is not null;
            var chips = vocabulary.TagsIn(box.Id)
                .Where(t => showAll || tags.Contains(t.Id) || catalog.Hotkeys.ContainsKey(t.Id) || mostUsed.Contains(t.Id))
                .Select(t => new Chip(t.Id, t.Name, catalog.Hotkeys.GetValueOrDefault(t.Id), tags.Contains(t.Id),
                    carriedOver.Contains(t.Id)))
                .ToList();
            var isMissing = missing.Contains(box.Id);
            if (chips.Count > 0 || isMissing)
                groups.Add(new ChipGroup(box, PathOf(vocabulary, box), chips, isMissing));
        }
        return groups;
    }

    private static IReadOnlyList<string> PathOf(TagVocabulary vocabulary, Box box)
    {
        var path = box.ParentTag is { } parent ? vocabulary.PathOf(parent).ToList() : [];
        if (box.IsNamed)
            path.Add(box.Name);
        return path;
    }
}
