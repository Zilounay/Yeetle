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

    internal ActiveSession(long id, ReviewSession review, IEnumerable<SessionClip> clips, Action<SessionSnapshot> journal)
    {
        Id = id;
        _review = review;
        _journal = journal;
        _clips = clips.ToDictionary(c => c.Id);
    }

    public long Id { get; }

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
