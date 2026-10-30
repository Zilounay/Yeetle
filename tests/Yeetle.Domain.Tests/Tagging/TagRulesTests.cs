using System.Collections.Immutable;
using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Tagging;

public class TagRulesTests
{
    private readonly TagVocabulary _vocabulary = Create();
    private readonly TagRules _rules = new(Create());

    private ImmutableHashSet<TagId> Pick(params TagId[] tags) =>
        tags.Aggregate(ImmutableHashSet<TagId>.Empty, _rules.Select);

    [Fact]
    public void Picking_a_tag_also_picks_its_parents() =>
        Assert.Equal([Cs2, Dust2], Pick(Dust2).OrderBy(t => t.Value));

    [Fact]
    public void Removing_a_tag_removes_the_tags_below_it()
    {
        var tags = _rules.Deselect(Pick(Dust2, M4, Zil), Cs2);

        Assert.Equal([Zil], tags);
    }

    [Fact]
    public void A_pick_one_box_swaps_the_chosen_tag()
    {
        var tags = Pick(Dust2, Mirage);

        Assert.Contains(Mirage, tags);
        Assert.DoesNotContain(Dust2, tags);
    }

    [Fact]
    public void Swapping_also_drops_what_was_below_the_old_tag()
    {
        var tags = Pick(Dust2, M4, Dota2);

        Assert.Equal([Dota2], tags);
    }

    [Fact]
    public void A_pick_many_box_keeps_every_tag() =>
        Assert.Superset(new HashSet<TagId> { M4, Awp, Funny, Clutch }, Pick(M4, Awp, Funny, Clutch).ToHashSet());

    [Fact]
    public void Picking_a_tag_twice_changes_nothing() => Assert.Equal(Pick(Dust2), Pick(Dust2, Dust2));

    [Fact]
    public void Boxes_under_a_tag_only_show_when_it_is_picked()
    {
        Assert.DoesNotContain(_rules.VisibleBoxes(Pick()), b => b.Id == Map);
        Assert.Contains(_rules.VisibleBoxes(Pick(Cs2)), b => b.Id == Map);
        Assert.DoesNotContain(_rules.VisibleBoxes(Pick(Cs2)), b => b.Id == Hero);
    }

    [Fact]
    public void Required_boxes_report_only_while_visible()
    {
        Assert.Equal([Game], _rules.MissingRequired(Pick()).Select(b => b.Id));
        Assert.Equal([Map], _rules.MissingRequired(Pick(Cs2)).Select(b => b.Id));
        Assert.Empty(_rules.MissingRequired(Pick(Dust2)));
        Assert.Empty(_rules.MissingRequired(Pick(Dota2)));
    }

    [Fact]
    public void Tag_manager_order_lists_each_tag_before_the_tags_below_it() =>
        Assert.Equal(
            [Cs2, Dust2, Mirage, Ancient, M4, Awp, Dota2, Pudge, Zil, Funny, Unexpected, Clutch],
            _vocabulary.InManagerOrder);

    [Fact]
    public void A_tag_nested_inside_itself_is_rejected()
    {
        var a = new TagId(1);
        var b = new TagId(2);
        Assert.Throws<ArgumentException>(() => new TagVocabulary(
            [new Box(new BoxId(1), "A", BoxRule.PickMany, false, b), new Box(new BoxId(2), "B", BoxRule.PickMany, false, a)],
            [new Tag(a, "A", new BoxId(1)), new Tag(b, "B", new BoxId(2))]));
    }
}
