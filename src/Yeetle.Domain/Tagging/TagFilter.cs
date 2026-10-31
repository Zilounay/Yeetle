namespace Yeetle.Domain.Tagging;

// The Explorer's tag filter (spec §6, §8): tags from the same box combine with OR, tags from
// different boxes with AND, and a tag also matches clips that carry a tag below it.
public sealed class TagFilter(TagVocabulary vocabulary, IEnumerable<TagId> tags)
{
    private readonly List<List<TagId>> _groups = tags.Distinct()
        .GroupBy(t => vocabulary.Tag(t).Box)
        .Select(g => g.ToList())
        .ToList();

    public bool IsEmpty => _groups.Count == 0;

    public bool Matches(IReadOnlySet<TagId> clipTags) =>
        _groups.All(group => group.Any(tag => MatchesTag(clipTags, tag)));

    private bool MatchesTag(IReadOnlySet<TagId> clipTags, TagId tag) =>
        clipTags.Contains(tag) || vocabulary.DescendantsOf(tag).Any(clipTags.Contains);
}
