using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Sessions;

public class ClipEditTests
{
    private static ClipEdit Open(Decision decision = Decision.Kept) =>
        new(new TagRules(Create()), new ClipReview(new ClipId(1), decision, [], [], Visited: true));

    [Fact]
    public void Changes_follow_the_tag_rules_and_undo_one_step_at_a_time()
    {
        var edit = Open();

        edit.SelectTag(Dust2);
        edit.SetDecision(Decision.Yeeted);
        edit.AddMarker(TimeSpan.FromSeconds(5), n => $"Marker {n}");

        Assert.Equal([Cs2, Dust2], edit.Current.Tags.OrderBy(t => t.Value));
        Assert.Equal(Decision.Yeeted, edit.Current.Decision);
        Assert.Single(edit.Current.Markers);
        Assert.True(edit.Undo());
        Assert.True(edit.Undo());
        Assert.Equal(Decision.Kept, edit.Current.Decision);
        Assert.True(edit.Undo());
        Assert.Empty(edit.Current.Tags);
        Assert.False(edit.Undo());
    }

    [Fact]
    public void A_tag_deleted_while_the_clip_is_open_stays_on_it_and_is_named_as_retired()
    {
        var edit = Open();
        edit.SelectTag(Zil);
        var withoutZil = new TagVocabulary(
            Create().BoxesUnder(null).Concat(Create().InManagerOrder.SelectMany(t => Create().BoxesUnder(t))),
            Create().InManagerOrder.Where(t => t != Zil).Select(t => Create().Tag(t)),
            [new RetiredTag(Zil, "ZiL")]);

        edit.UseVocabulary(withoutZil);
        edit.SelectTag(Funny);

        Assert.Equal(new HashSet<TagId> { Zil, Funny }, edit.Current.Tags);
        Assert.Equal(["Funny", "ZiL"], withoutZil.NamesOf(edit.Current.Tags));
    }

    [Fact]
    public void A_change_to_the_same_value_leaves_no_undo_step()
    {
        var edit = Open();

        edit.SetDecision(Decision.Kept);

        Assert.False(edit.CanUndo);
    }
}
