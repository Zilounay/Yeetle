using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Library;

public class LibraryQueryTests
{
    private static readonly TagVocabulary Vocabulary = Create();
    private static readonly DateTime Day = new(2026, 10, 11, 20, 0, 0);

    private static LibraryClip Clip(long id, string name, int hour = 0, Decision decision = Decision.Kept,
        FileState state = FileState.Present, TagId[]? tags = null, string[]? markers = null, int seconds = 0) =>
        new(new ClipId(id), Path.Combine(@"D:\Clips", name), Day.AddHours(hour), seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
            state, decision, (tags ?? []).ToImmutableHashSet(),
            (markers ?? []).Select((label, i) => new Marker(i + 1, TimeSpan.FromSeconds(i), label)).ToImmutableList());

    private static IEnumerable<string> Names(IEnumerable<LibraryClip> clips) => clips.Select(c => c.FileName);

    [Fact]
    public void Shows_kept_clips_newest_first_and_leaves_deleted_ones_out()
    {
        LibraryClip[] clips =
        [
            Clip(1, "old.mp4", hour: 1), Clip(2, "new.mp4", hour: 5), Clip(3, "yeeted.mp4", decision: Decision.Yeeted),
            Clip(4, "fresh.mp4", decision: Decision.New), Clip(5, "gone.mp4", state: FileState.Deleted),
            Clip(6, "unplugged.mp4", hour: 2, state: FileState.Offline),
        ];

        var shown = LibraryQuery.Apply(clips, LibraryFilter.Default, Vocabulary);

        Assert.Equal(["new.mp4", "unplugged.mp4", "old.mp4"], Names(shown));
    }

    [Fact]
    public void Tags_combine_like_the_tag_filter_and_text_finds_names_and_marker_labels()
    {
        LibraryClip[] clips =
        [
            Clip(1, "a.mp4", tags: [Cs2, Dust2], markers: ["Smoke kill"]), Clip(2, "b.mp4", tags: [Cs2, Mirage]),
            Clip(3, "c.mp4", tags: [Dota2]), Clip(4, "Clutch Éclair.mp4"),
        ];

        Assert.Equal(["a.mp4"], Names(LibraryQuery.Apply(clips, LibraryFilter.Default with { Tags = [Dust2] }, Vocabulary)));
        Assert.Equal(["a.mp4", "b.mp4"], Names(LibraryQuery.Apply(clips, LibraryFilter.Default with { Tags = [Cs2] }, Vocabulary, LibrarySort.Name, descending: false)));
        Assert.Equal(["a.mp4"], Names(LibraryQuery.Apply(clips, LibraryFilter.Default with { Text = "SMOKE" }, Vocabulary)));
        Assert.Equal(["Clutch Éclair.mp4"], Names(LibraryQuery.Apply(clips, LibraryFilter.Default with { Text = "eclair" }, Vocabulary)));
    }

    [Fact]
    public void Incomplete_finds_clips_with_an_empty_visible_required_box()
    {
        // Game is required at the top; Map is required under CS2.
        LibraryClip[] clips = [Clip(1, "untagged.mp4"), Clip(2, "cs-no-map.mp4", tags: [Cs2]), Clip(3, "done.mp4", tags: [Dota2])];

        var shown = LibraryQuery.Apply(clips, LibraryFilter.Default with { IncompleteOnly = true }, Vocabulary, LibrarySort.Name, descending: false);

        Assert.Equal(["cs-no-map.mp4", "untagged.mp4"], Names(shown));
    }

    [Fact]
    public void Sorts_by_duration_with_unknown_durations_last_when_longest_first()
    {
        LibraryClip[] clips = [Clip(1, "short.mp4", seconds: 10), Clip(2, "unknown.mp4"), Clip(3, "long.mp4", seconds: 90)];

        Assert.Equal(["long.mp4", "short.mp4", "unknown.mp4"], Names(LibraryQuery.Apply(clips, LibraryFilter.Default, Vocabulary, LibrarySort.Duration)));
    }

    [Fact]
    public void A_deleted_tag_in_the_filter_is_ignored() =>
        Assert.Single(LibraryQuery.Apply([Clip(1, "a.mp4")], LibraryFilter.Default with { Tags = [new TagId(999)] }, Vocabulary));
}
