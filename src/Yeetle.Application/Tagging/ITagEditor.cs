using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Tagging;

public enum HotkeyChange
{
    Set,
    // Another tag already has this key.
    Taken,
}

// Changes to the vocabulary (spec §6). Blocking calls: TagManager runs them in the background.
public interface ITagEditor
{
    // A box with an empty name under a tag holds that tag's tags directly.
    BoxId AddBox(TagId? parent, string name, BoxRule rule);

    TagId AddTag(BoxId box, string name);

    void RenameBox(BoxId box, string name);

    void RenameTag(TagId tag, string name);

    void SetBoxRule(BoxId box, BoxRule rule, bool required);

    // A colour such as "#E5534B", or null for none.
    void SetColour(TagId tag, string? colour);

    // Null removes the key.
    HotkeyChange SetHotkey(TagId tag, string? hotkey);

    // Moves the box or tag among its siblings; the order is the one used in filenames.
    void MoveBox(BoxId box, int step);

    void MoveTag(TagId tag, int step);

    // Clips carrying the tag or anything below it.
    int CountClipsBelowTag(TagId tag);

    int CountClipsInBox(BoxId box);

    // Deletes it with everything below it from the vocabulary. They are retired, not removed:
    // clips that carry them keep them under their old names (spec §6).
    void DeleteTag(TagId tag);

    void DeleteBox(BoxId box);
}
