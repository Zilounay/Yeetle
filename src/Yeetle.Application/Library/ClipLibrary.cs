using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Library;

// The Explorer's actions on clip records (spec §8). File operations go through FileOperations.
public sealed class ClipLibrary(IExplorerStore store, ITagStore tags, IBackups backups, ILog log)
{
    // Raised on the caller's thread after a change made here or in an opened clip.
    public event EventHandler? Changed;

    public Task<IReadOnlyList<LibraryClip>> LoadAsync() => Task.Run(store.LoadClips);

    public Task<TagCatalog> LoadCatalogAsync() => Task.Run(tags.LoadCatalog);

    public async Task SetDecisionAsync(IReadOnlyCollection<ClipId> clips, Decision decision)
    {
        await Task.Run(() => store.SetDecision(clips, decision));
        log.Info($"{clips.Count} clip(s) set to {decision}.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Asked with a confirmation first; a backup comes before (spec §8, §10).
    public async Task WipeAsync(IReadOnlyCollection<ClipId> clips)
    {
        await Task.Run(() =>
        {
            backups.Create(BackupPool.Safety);
            store.Wipe(clips);
        });
        log.Info($"{clips.Count} clip(s) wiped.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Null when the clip is gone from the library meanwhile.
    public async Task<ClipEditor?> OpenAsync(ClipId clip)
    {
        var (found, catalog) = await Task.Run(() => (store.LoadClip(clip), tags.LoadCatalog()));
        if (found is null)
            return null;
        var review = new ClipReview(found.Id, found.Decision, found.Tags, [], Visited: true) { Markers = found.Markers };
        var editor = new ClipEditor(found, new ClipEdit(new TagRules(catalog.Vocabulary), review), catalog, store, log);
        editor.Saved += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        return editor;
    }
}
