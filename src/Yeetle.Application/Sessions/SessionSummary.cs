using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;

namespace Yeetle.Application.Sessions;

public sealed record SummaryClip(ClipId Clip, string FileName, IReadOnlyList<string> Tags, int MarkerCount);

// The read-only summary shown before Commit or Discard (spec §5): counts, then the clips kept,
// yeeted and skipped, each with its tags (in Tag Manager order) and marker count.
public sealed record SessionSummary(
    int Reviewed, IReadOnlyList<SummaryClip> Kept, IReadOnlyList<SummaryClip> Yeeted, IReadOnlyList<SummaryClip> Skipped)
{
    // Kept and yeeted clips: what Commit saves.
    public int Decisions => Kept.Count + Yeeted.Count;

    public static SessionSummary From(ActiveSession session)
    {
        var vocabulary = session.Catalog.Vocabulary;
        var reviews = session.Reviews;

        SummaryClip Item(ClipReview review) => new(review.Clip, session.Clip(review.Clip).FileName,
            vocabulary.InManagerOrder.Where(review.Tags.Contains).Select(t => vocabulary.Tag(t).Name).ToList(),
            review.Markers.Count);

        return new SessionSummary(
            reviews.Count(r => r.Visited || r.Decision != Decision.New),
            reviews.Where(r => r.Decision == Decision.Kept).Select(Item).ToList(),
            reviews.Where(r => r.Decision == Decision.Yeeted).Select(Item).ToList(),
            reviews.Where(r => r.Visited && r.Decision == Decision.New).Select(Item).ToList());
    }
}
