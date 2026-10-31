using Yeetle.Application.Diagnostics;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Library;

// A clip opened from the Explorer in the player (spec §8): its decision, tags and markers are
// saved at once after every change, and undo goes back one change at a time. Saves are written
// one after another, in the order the changes happened.
public sealed class ClipEditor : IClipEditing
{
    private readonly ClipEdit _edit;
    private readonly IExplorerStore _store;
    private readonly ILog _log;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private Task _saveTail = Task.CompletedTask;

    internal ClipEditor(LibraryClip clip, ClipEdit edit, TagCatalog catalog, IExplorerStore store, ILog log)
    {
        Clip = clip;
        _edit = edit;
        Catalog = catalog;
        _store = store;
        _log = log;
    }

    // The clip as it was opened: path, date and duration.
    public LibraryClip Clip { get; }

    public TagCatalog Catalog { get; private set; }

    public ClipReview CurrentReview => _edit.Current;

    ClipReview? IClipEditing.CurrentReview => _edit.Current;

    public bool CanUndo => _edit.CanUndo;

    // Raised after every change, on the caller's thread.
    public event EventHandler? Changed;

    // Raised once a change is in the database.
    public event EventHandler? Saved;

    // Raised with the reason when a change could not be saved.
    public event EventHandler<string>? SaveFailed;

    public void SetDecision(Decision decision) => Do(() => _edit.SetDecision(decision));

    public void SelectTag(TagId tag) => Do(() => _edit.SelectTag(tag));

    public void DeselectTag(TagId tag) => Do(() => _edit.DeselectTag(tag));

    public Marker AddMarker(TimeSpan position, Func<int, string> label)
    {
        var marker = _edit.AddMarker(position, label);
        Record();
        return marker;
    }

    public void RenameMarker(int number, string label) => Do(() => _edit.RenameMarker(number, label));

    public void MoveMarker(int number, TimeSpan position) => Do(() => _edit.MoveMarker(number, position));

    public void DeleteMarker(int number) => Do(() => _edit.DeleteMarker(number));

    public void Undo()
    {
        if (_edit.Undo())
            Record();
    }

    // The Tag Manager changed the tags: new names and rules show at once.
    public void UseCatalog(TagCatalog catalog)
    {
        Catalog = catalog;
        Do(() => _edit.UseVocabulary(catalog.Vocabulary));
    }

    // Completes once every change made so far is saved.
    public Task FlushAsync() => _saveTail;

    private void Do(Action change)
    {
        change();
        Record();
    }

    private void Record()
    {
        var state = _edit.Current;
        _saveTail = SaveAsync();
        Changed?.Invoke(this, EventArgs.Empty);

        async Task SaveAsync()
        {
            await _saveGate.WaitAsync();
            try
            {
                await Task.Run(() => _store.SaveEdit(state.Clip, state.Decision, state.Tags, state.Markers));
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.Data.Common.DbException)
            {
                _log.Error($"Clip {state.Clip.Value}: the change could not be saved.", e);
                SaveFailed?.Invoke(this, e.Message);
            }
            finally
            {
                _saveGate.Release();
            }
        }
    }
}
