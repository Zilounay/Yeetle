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
        Dictionary<TagId, int>? usage = null, Dictionary<TagId, DateTime>? lastUsed = null,
        TagVocabulary? vocabulary = null) =>
        TagChips.For(new TagCatalog(vocabulary ?? Vocabulary, hotkeys ?? [], usage ?? []) { LastUsed = lastUsed ?? [] },
            tags, carried ?? new HashSet<TagId>());

    [Fact]
    public void Every_tag_of_a_top_level_box_shows_even_unused_ones()
    {
        var groups = Chips(new HashSet<TagId>(), hotkeys: new() { [Cs2] = "Digit1" });

        Assert.Equal(2, groups.Single(g => g.Box.Id == Game).Chips.Count);
        Assert.Equal("Digit1", groups.Single(g => g.Box.Id == Game).Chips.Single(c => c.Tag == Cs2).Hotkey);
        Assert.Equal(2, groups.Single(g => g.Box.Id == Moments).Chips.Count);
    }

    [Fact]
    public void Chips_come_last_used_first_then_most_used_then_newest()
    {
        var clutch = new TagId(32);
        var ace = new TagId(33);
        var vocabulary = new TagVocabulary(
            [new Box(Moments, "Moments", BoxRule.PickMany, Required: false, ParentTag: null)],
            [new Tag(Funny, "Funny", Moments), new Tag(Fail, "Fail", Moments), new Tag(clutch, "Clutch", Moments),
                new Tag(ace, "Ace", Moments)]);
        var october = new DateTime(2026, 10, 20);

        var chips = Chips(new HashSet<TagId>(), vocabulary: vocabulary,
            usage: new() { [Funny] = 9, [Fail] = 2, [clutch] = 4 },
            lastUsed: new() { [Funny] = october, [Fail] = october.AddDays(5), [clutch] = october })
            .Single().Chips.Select(c => c.Tag);

        // Fail is the latest; Funny and Clutch tie on date, Funny is used more; Ace was never used.
        Assert.Equal([Fail, Funny, clutch, ace], chips);
    }

    [Fact]
    public void A_box_under_a_picked_tag_shows_all_its_tags_with_its_path()
    {
        var map = Chips(new HashSet<TagId> { Cs2 }).Single(g => g.Box.Id == Map);

        Assert.Equal([Mirage, Dust2], map.Chips.Select(c => c.Tag)); // never used: newest first
        Assert.Equal(["Counter-Strike 2", "Map"], map.Path);
        Assert.True(map.IsMissingRequired);
    }

    [Fact]
    public void Tags_on_the_clip_always_show_and_carried_ones_are_marked()
    {
        var groups = Chips(new HashSet<TagId> { Fail }, carried: new HashSet<TagId> { Fail });

        var fail = groups.Single(g => g.Box.Id == Moments).Chips.Single(c => c.Tag == Fail);
        Assert.True(fail.IsSelected);
        Assert.True(fail.IsCarriedOver);
    }

    [Fact]
    public void An_empty_required_box_is_flagged() =>
        Assert.True(Chips(new HashSet<TagId>()).Single(g => g.Box.Id == Game).IsMissingRequired);

    [Fact]
    public void An_optional_box_without_tags_is_left_out()
    {
        var empty = new BoxId(9);
        var vocabulary = new TagVocabulary(
            [new Box(Moments, "Moments", BoxRule.PickMany, Required: false, ParentTag: null),
                new Box(empty, "People", BoxRule.PickMany, Required: false, ParentTag: null)],
            [new Tag(Funny, "Funny", Moments)]);

        Assert.DoesNotContain(Chips(new HashSet<TagId>(), vocabulary: vocabulary), g => g.Box.Id == empty);
    }
}
