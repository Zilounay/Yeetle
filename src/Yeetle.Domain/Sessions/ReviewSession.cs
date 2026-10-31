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
    bool Visited)
{
    // In position order. Markers are never carried over to the next clip.
    public ImmutableList<Marker> Markers { get; init; } = [];
}

public enum KeepOutcome
{
    Kept,
    // A visible required box is empty: nothing changed, and pressing Keep again keeps anyway.
    RequiredBoxEmpty,
}

// A review run over New clips, oldest first (spec §5). Everything stays in memory until the
// session is committed; undo goes back one decision, tag or marker change at a time.
public sealed class ReviewSession
{
    private TagRules _rules;
    private readonly Stack<(ImmutableList<ClipReview> Clips, int Position)> _undo = new();
    private ImmutableList<ClipReview> _clips;
    private bool _requiredWarningShown;

    public ReviewSession(TagRules rules, IEnumerable<ClipId> clips)
        : this(rules, clips.Select(c => new ClipReview(c, Decision.New, [], [], Visited: false)), position: 0)
    {
    }

    // Starts from given states: clips that already carry tags, or a session restored from its
    // journal after a crash. The undo history starts empty.
    public ReviewSession(TagRules rules, IEnumerable<ClipReview> clips, int position)
    {
        _rules = rules;
        _clips = clips.ToImmutableList();
        Position = Math.Clamp(position, 0, _clips.Count);
        if (!IsAtEnd)
            _clips = _clips.SetItem(Position, _clips[Position] with { Visited = true });
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

    // Adds a marker to the current clip, numbered after its highest one; label gets that number.
    public Marker AddMarker(TimeSpan position, Func<int, string> label)
    {
        var (markers, added) = ClipMarkers.Add(CurrentOrThrow().Markers, position, label);
        ChangeMarkers(markers);
        return added;
    }

    public void RenameMarker(int number, string label) =>
        ChangeMarkers(ClipMarkers.Rename(CurrentOrThrow().Markers, number, label));

    public void MoveMarker(int number, TimeSpan position) =>
        ChangeMarkers(ClipMarkers.Move(CurrentOrThrow().Markers, number, position));

    public void DeleteMarker(int number) => ChangeMarkers(ClipMarkers.Delete(CurrentOrThrow().Markers, number));

    // The Tag Manager changed the vocabulary mid-session (spec §5): its rules apply from now on,
    // and deleted tags leave every clip, in the undo history too.
    public void UseVocabulary(TagVocabulary vocabulary)
    {
        _rules = new TagRules(vocabulary);
        var known = vocabulary.InManagerOrder.ToHashSet();
        _clips = WithKnownTags(_clips);
        var history = _undo.Reverse().Select(s => (WithKnownTags(s.Clips), s.Position)).ToList();
        _undo.Clear();
        foreach (var state in history)
            _undo.Push(state);

        ImmutableList<ClipReview> WithKnownTags(ImmutableList<ClipReview> clips) => clips.ConvertAll(c => c with
        {
            Tags = c.Tags.Intersect(known),
            CarriedOver = c.CarriedOver.Intersect(known),
        });
    }

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

    private void ChangeMarkers(ImmutableList<Marker> markers)
    {
        var current = CurrentOrThrow();
        if (markers.SequenceEqual(current.Markers))
            return;
        SaveUndo();
        _clips = _clips.SetItem(Position, current with { Markers = markers });
    }

    private void SaveUndo() => _undo.Push((_clips, Position));

    private ClipReview CurrentOrThrow() =>
        Current ?? throw new InvalidOperationException("The session has passed its last clip.");
}
