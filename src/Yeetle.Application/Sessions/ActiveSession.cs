using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// A running review session: the domain rules, the clips to play, and a journal entry after
// every action so a crash loses nothing (spec §5).
public sealed class ActiveSession
{
    private readonly ReviewSession _review;
    private readonly Action<SessionSnapshot> _journal;
    private readonly Dictionary<ClipId, SessionClip> _clips;

    internal ActiveSession(long id, ReviewSession review, IEnumerable<SessionClip> clips, TagCatalog catalog,
        Action<SessionSnapshot> journal)
    {
        Id = id;
        _review = review;
        _journal = journal;
        _clips = clips.ToDictionary(c => c.Id);
        Catalog = catalog;
    }

    public long Id { get; }

    // The tags as the Tag Manager last left them.
    public TagCatalog Catalog { get; private set; }

    public IReadOnlyList<ClipReview> Reviews => _review.Clips;

    public int Position => _review.Position;

    public bool IsAtEnd => _review.IsAtEnd;

    public bool CanUndo => _review.CanUndo;

    public SessionClip? Current => _review.Current is { } current ? _clips[current.Clip] : null;

    public ClipReview? CurrentReview => _review.Current;

    public SessionClip Clip(ClipId id) => _clips[id];

    // Raised after every change, on the caller's thread.
    public event EventHandler? Changed;

    public KeepOutcome Keep()
    {
        var outcome = _review.Keep();
        if (outcome == KeepOutcome.Kept)
            Record();
        return outcome;
    }

    public void Yeet() => Do(_review.Yeet);

    public void Skip() => Do(_review.Skip);

    public void Back() => Do(_review.Back);

    public void SelectTag(TagId tag) => Do(() => _review.SelectTag(tag));

    public void DeselectTag(TagId tag) => Do(() => _review.DeselectTag(tag));

    public Marker AddMarker(TimeSpan position, Func<int, string> label)
    {
        var marker = _review.AddMarker(position, label);
        Record();
        return marker;
    }

    public void RenameMarker(int number, string label) => Do(() => _review.RenameMarker(number, label));

    public void MoveMarker(int number, TimeSpan position) => Do(() => _review.MoveMarker(number, position));

    public void DeleteMarker(int number) => Do(() => _review.DeleteMarker(number));

    // The Tag Manager changed the tags (spec §5): new names and rules show at once, and deleted
    // tags leave the session's clips.
    public void UseCatalog(TagCatalog catalog)
    {
        Catalog = catalog;
        Do(() => _review.UseVocabulary(catalog.Vocabulary));
    }

    public bool Undo()
    {
        if (!_review.Undo())
            return false;
        Record();
        return true;
    }

    public SessionSnapshot Snapshot() => new(_review.Position, _review.Clips);

    private void Do(Action action)
    {
        action();
        Record();
    }

    private void Record()
    {
        _journal(Snapshot());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
