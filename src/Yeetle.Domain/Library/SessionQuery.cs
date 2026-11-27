using System.Globalization;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Library;

// One clip of a past session (#72): the decision taken then, and the clip as it is now.
public sealed record SessionRow(PastReview Review, LibraryClip Clip)
{
    // Decided otherwise since (in the Explorer, a later session or a wipe).
    public bool DecisionChanged => Clip.Decision != Review.Decision;

    // The Tags column shows the current tags; this tells they differ from those at commit.
    public bool TagsChanged => !Clip.Tags.SetEquals(Review.Tags);
}

public enum SessionSort
{
    Position,
    Name,
    ReviewedAt,
    Decision,
    Duration,
}

// The clips of one past session, filtered and sorted (#72). Filters work as in the Explorer:
// Decisions lists the decisions taken then (New for skipped), tags combine as in TagFilter on the
// current tags, and text looks in file names and marker labels.
public static class SessionQuery
{
    public static IReadOnlyList<SessionRow> Apply(PastSession session, IReadOnlyDictionary<ClipId, LibraryClip> clips,
        IReadOnlySet<Decision> decisions, IReadOnlyCollection<TagId> tags, string text, TagVocabulary vocabulary,
        SessionSort sort = SessionSort.Position, bool descending = false)
    {
        var tagFilter = new TagFilter(vocabulary, tags.Where(t => vocabulary.InManagerOrder.Contains(t)));
        var search = text.Trim();
        // A clip no longer in the library (its folder hidden) leaves the list.
        var rows = session.Clips
            .Where(r => clips.ContainsKey(r.Clip))
            .Select(r => new SessionRow(r, clips[r.Clip]))
            .Where(r => decisions.Contains(r.Review.Decision)
                        && tagFilter.Matches(r.Clip.Tags)
                        && (search.Length == 0 || LibraryQuery.ContainsText(r.Clip, search)));
        return Sort(rows, sort, descending).ToList();
    }

    private static IEnumerable<SessionRow> Sort(IEnumerable<SessionRow> rows, SessionSort sort, bool descending)
    {
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        IOrderedEnumerable<SessionRow> ordered = sort switch
        {
            SessionSort.Name => descending
                ? rows.OrderByDescending(r => r.Clip.FileName, byName)
                : rows.OrderBy(r => r.Clip.FileName, byName),
            // Clips reviewed before decision times were recorded sort first.
            SessionSort.ReviewedAt => descending
                ? rows.OrderByDescending(r => r.Review.ReviewedAt ?? DateTime.MinValue)
                : rows.OrderBy(r => r.Review.ReviewedAt ?? DateTime.MinValue),
            SessionSort.Decision => descending
                ? rows.OrderByDescending(r => r.Review.Decision)
                : rows.OrderBy(r => r.Review.Decision),
            SessionSort.Duration => descending
                ? rows.OrderByDescending(r => r.Clip.Duration ?? TimeSpan.Zero)
                : rows.OrderBy(r => r.Clip.Duration ?? TimeSpan.Zero),
            _ => descending ? rows.OrderByDescending(r => r.Review.Position) : rows.OrderBy(r => r.Review.Position),
        };
        return ordered.ThenBy(r => r.Review.Position);
    }
}
