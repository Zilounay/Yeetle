using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Library;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// Starts, resumes and ends review sessions (spec §5). Database work runs in the background;
// journal entries are written one after another, in the order the actions happened.
public sealed class SessionService(
    ISessionStore store, ITagStore tags, IBackups backups, ScanScheduler scans, TimeProvider clock, ILog log)
{
    private readonly SemaphoreSlim _journalGate = new(1, 1);
    private Task _journalTail = Task.CompletedTask;

    public Task<int> CountNewAsync() => Task.Run(store.CountNew);

    public Task<UnfinishedSession?> FindUnfinishedAsync() => Task.Run(store.FindUnfinished);

    // Null when there is nothing to review. A scan runs first, so just-recorded clips are in.
    public async Task<ActiveSession?> StartAsync()
    {
        await scans.RequestAsync(ScanTrigger.SessionStart);
        var (clips, vocabulary, id) = await Task.Run(() =>
        {
            var newClips = store.NewClips();
            return (newClips, tags.LoadVocabulary(), newClips.Count > 0 ? store.Start(Now) : 0);
        });
        if (clips.Count == 0)
            return null;
        var review = new ReviewSession(new TagRules(vocabulary),
            clips.Select(c => new ClipReview(c.Id, Domain.Clips.Decision.New, c.Tags, [], Visited: false)), position: 0);
        var session = Create(id, review, clips);
        Journal(id, session.Snapshot());
        log.Info($"Session {id} started with {clips.Count} clips.");
        return session;
    }

    public async Task<ActiveSession> ResumeAsync(UnfinishedSession unfinished)
    {
        var (clips, vocabulary) = await Task.Run(() =>
            (store.Clips(unfinished.Snapshot.Clips.Select(c => c.Clip).ToList()), tags.LoadVocabulary()));
        var known = clips.Select(c => c.Id).ToHashSet();
        // A clip wiped from the library meanwhile simply leaves the session.
        var reviews = unfinished.Snapshot.Clips.Where(c => known.Contains(c.Clip)).ToList();
        var review = new ReviewSession(new TagRules(vocabulary), reviews, unfinished.Snapshot.Position);
        log.Info($"Session {unfinished.Id} resumed.");
        return Create(unfinished.Id, review, clips);
    }

    // Completes once every journal entry written so far is in the database.
    public Task FlushJournalAsync() => _journalTail;

    // Saves the session, then takes a session backup (spec §10).
    public async Task CommitAsync(long sessionId, IReadOnlyList<ClipReview> reviews)
    {
        await FlushJournalAsync();
        await Task.Run(() =>
        {
            store.Commit(sessionId, reviews, Now);
            backups.Create(BackupPool.Session);
        });
        log.Info($"Session {sessionId} committed.");
    }

    public async Task DiscardAsync(long sessionId)
    {
        await FlushJournalAsync();
        await Task.Run(() => store.Discard(sessionId, Now));
        log.Info($"Session {sessionId} discarded.");
    }

    private DateTime Now => clock.GetLocalNow().DateTime;

    private ActiveSession Create(long id, ReviewSession review, IEnumerable<SessionClip> clips) =>
        new(id, review, clips, snapshot => Journal(id, snapshot));

    private void Journal(long sessionId, SessionSnapshot snapshot)
    {
        var now = Now;
        _journalTail = WriteAsync();

        async Task WriteAsync()
        {
            await _journalGate.WaitAsync();
            try
            {
                await Task.Run(() => store.Journal(sessionId, snapshot, now));
            }
            catch (Exception e)
            {
                // The session goes on in memory; only crash recovery is weaker until the next write.
                log.Error($"Session {sessionId}: journal entry could not be written.", e);
            }
            finally
            {
                _journalGate.Release();
            }
        }
    }
}
