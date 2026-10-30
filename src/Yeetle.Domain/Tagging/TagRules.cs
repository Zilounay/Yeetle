using System.Collections.Immutable;

namespace Yeetle.Domain.Tagging;

// How a clip's tags change when one is picked or removed (spec §6).
public sealed class TagRules(TagVocabulary vocabulary)
{
    // Picking a tag also picks its parents. In a pick-one box it replaces the tag already chosen
    // there, along with everything below that tag.
    public ImmutableHashSet<TagId> Select(ImmutableHashSet<TagId> current, TagId tag)
    {
        var result = current;
        foreach (var id in vocabulary.AncestorsOf(tag).Reverse().Append(tag))
        {
            if (result.Contains(id))
                continue;
            var box = vocabulary.BoxOf(id);
            if (box.Rule == BoxRule.PickOne)
            {
                foreach (var other in vocabulary.TagsIn(box.Id).Where(t => result.Contains(t.Id)))
                    result = Deselect(result, other.Id);
            }
            result = result.Add(id);
        }
        return result;
    }

    // Removing a tag removes everything below it.
    public ImmutableHashSet<TagId> Deselect(ImmutableHashSet<TagId> current, TagId tag) =>
        current.Except(vocabulary.DescendantsOf(tag).Prepend(tag));

    // Top-level boxes, plus the boxes under each picked tag, in Tag Manager order.
    public IEnumerable<Box> VisibleBoxes(IReadOnlySet<TagId> current) =>
        vocabulary.BoxesUnder(null)
            .Concat(vocabulary.InManagerOrder.Where(current.Contains).SelectMany(t => vocabulary.BoxesUnder(t)));

    // Required only counts while the box is visible, and only warns (spec §5).
    public IEnumerable<Box> MissingRequired(IReadOnlySet<TagId> current) =>
        VisibleBoxes(current).Where(b => b.Required && !vocabulary.TagsIn(b.Id).Any(t => current.Contains(t.Id)));
}
