using Yeetle.Application.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Tests.Sessions;

public class TagChipsTests
{
    private static readonly BoxId Game = new(1);
    private static readonly BoxId Map = new(2);
    private static readonly BoxId Moments = new(3);
    private static readonly TagId Cs2 = new(10);
    private static readonly TagId Dota2 = new(11);
    private static readonly TagId Dust2 = new(20);
    private static readonly TagId Mirage = new(21);
    private static readonly TagId Funny = new(30);
    private static readonly TagId Fail = new(31);

    private static readonly TagVocabulary Vocabulary = new(
        [
            new Box(Game, "Game", BoxRule.PickOne, Required: true, ParentTag: null),
            new Box(Map, "Map", BoxRule.PickOne, Required: true, Cs2),
            new Box(Moments, "Moments", BoxRule.PickMany, Required: false, ParentTag: null),
        ],
        [
            new Tag(Cs2, "Counter-Strike 2", Game), new Tag(Dota2, "Dota 2", Game),
            new Tag(Dust2, "Dust2", Map), new Tag(Mirage, "Mirage", Map),
            new Tag(Funny, "Funny", Moments), new Tag(Fail, "Fail", Moments),
        ]);

    private static IReadOnlyList<ChipGroup> Chips(
        IReadOnlySet<TagId> tags, IReadOnlySet<TagId>? carried = null, Dictionary<TagId, string>? hotkeys = null,
        Dictionary<TagId, int>? usage = null) =>
        TagChips.For(new TagCatalog(Vocabulary, hotkeys ?? [], usage ?? []), tags, carried ?? new HashSet<TagId>());

    [Fact]
    public void Top_level_boxes_show_hotkeyed_and_most_used_tags_only()
    {
        var groups = Chips(new HashSet<TagId>(), hotkeys: new() { [Cs2] = "Digit1" }, usage: new() { [Funny] = 3 });

        Assert.Equal([Cs2], groups.Single(g => g.Box.Id == Game).Chips.Select(c => c.Tag));
        Assert.Equal("Digit1", groups.Single(g => g.Box.Id == Game).Chips.Single().Hotkey);
        Assert.Equal([Funny], groups.Single(g => g.Box.Id == Moments).Chips.Select(c => c.Tag));
    }

    [Fact]
    public void A_box_under_a_picked_tag_shows_all_its_tags_with_its_path()
    {
        var map = Chips(new HashSet<TagId> { Cs2 }).Single(g => g.Box.Id == Map);

        Assert.Equal([Dust2, Mirage], map.Chips.Select(c => c.Tag));
        Assert.Equal(["Counter-Strike 2", "Map"], map.Path);
        Assert.True(map.IsMissingRequired);
    }

    [Fact]
    public void Tags_on_the_clip_always_show_and_carried_ones_are_marked()
    {
        var groups = Chips(new HashSet<TagId> { Fail }, carried: new HashSet<TagId> { Fail });

        var fail = groups.Single(g => g.Box.Id == Moments).Chips.Single();
        Assert.True(fail.IsSelected);
        Assert.True(fail.IsCarriedOver);
    }

    [Fact]
    public void An_empty_required_box_shows_even_without_chips() =>
        Assert.True(Chips(new HashSet<TagId>()).Single(g => g.Box.Id == Game).IsMissingRequired);

    [Fact]
    public void An_optional_box_without_chips_is_left_out() =>
        Assert.DoesNotContain(Chips(new HashSet<TagId>()), g => g.Box.Id == Moments);
}
