using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Sessions;

// One clip opened from the Explorer (spec §8): its decision, tags and markers can be changed with
// the same rules as in a session, and undone step by step. There is no next clip and no
// carried-over tags.
public sealed class ClipEdit
{
    private readonly Stack<ClipReview> _undo = new();
    private TagRules _rules;

    public ClipEdit(TagRules rules, ClipReview clip)
    {
        _rules = rules;
        Current = clip with { CarriedOver = [] };
    }

    public ClipReview Current { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public void SetDecision(Decision decision) => Change(Current with { Decision = decision });

    public void SelectTag(TagId tag) => Change(Current with { Tags = _rules.Select(Current.Tags, tag) });

    public void DeselectTag(TagId tag) => Change(Current with { Tags = _rules.Deselect(Current.Tags, tag) });

    public Marker AddMarker(TimeSpan position, Func<int, string> label)
    {
        var (markers, added) = ClipMarkers.Add(Current.Markers, position, label);
        Change(Current with { Markers = markers });
        return added;
    }

    public void RenameMarker(int number, string label) =>
        Change(Current with { Markers = ClipMarkers.Rename(Current.Markers, number, label) });

    public void MoveMarker(int number, TimeSpan position) =>
        Change(Current with { Markers = ClipMarkers.Move(Current.Markers, number, position) });

    public void DeleteMarker(int number) => Change(Current with { Markers = ClipMarkers.Delete(Current.Markers, number) });

    // Rules of a changed vocabulary apply from now on; deleted tags leave the clip and its history.
    public void UseVocabulary(TagVocabulary vocabulary)
    {
        _rules = new TagRules(vocabulary);
        var known = vocabulary.InManagerOrder.ToHashSet();
        Current = Current with { Tags = Current.Tags.Intersect(known) };
        var history = _undo.Reverse().Select(c => c with { Tags = c.Tags.Intersect(known) }).ToList();
        _undo.Clear();
        foreach (var state in history)
            _undo.Push(state);
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var previous))
            return false;
        Current = previous;
        return true;
    }

    private void Change(ClipReview changed)
    {
        if (changed.Decision == Current.Decision && changed.Tags.SetEquals(Current.Tags)
                                                 && changed.Markers.SequenceEqual(Current.Markers))
            return;
        _undo.Push(Current);
        Current = changed;
    }
}
