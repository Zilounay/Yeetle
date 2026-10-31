using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Library;

public class MarkerSheetTests
{
    [Fact]
    public void Lists_each_clip_with_its_tags_in_manager_order_and_its_markers()
    {
        var clip = new LibraryClip(new ClipId(1), @"D:\Clips\2026-10-11 22-15-36.mp4", DateTime.Now, null, FileState.Present,
            Decision.Kept, [Funny, M4, Dust2, Cs2],
            [new Marker(1, TimeSpan.FromSeconds(87), "Smoke kill"), new Marker(2, TimeSpan.FromSeconds(197), "Triple kill")]);
        var other = clip with { Id = new ClipId(2), FullPath = @"D:\Clips\b.mp4", Tags = [], Markers = [] };

        var text = MarkerSheet.Write([clip, other], Create(), "Tags:", "Markers:");

        Assert.Equal(
            """
            ### 2026-10-11 22-15-36.mp4 ###
            Tags: Counter-Strike 2, Dust2, M4, Funny
            Markers:
            1:27  Smoke kill
            3:17  Triple kill

            ### b.mp4 ###
            Tags:
            Markers:

            """.ReplaceLineEndings(), text);
    }
}
