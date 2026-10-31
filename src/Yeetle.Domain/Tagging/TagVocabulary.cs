namespace Yeetle.Domain.Tagging;

// Every box and tag, in Tag Manager order: boxes and tags are given in the order they appear
// within their parent, and that order is the one used in renamed filenames (spec §6).
public sealed class TagVocabulary
{
    private readonly Dictionary<TagId, Tag> _tags;
    private readonly Dictionary<BoxId, Box> _boxes;
    private readonly ILookup<TagId?, Box> _boxesUnder;
    private readonly ILookup<BoxId, Tag> _tagsIn;

    public TagVocabulary(IEnumerable<Box> boxes, IEnumerable<Tag> tags)
    {
        var boxList = boxes.ToList();
        var tagList = tags.ToList();
        _boxes = boxList.ToDictionary(b => b.Id);
        _tags = tagList.ToDictionary(t => t.Id);
        _boxesUnder = boxList.ToLookup(b => b.ParentTag);
        _tagsIn = tagList.ToLookup(t => t.Box);

        foreach (var tag in tagList.Where(t => !_boxes.ContainsKey(t.Box)))
            throw new ArgumentException($"Tag {tag.Id} is in unknown box {tag.Box}.", nameof(tags));
        foreach (var box in boxList.Where(b => b.ParentTag is { } parent && !_tags.ContainsKey(parent)))
            throw new ArgumentException($"Box {box.Id} is under unknown tag {box.ParentTag}.", nameof(boxes));
        InManagerOrder = Below(parent: null).ToList();
        // A tag nested inside itself can never be reached from the top level.
        if (InManagerOrder.Count != tagList.Count)
            throw new ArgumentException("Some tags are nested inside themselves.", nameof(boxes));
    }

    public static TagVocabulary Empty { get; } = new([], []);

    // Depth first: a tag, then the boxes under it, before the next tag of its box.
    public IReadOnlyList<TagId> InManagerOrder { get; }

    public Tag Tag(TagId id) => _tags[id];

    public Box Box(BoxId id) => _boxes[id];

    public Box BoxOf(TagId tag) => _boxes[_tags[tag].Box];

    public IEnumerable<Box> BoxesUnder(TagId? parent) => _boxesUnder[parent];

    public IEnumerable<Tag> TagsIn(BoxId box) => _tagsIn[box];

    public TagId? ParentOf(TagId tag) => BoxOf(tag).ParentTag;

    // Nearest first.
    public IEnumerable<TagId> AncestorsOf(TagId tag)
    {
        for (var parent = ParentOf(tag); parent is { } id; parent = ParentOf(id))
            yield return id;
    }

    public IEnumerable<TagId> DescendantsOf(TagId tag) => Below(tag);

    // How a tag is shown in suggestions: its parent tags and the named boxes between them, without
    // the top-level box, e.g. ["Dota 2", "Hero", "Crystal Maiden"] (spec §6).
    public IReadOnlyList<string> PathOf(TagId tag)
    {
        var path = new List<string> { Tag(tag).Name };
        for (var box = BoxOf(tag); box.ParentTag is { } parent; box = BoxOf(parent))
        {
            if (box.IsNamed)
                path.Add(box.Name);
            path.Add(Tag(parent).Name);
        }
        path.Reverse();
        return path;
    }

    // Depth first, in Tag Manager order.
    private IEnumerable<TagId> Below(TagId? parent) =>
        BoxesUnder(parent).SelectMany(b => TagsIn(b.Id)).SelectMany(t => Below(t.Id).Prepend(t.Id));
}
