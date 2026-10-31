namespace Yeetle.Domain.Tagging;

public readonly record struct TagId(long Value);

public readonly record struct BoxId(long Value);

public enum BoxRule
{
    PickOne,
    PickMany,
}

// A box holds tags and carries the rules (spec §6). ParentTag is null for a top-level box. A box
// with an empty name is the invisible pick-many box of a tag that holds tags directly.
public sealed record Box(BoxId Id, string Name, BoxRule Rule, bool Required, TagId? ParentTag)
{
    public bool IsNamed => Name.Length > 0;
}

public sealed record Tag(TagId Id, string Name, BoxId Box);
