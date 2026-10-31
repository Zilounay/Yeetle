using System.Collections.Immutable;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

public sealed class SqliteSessionStore(SqliteConnectionFactory connections, TimeProvider clock) : ISessionStore
{
    private const string Committed = "Committed";
    private const string Discarded = "Discarded";

    // Clips a session can play: New, file present, folder neither hidden nor offline.
    private const string PlayableNew = """
        FROM Clips c JOIN SourceFolders f ON f.Id = c.FolderId
        WHERE c.Decision = 'New' AND c.FileState = 'Present' AND f.Status <> 'Hidden' AND f.IsOffline = 0
        """;

    private const string ClipColumns = "SELECT c.Id, f.Path AS FolderPath, c.RelativePath, c.RecordedAt ";

    public int CountNew()
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<int>("SELECT COUNT(*) " + PlayableNew + ";");
    }

    public IReadOnlyList<SessionClip> NewClips()
    {
        using var connection = connections.Open();
        var rows = connection.Query<ClipRow>(ClipColumns + PlayableNew + " ORDER BY c.SkippedAt IS NULL, c.RecordedAt, c.Id;");
        return WithTags(connection, rows.ToList());
    }

    public IReadOnlyList<SessionClip> Clips(IReadOnlyCollection<ClipId> ids)
    {
        using var connection = connections.Open();
        var rows = connection.Query<ClipRow>(
            ClipColumns + "FROM Clips c JOIN SourceFolders f ON f.Id = c.FolderId WHERE c.Id IN @ids;",
            new { ids = ids.Select(i => i.Value).ToList() });
        return WithTags(connection, rows.ToList());
    }

    public long Start(DateTime now)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<long>("INSERT INTO Sessions (StartedAt) VALUES (@now) RETURNING Id;",
            new { now = DbTime.Write(now) });
    }

    // Only the latest entry matters for recovery, so older ones are dropped as new ones arrive.
    public void Journal(long sessionId, SessionSnapshot snapshot, DateTime now)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var seq = connection.ExecuteScalar<long>(
            "INSERT INTO SessionJournal (SessionId, Snapshot, CreatedAt) VALUES (@sessionId, @json, @now) RETURNING Seq;",
            new { sessionId, json = SnapshotJson.Write(snapshot), now = DbTime.Write(now) }, transaction);
        connection.Execute("DELETE FROM SessionJournal WHERE SessionId = @sessionId AND Seq < @seq;",
            new { sessionId, seq }, transaction);
        transaction.Commit();
    }

    public UnfinishedSession? FindUnfinished()
    {
        using var connection = connections.Open();
        var row = connection.QueryFirstOrDefault<(long Id, string StartedAt, string? Snapshot)>(
            """
            SELECT s.Id, s.StartedAt,
                   (SELECT j.Snapshot FROM SessionJournal j WHERE j.SessionId = s.Id ORDER BY j.Seq DESC LIMIT 1)
            FROM Sessions s WHERE s.Outcome IS NULL ORDER BY s.Id DESC LIMIT 1;
            """);
        if (row.Id == 0)
            return null;
        // A session that crashed before its first entry has nothing to recover.
        if (row.Snapshot is null)
        {
            Close(connection, row.Id, Discarded, clock.GetLocalNow().DateTime);
            return null;
        }
        return new UnfinishedSession(row.Id, DbTime.Read(row.StartedAt), SnapshotJson.Read(row.Snapshot));
    }

    public void Commit(long sessionId, IReadOnlyList<ClipReview> clips, DateTime now)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var clip in clips.Where(c => c.Visited || c.Decision != Decision.New))
        {
            connection.Execute(
                """
                UPDATE Clips SET Decision = @decision,
                    SkippedAt = CASE WHEN @decision = 'New' THEN @now ELSE NULL END
                WHERE Id = @id;
                """,
                new { id = clip.Clip.Value, decision = clip.Decision.ToString(), now = DbTime.Write(now) }, transaction);
            connection.Execute("DELETE FROM ClipTags WHERE ClipId = @id;", new { id = clip.Clip.Value }, transaction);
            // A tag deleted in the Tag Manager after the snapshot was taken is skipped.
            connection.Execute(
                "INSERT INTO ClipTags (ClipId, TagId) SELECT @clipId, Id FROM Nodes WHERE Id = @tagId AND Kind = 'Tag';",
                clip.Tags.Select(t => new { clipId = clip.Clip.Value, tagId = t.Value }), transaction);
            connection.Execute("DELETE FROM Markers WHERE ClipId = @id;", new { id = clip.Clip.Value }, transaction);
            connection.Execute("INSERT INTO Markers (ClipId, PositionMs, Label) VALUES (@clipId, @positionMs, @label);",
                clip.Markers.Select(m => new
                {
                    clipId = clip.Clip.Value, positionMs = (long)m.Position.TotalMilliseconds, label = m.Label,
                }), transaction);
        }
        Close(connection, sessionId, Committed, now, transaction);
        transaction.Commit();
    }

    public void Discard(long sessionId, DateTime now)
    {
        using var connection = connections.Open();
        Close(connection, sessionId, Discarded, now);
    }

    private static void Close(SqliteConnection connection, long sessionId, string outcome, DateTime now,
        SqliteTransaction? transaction = null)
    {
        connection.Execute("UPDATE Sessions SET Outcome = @outcome, EndedAt = @now WHERE Id = @sessionId;",
            new { sessionId, outcome, now = DbTime.Write(now) }, transaction);
        connection.Execute("DELETE FROM SessionJournal WHERE SessionId = @sessionId;", new { sessionId }, transaction);
    }

    private static List<SessionClip> WithTags(SqliteConnection connection, List<ClipRow> rows)
    {
        var tags = connection.Query<(long ClipId, long TagId)>(
                "SELECT ClipId, TagId FROM ClipTags WHERE ClipId IN @ids;", new { ids = rows.Select(r => r.Id).ToList() })
            .ToLookup(t => t.ClipId, t => new TagId(t.TagId));
        // Markers get numbers 1, 2, 3... in position order, as stored rows have none.
        var markers = connection.Query<(long ClipId, long PositionMs, string Label)>(
                "SELECT ClipId, PositionMs, Label FROM Markers WHERE ClipId IN @ids ORDER BY PositionMs, Id;",
                new { ids = rows.Select(r => r.Id).ToList() })
            .ToLookup(m => m.ClipId);
        return rows.Select(r => new SessionClip(new ClipId(r.Id), Path.Combine(r.FolderPath, r.RelativePath),
            DbTime.Read(r.RecordedAt), tags[r.Id].ToImmutableHashSet())
        {
            Markers = markers[r.Id].Select((m, i) => new Marker(i + 1, TimeSpan.FromMilliseconds(m.PositionMs), m.Label))
                .ToImmutableList(),
        }).ToList();
    }

    private sealed class ClipRow
    {
        public long Id { get; init; }
        public string FolderPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string RecordedAt { get; init; } = string.Empty;
    }

    // The snapshot as compact JSON, independent of how the domain types are shaped.
    private static class SnapshotJson
    {
        public static string Write(SessionSnapshot snapshot) => JsonSerializer.Serialize(new SnapshotDto(
            snapshot.Position,
            snapshot.Clips.Select(c => new ClipDto(c.Clip.Value, c.Decision.ToString(),
                c.Tags.Select(t => t.Value).ToList(), c.CarriedOver.Select(t => t.Value).ToList(), c.Visited,
                c.Markers.Select(m => new MarkerDto(m.Number, (long)m.Position.TotalMilliseconds, m.Label)).ToList())).ToList()));

        public static SessionSnapshot Read(string json)
        {
            var dto = JsonSerializer.Deserialize<SnapshotDto>(json)!;
            return new SessionSnapshot(dto.Position, dto.Clips.Select(c => new ClipReview(
                new ClipId(c.Id), Enum.Parse<Decision>(c.Decision),
                c.Tags.Select(t => new TagId(t)).ToImmutableHashSet(),
                c.CarriedOver.Select(t => new TagId(t)).ToImmutableHashSet(),
                c.Visited)
            {
                // Journals written before markers existed have none.
                Markers = (c.Markers ?? []).Select(m => new Marker(m.Number, TimeSpan.FromMilliseconds(m.PositionMs), m.Label))
                    .ToImmutableList(),
            }).ToList());
        }

        private sealed record SnapshotDto(int Position, List<ClipDto> Clips);

        private sealed record ClipDto(
            long Id, string Decision, List<long> Tags, List<long> CarriedOver, bool Visited, List<MarkerDto>? Markers);

        private sealed record MarkerDto(int Number, long PositionMs, string Label);
    }
}
