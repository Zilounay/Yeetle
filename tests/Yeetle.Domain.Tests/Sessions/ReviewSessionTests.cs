using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Sessions;

public class ReviewSessionTests
{
    private static ReviewSession Start(int clips = 3) =>
        new(new TagRules(Create()), Enumerable.Range(1, clips).Select(i => new ClipId(i)));

    // Dota 2 has no required box below it, so keeping goes through.
    private static void TagAndKeep(ReviewSession session)
    {
        session.SelectTag(Dota2);
        session.Keep();
    }

    [Fact]
    public void Keep_and_yeet_decide_and_move_on()
    {
        var session = Start();

        TagAndKeep(session);
        session.Yeet();

        Assert.Equal([Decision.Kept, Decision.Yeeted, Decision.New], session.Clips.Select(c => c.Decision));
        Assert.Equal(2, session.Position);
    }

    [Fact]
    public void Skip_leaves_the_clip_new_and_back_returns_to_it()
    {
        var session = Start();

        session.Skip();
        session.Back();

        Assert.Equal(Decision.New, session.Current!.Decision);
        Assert.True(session.Clips[1].Visited);
    }

    [Fact]
    public void A_decision_can_be_changed_by_going_back()
    {
        var session = Start();
        TagAndKeep(session);

        session.Back();
        session.Yeet();

        Assert.Equal(Decision.Yeeted, session.Clips[0].Decision);
    }

    [Fact]
    public void Passing_the_last_clip_ends_the_session()
    {
        var session = Start(clips: 1);

        session.Yeet();

        Assert.True(session.IsAtEnd);
        Assert.Null(session.Current);
        Assert.Throws<InvalidOperationException>(session.Yeet);
    }

    [Fact]
    public void Keep_with_an_empty_required_box_warns_once_then_keeps()
    {
        var session = Start();
        session.SelectTag(Cs2); // Map is required and empty

        Assert.Equal(KeepOutcome.RequiredBoxEmpty, session.Keep());
        Assert.Equal(0, session.Position);
        Assert.Equal(KeepOutcome.Kept, session.Keep());
        Assert.Equal(Decision.Kept, session.Clips[0].Decision);
    }

    [Fact]
    public void Changing_tags_after_the_warning_warns_again()
    {
        var session = Start();
        session.SelectTag(Cs2);
        session.Keep();

        session.SelectTag(M4);

        Assert.Equal(KeepOutcome.RequiredBoxEmpty, session.Keep());
    }

    [Fact]
    public void Tags_carry_over_to_an_untagged_next_clip()
    {
        var session = Start();
        session.SelectTag(Pudge);

        session.Keep();

        Assert.Equal([Dota2, Pudge], session.Current!.Tags.OrderBy(t => t.Value));
        Assert.Equal(session.Current.Tags, session.Current.CarriedOver);
    }

    [Fact]
    public void Removing_a_carried_over_tag_takes_one_step()
    {
        var session = Start();
        session.SelectTag(Pudge);
        session.Keep();

        session.DeselectTag(Pudge);

        Assert.Equal([Dota2], session.Current!.Tags);
        Assert.Equal([Dota2], session.Current.CarriedOver);
    }

    [Fact]
    public void Tags_do_not_carry_over_to_a_clip_already_seen()
    {
        var session = Start();
        session.Skip();
        session.Back();
        session.SelectTag(Dota2);

        session.Skip();

        Assert.Empty(session.Current!.Tags);
    }

    [Fact]
    public void Undo_goes_back_one_change_at_a_time()
    {
        var session = Start();
        session.SelectTag(Dota2);
        session.SelectTag(Pudge);
        session.Keep();

        Assert.True(session.Undo());
        Assert.Equal(0, session.Position);
        Assert.Equal(Decision.New, session.Current!.Decision);
        Assert.Contains(Pudge, session.Current.Tags);

        Assert.True(session.Undo());
        Assert.DoesNotContain(Pudge, session.Current!.Tags);

        Assert.True(session.Undo());
        Assert.Empty(session.Current!.Tags);
        Assert.False(session.Undo());
    }

    [Fact]
    public void A_restored_session_continues_where_it_stopped()
    {
        var before = Start();
        TagAndKeep(before);

        var restored = new ReviewSession(new TagRules(Create()), before.Clips, before.Position);

        Assert.Equal(before.Clips, restored.Clips);
        Assert.Equal(1, restored.Position);
        Assert.False(restored.CanUndo);
    }

    [Fact]
    public void Undoing_a_keep_also_undoes_the_carried_over_tags()
    {
        var session = Start();
        TagAndKeep(session);

        session.Undo();

        Assert.Empty(session.Clips[1].Tags);
        Assert.False(session.Clips[1].Visited);
    }
}
