namespace Yeetle.Domain.Tagging;

// A vocabulary written out as a tree, as a tags file holds it (spec §6): boxes holding tags, tags
// holding boxes. A box with an empty name is a tag's invisible pick-many box.
public sealed record VocabularyTemplate(IReadOnlyList<TemplateBox> Boxes);

public sealed record TemplateBox(string Name, BoxRule Rule, bool Required, IReadOnlyList<TemplateTag> Tags)
{
    // The box of a tag that holds tags directly.
    public static TemplateBox Unnamed(IReadOnlyList<TemplateTag> tags) => new(string.Empty, BoxRule.PickMany, false, tags);
}

public sealed record TemplateTag(string Name, IReadOnlyList<TemplateBox> Boxes)
{
    public TemplateTag(string name) : this(name, [])
    {
    }
}
