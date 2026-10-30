using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Sessions;

// One clip as it stands in the session. Visited tells a skipped clip (seen, still New) from one
// not reached yet. CarriedOver holds the tags copied from the previous clip (sticky tags).
public sealed record ClipReview(
    ClipId Clip,
    Decision Decision,
    ImmutableHashSet<TagId> Tags,
    ImmutableHashSet<TagId> CarriedOver,
    bool Visited);

public enum KeepOutcome
{
    Kept,
    // A visible required box is empty: nothing changed, and pressing Keep again keeps anyway.
    RequiredBoxEmpty,
}

// A review run over New clips, oldest first (spec §5). Everything stays in memory until the
// session is committed; undo goes back one decision or tag change at a time.
public sealed class ReviewSession
{
    private readonly TagRules _rules;
    private readonly Stack<(ImmutableList<ClipReview> Clips, int Position)> _undo = new();
    private ImmutableList<ClipReview> _clips;
    private bool _requiredWarningShown;

    public ReviewSession(TagRules rules, IEnumerable<ClipId> clips)
    {
        _rules = rules;
        _clips = clips.Select(c => new ClipReview(c, Decision.New, [], [], Visited: false)).ToImmutableList();
        if (_clips.Count > 0)
            _clips = _clips.SetItem(0, _clips[0] with { Visited = true });
    }

    public IReadOnlyList<ClipReview> Clips => _clips;

    // Equals Clips.Count once the last clip has been passed, which ends the session.
    public int Position { get; private set; }

    public bool IsAtEnd => Position == _clips.Count;

    public ClipReview? Current => IsAtEnd ? null : _clips[Position];

    public bool CanUndo => _undo.Count > 0;

    public KeepOutcome Keep()
    {
        var current = CurrentOrThrow();
        if (!_requiredWarningShown && _rules.MissingRequired(current.Tags).Any())
        {
            _requiredWarningShown = true;
            return KeepOutcome.RequiredBoxEmpty;
        }
        Decide(Decision.Kept);
        return KeepOutcome.Kept;
    }

    public void Yeet() => Decide(Decision.Yeeted);

    // Moves on without deciding: the clip stays New.
    public void Skip()
    {
        CurrentOrThrow();
        MoveNext();
    }

    public void Back()
    {
        if (Position == 0)
            return;
        Position--;
        _requiredWarningShown = false;
    }

    public void SelectTag(TagId tag) => ChangeTags(_rules.Select(CurrentOrThrow().Tags, tag));

    public void DeselectTag(TagId tag) => ChangeTags(_rules.Deselect(CurrentOrThrow().Tags, tag));

    public bool Undo()
    {
        if (!_undo.TryPop(out var previous))
            return false;
        (_clips, Position) = previous;
        _requiredWarningShown = false;
        return true;
    }

    private void Decide(Decision decision)
    {
        var current = CurrentOrThrow();
        SaveUndo();
        _clips = _clips.SetItem(Position, current with { Decision = decision });
        MoveNext();
    }

    private void ChangeTags(ImmutableHashSet<TagId> tags)
    {
        var current = CurrentOrThrow();
        if (tags.SetEquals(current.Tags))
            return;
        SaveUndo();
        _clips = _clips.SetItem(Position, current with { Tags = tags, CarriedOver = current.CarriedOver.Intersect(tags) });
        _requiredWarningShown = false;
    }

    // The first time a clip is reached with no tags, it gets the tags of the clip just left.
    private void MoveNext()
    {
        var left = _clips[Position];
        Position++;
        _requiredWarningShown = false;
        if (IsAtEnd || _clips[Position].Visited)
            return;
        var next = _clips[Position];
        _clips = _clips.SetItem(Position, next.Tags.IsEmpty
            ? next with { Tags = left.Tags, CarriedOver = left.Tags, Visited = true }
            : next with { Visited = true });
    }

    private void SaveUndo() => _undo.Push((_clips, Position));

    private ClipReview CurrentOrThrow() =>
        Current ?? throw new InvalidOperationException("The session has passed its last clip.");
}
