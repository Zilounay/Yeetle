using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Tagging;

// Edits the vocabulary from the Tag Manager (spec §6). Changes apply at once and for good, also
// during a session (spec §5); Changed tells the session screen to load the tags again.
public sealed class TagManager(ITagStore store, ITagEditor editor, IBackups backups, ILog log)
{
    // Raised on the caller's thread after every change.
    public event EventHandler? Changed;

    public Task<TagCatalog> LoadAsync() => Task.Run(store.LoadCatalog);

    public Task<BoxId> AddBoxAsync(TagId? parent, string name, BoxRule rule) => EditAsync(() => editor.AddBox(parent, name, rule));

    public Task<TagId> AddTagAsync(BoxId box, string name) => EditAsync(() => editor.AddTag(box, name));

    public Task RenameBoxAsync(BoxId box, string name) => EditAsync(() => editor.RenameBox(box, name));

    public Task RenameTagAsync(TagId tag, string name) => EditAsync(() => editor.RenameTag(tag, name));

    public Task SetBoxRuleAsync(BoxId box, BoxRule rule, bool required) => EditAsync(() => editor.SetBoxRule(box, rule, required));

    public Task SetColourAsync(TagId tag, string? colour) => EditAsync(() => editor.SetColour(tag, colour));

    public Task<HotkeyChange> SetHotkeyAsync(TagId tag, string? hotkey) => EditAsync(() => editor.SetHotkey(tag, hotkey));

    public Task MoveBoxAsync(BoxId box, int step) => EditAsync(() => editor.MoveBox(box, step));

    public Task MoveTagAsync(TagId tag, int step) => EditAsync(() => editor.MoveTag(tag, step));

    public Task<int> CountClipsBelowTagAsync(TagId tag) => Task.Run(() => editor.CountClipsBelowTag(tag));

    public Task<int> CountClipsInBoxAsync(BoxId box) => Task.Run(() => editor.CountClipsInBox(box));

    // A backup comes first (spec §10), since the tag leaves the vocabulary for good.
    public Task DeleteTagAsync(TagId tag) => DeleteAsync(() => editor.DeleteTag(tag), $"Tag {tag.Value} deleted.");

    public Task DeleteBoxAsync(BoxId box) => DeleteAsync(() => editor.DeleteBox(box), $"Box {box.Value} deleted.");

    private async Task DeleteAsync(Action delete, string done)
    {
        await EditAsync(() =>
        {
            backups.Create(BackupPool.Safety);
            delete();
        });
        log.Info(done);
    }

    private Task EditAsync(Action edit) => EditAsync(() =>
    {
        edit();
        return true;
    });

    private async Task<T> EditAsync<T>(Func<T> edit)
    {
        var result = await Task.Run(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
