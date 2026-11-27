using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;
using static Yeetle.Domain.Tests.Tagging.SampleVocabulary;

namespace Yeetle.Domain.Tests.Library;

// The clips of a past session (#72): what was decided then, the clip as it is now.
public class SessionQueryTests
{
    private static readonly TagVocabulary Vocabulary = Create();
    private static readonly DateTime Day = new(2026, 11, 27, 20, 0, 0);
    private static readonly HashSet<Decision> Every = [Decision.Kept, Decision.Yeeted, Decision.New];

    private static LibraryClip Clip(long id, string name, Decision now, TagId[]? tags = null) =>
        new(new ClipId(id), Path.Combine(@"D:\Clips", name), Day, null, FileState.Present, now,
            (tags ?? []).ToImmutableHashSet(), []);

    private static PastReview Review(long id, int position, Decision then, TagId[]? tags = null) =>
        new(new ClipId(id), position, then, Day.AddMinutes(position), (tags ?? []).ToImmutableHashSet());

    private static readonly PastSession Session = new(1, Day, Day.AddMinutes(10),
    [
        Review(1, 0, Decision.Kept, [Cs2]), Review(2, 1, Decision.Yeeted), Review(3, 2, Decision.New),
    ]);

    private static readonly Dictionary<ClipId, LibraryClip> Clips = new[]
    {
        // Kept then, tags changed since; yeeted then, kept since; skipped then, still New.
        Clip(1, "b.mp4", Decision.Kept, [Cs2, Dust2]), Clip(2, "c.mp4", Decision.Kept), Clip(3, "a.mp4", Decision.New),
    }.ToDictionary(c => c.Id);

    [Fact]
    public void Keeps_the_review_order_and_the_decision_taken_then()
    {
        var rows = SessionQuery.Apply(Session, Clips, Every, [], string.Empty, Vocabulary);

        Assert.Equal(["b.mp4", "c.mp4", "a.mp4"], rows.Select(r => r.Clip.FileName));
        Assert.Equal([Decision.Kept, Decision.Yeeted, Decision.New], rows.Select(r => r.Review.Decision));
    }

    [Fact]
    public void Tells_what_changed_since()
    {
        var rows = SessionQuery.Apply(Session, Clips, Every, [], string.Empty, Vocabulary);

        Assert.Equal([false, true, false], rows.Select(r => r.DecisionChanged));
        Assert.Equal([true, false, false], rows.Select(r => r.TagsChanged));
    }

    [Fact]
    public void Filters_by_decision_then_current_tags_and_text_and_sorts()
    {
        Assert.Equal(["c.mp4"], SessionQuery.Apply(Session, Clips, new HashSet<Decision> { Decision.Yeeted }, [], string.Empty, Vocabulary)
            .Select(r => r.Clip.FileName));
        Assert.Equal(["b.mp4"], SessionQuery.Apply(Session, Clips, Every, [Dust2], string.Empty, Vocabulary).Select(r => r.Clip.FileName));
        Assert.Equal(["a.mp4"], SessionQuery.Apply(Session, Clips, Every, [], "A.MP", Vocabulary).Select(r => r.Clip.FileName));
        Assert.Equal(["a.mp4", "b.mp4", "c.mp4"], SessionQuery.Apply(Session, Clips, Every, [], string.Empty, Vocabulary, SessionSort.Name)
            .Select(r => r.Clip.FileName));
    }

    [Fact]
    public void A_clip_no_longer_in_the_library_leaves_the_list()
    {
        var rows = SessionQuery.Apply(Session, Clips.Where(c => c.Key.Value != 2).ToDictionary(), Every, [], string.Empty, Vocabulary);

        Assert.Equal(["b.mp4", "a.mp4"], rows.Select(r => r.Clip.FileName));
    }
}
