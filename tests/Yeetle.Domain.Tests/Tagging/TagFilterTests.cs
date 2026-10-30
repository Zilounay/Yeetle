using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Tagging;

public class TagFilterTests
{
    private readonly TagVocabulary _vocabulary = Create();

    private bool Matches(TagId[] filter, params TagId[] clip) =>
        new TagFilter(_vocabulary, filter).Matches(clip.ToHashSet());

    [Fact]
    public void Tags_from_the_same_box_combine_with_or()
    {
        TagId[] filter = [Dust2, Mirage];
        Assert.True(Matches(filter, Cs2, Dust2));
        Assert.True(Matches(filter, Cs2, Mirage));
        Assert.False(Matches(filter, Cs2, Ancient));
    }

    [Fact]
    public void Tags_from_different_boxes_combine_with_and()
    {
        TagId[] filter = [Cs2, Dust2, Mirage, M4];
        Assert.True(Matches(filter, Cs2, Mirage, M4));
        Assert.False(Matches(filter, Cs2, Mirage, Awp));
        Assert.False(Matches(filter, Dota2, M4));
    }

    [Fact]
    public void A_parent_tag_matches_clips_tagged_with_its_children()
    {
        Assert.True(Matches([Funny], Unexpected));
        Assert.True(Matches([Cs2], Dust2));
    }

    [Fact]
    public void An_empty_filter_matches_everything()
    {
        Assert.True(new TagFilter(_vocabulary, []).IsEmpty);
        Assert.True(Matches([]));
    }
}
