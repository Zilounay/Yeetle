using System.Collections.Immutable;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// A clip as a session needs it: what to play and what to show on its card, and the tags and
// markers it already has.
public sealed record SessionClip(ClipId Id, string FullPath, DateTime RecordedAt, ImmutableHashSet<TagId> Tags)
{
    public ImmutableList<Marker> Markers { get; init; } = [];

    public string FileName => Path.GetFileName(FullPath);
}

// The whole state of a session after an action, as written to the journal.
public sealed record SessionSnapshot(int Position, IReadOnlyList<ClipReview> Clips);

// A session the app did not get to commit or discard, usually because it crashed.
public sealed record UnfinishedSession(long Id, DateTime StartedAt, SessionSnapshot Snapshot)
{
    public int Decided => Snapshot.Clips.Count(c => c.Decision != Decision.New);
}

// Sessions in the database (spec §5). Blocking calls: SessionService runs them in the background.
public interface ISessionStore
{
    int CountNew();

    // New clips that can be played, skipped ones first, then oldest recording first.
    IReadOnlyList<SessionClip> NewClips();

    IReadOnlyList<SessionClip> Clips(IReadOnlyCollection<ClipId> ids);

    long Start(DateTime now);

    void Journal(long sessionId, SessionSnapshot snapshot, DateTime now);

    UnfinishedSession? FindUnfinished();

    // Saves decisions and tags in one transaction and clears the journal. Visited clips left
    // New are marked skipped, so they come first next time.
    void Commit(long sessionId, IReadOnlyList<ClipReview> clips, DateTime now);

    // Drops everything the session did; its clips stay as they were.
    void Discard(long sessionId, DateTime now);
}

public interface ITagStore
{
    TagCatalog LoadCatalog();
}
