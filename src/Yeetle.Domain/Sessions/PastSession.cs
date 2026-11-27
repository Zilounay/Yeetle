using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Sessions;

// One clip as a committed session left it (#72): its place in the review, the decision taken then
// (New for a clip skipped), when, and the tags it had then.
public sealed record PastReview(
    ClipId Clip, int Position, Decision Decision, DateTime? ReviewedAt, ImmutableHashSet<TagId> Tags);

// A committed session and the clips it handled, in review order (#72).
public sealed record PastSession(long Id, DateTime StartedAt, DateTime EndedAt, IReadOnlyList<PastReview> Clips)
{
    public int Kept => Clips.Count(c => c.Decision == Decision.Kept);

    public int Yeeted => Clips.Count(c => c.Decision == Decision.Yeeted);

    public int Skipped => Clips.Count(c => c.Decision == Decision.New);

    public TimeSpan Duration => EndedAt - StartedAt;
}
