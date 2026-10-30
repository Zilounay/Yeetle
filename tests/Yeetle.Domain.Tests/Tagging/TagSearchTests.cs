using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Tagging;

public class TagSearchTests
{
    private readonly TagVocabulary _vocabulary = Create();

    [Fact]
    public void Finds_tags_containing_the_text_ignoring_case() =>
        Assert.Equal([Dust2], TagSearch.Find(_vocabulary, "DUST"));

    [Fact]
    public void Names_starting_with_the_text_come_first() =>
        Assert.Equal([Ancient, Awp, Mirage, Dota2], TagSearch.Find(_vocabulary, "a"));

    [Fact]
    public void Empty_text_finds_nothing() => Assert.Empty(TagSearch.Find(_vocabulary, "  "));

    [Fact]
    public void Works_for_any_alphabet()
    {
        var vocabulary = new TagVocabulary(
            [new Box(new BoxId(1), "Игра", BoxRule.PickOne, false, null)],
            [new Tag(new TagId(1), "Дота 2", new BoxId(1))]);

        Assert.Equal([new TagId(1)], TagSearch.Find(vocabulary, "дота"));
    }

    [Fact]
    public void The_path_names_parent_tags_and_named_boxes_but_not_the_top_box()
    {
        Assert.Equal(["Dota 2", "Hero", "Pudge"], _vocabulary.PathOf(Pudge));
        Assert.Equal(["Counter-Strike 2"], _vocabulary.PathOf(Cs2));
        Assert.Equal(["Funny", "Unexpected"], _vocabulary.PathOf(Unexpected)); // unnamed box
    }
}
