using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// The vocabulary with what the screens need on top: each tag's hotkey and colour, how many clips
// carry it, and when it was last used.
public sealed record TagCatalog(
    TagVocabulary Vocabulary, IReadOnlyDictionary<TagId, string> Hotkeys, IReadOnlyDictionary<TagId, int> Usage)
{
    public static TagCatalog Empty { get; } = new(TagVocabulary.Empty, new Dictionary<TagId, string>(), new Dictionary<TagId, int>());

    // Colours such as "#E5534B"; tags without one are left out.
    public IReadOnlyDictionary<TagId, string> Colours { get; init; } = new Dictionary<TagId, string>();

    // The recording date of the latest clip carrying each tag; unused tags are left out.
    public IReadOnlyDictionary<TagId, DateTime> LastUsed { get; init; } = new Dictionary<TagId, DateTime>();
}

public sealed record Chip(TagId Tag, string Name, string? Hotkey, bool IsSelected, bool IsCarriedOver);

// One box on the session screen: its path ("Counter-Strike 2 › Map"), its rules and its chips.
public sealed record ChipGroup(Box Box, IReadOnlyList<string> Path, IReadOnlyList<Chip> Chips, bool IsMissingRequired);

// Which chips the session screen shows (spec §6, decided in #62): every tag of every visible box,
// so any tag is one click away. Within a box, the tags used on the latest clips come first, then
// the most used ones, then the newest. Tags carried over from the previous clip are selected.
public static class TagChips
{
    public static IReadOnlyList<ChipGroup> For(TagCatalog catalog, IReadOnlySet<TagId> tags, IReadOnlySet<TagId> carriedOver)
    {
        var vocabulary = catalog.Vocabulary;
        var rules = new TagRules(vocabulary);
        var missing = rules.MissingRequired(tags).Select(b => b.Id).ToHashSet();
        var groups = new List<ChipGroup>();
        foreach (var box in rules.VisibleBoxes(tags))
        {
            var chips = vocabulary.TagsIn(box.Id)
                .OrderByDescending(t => catalog.LastUsed.TryGetValue(t.Id, out var used) ? used : DateTime.MinValue)
                .ThenByDescending(t => catalog.Usage.GetValueOrDefault(t.Id))
                // Ids grow with each new tag, so the highest is the newest.
                .ThenByDescending(t => t.Id.Value)
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
