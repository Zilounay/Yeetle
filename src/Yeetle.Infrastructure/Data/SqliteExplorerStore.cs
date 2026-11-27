using System.Collections.Immutable;
using Dapper;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Library;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

public sealed class SqliteExplorerStore(SqliteConnectionFactory connections) : IExplorerStore
{
    private const string ClipColumns = """
        SELECT c.Id, f.Path AS FolderPath, c.RelativePath, c.RecordedAt, c.DurationMs, c.FileState, c.Decision,
               f.IsOffline AS FolderOffline
        FROM Clips c JOIN SourceFolders f ON f.Id = c.FolderId
        WHERE f.Status <> 'Hidden'
        """;

    public IReadOnlyList<LibraryClip> LoadClips()
    {
        using var connection = connections.Open();
        var rows = connection.Query<ClipRow>(ClipColumns + ";").ToList();
        var tags = connection.Query<(long ClipId, long TagId)>("SELECT ClipId, TagId FROM ClipTags;")
            .ToLookup(t => t.ClipId, t => new TagId(t.TagId));
        var markers = connection.Query<MarkerRow>("SELECT ClipId, PositionMs, Label FROM Markers ORDER BY PositionMs, Id;")
            .ToLookup(m => m.ClipId);
        return rows.Select(r => ToClip(r, tags[r.Id], markers[r.Id])).ToList();
    }

    public LibraryClip? LoadClip(ClipId clip)
    {
        using var connection = connections.Open();
        var row = connection.QuerySingleOrDefault<ClipRow>(ClipColumns + " AND c.Id = @id;", new { id = clip.Value });
        if (row is null)
            return null;
        var tags = connection.Query<long>("SELECT TagId FROM ClipTags WHERE ClipId = @id;", new { id = clip.Value })
            .Select(t => new TagId(t));
        var markers = connection.Query<MarkerRow>(
            "SELECT ClipId, PositionMs, Label FROM Markers WHERE ClipId = @id ORDER BY PositionMs, Id;", new { id = clip.Value });
        return ToClip(row, tags, markers);
    }

    public void SetDecision(IReadOnlyCollection<ClipId> clips, Decision decision)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("UPDATE Clips SET Decision = @decision, SkippedAt = NULL WHERE Id = @id;",
            clips.Select(c => new { id = c.Value, decision = decision.ToString() }), transaction);
        transaction.Commit();
    }

    public void Wipe(IReadOnlyCollection<ClipId> clips)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var ids = clips.Select(c => c.Value).ToList();
        connection.Execute("DELETE FROM ClipTags WHERE ClipId IN @ids;", new { ids }, transaction);
        connection.Execute("DELETE FROM Markers WHERE ClipId IN @ids;", new { ids }, transaction);
        connection.Execute("UPDATE Clips SET Decision = 'New', SkippedAt = NULL WHERE Id IN @ids;", new { ids }, transaction);
        transaction.Commit();
    }

    public void SaveEdit(ClipId clip, Decision decision, IReadOnlySet<TagId> tags, IReadOnlyList<Marker> markers)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var id = clip.Value;
        connection.Execute("UPDATE Clips SET Decision = @decision WHERE Id = @id;", new { id, decision = decision.ToString() },
            transaction);
        // Retired tags stay on the clip: they cannot be picked or removed in the editor (spec §6).
        connection.Execute(
            "DELETE FROM ClipTags WHERE ClipId = @id AND TagId IN (SELECT Id FROM Nodes WHERE Retired = 0);", new { id }, transaction);
        connection.Execute(
            "INSERT INTO ClipTags (ClipId, TagId) SELECT @id, Id FROM Nodes WHERE Id = @tagId AND Kind = 'Tag' AND Retired = 0;",
            tags.Select(t => new { id, tagId = t.Value }), transaction);
        ReplaceMarkers(connection, transaction, id, markers);
        transaction.Commit();
    }

    private static void ReplaceMarkers(SqliteConnection connection, SqliteTransaction transaction, long id,
        IReadOnlyList<Marker> markers)
    {
        connection.Execute("DELETE FROM Markers WHERE ClipId = @id;", new { id }, transaction);
        connection.Execute("INSERT INTO Markers (ClipId, PositionMs, Label) VALUES (@id, @positionMs, @label);",
            markers.Select(m => new { id, positionMs = (long)m.Position.TotalMilliseconds, label = m.Label }), transaction);
    }

    // Stored markers have no number: they get 1, 2, 3... in position order.
    private static LibraryClip ToClip(ClipRow row, IEnumerable<TagId> tags, IEnumerable<MarkerRow> markers)
    {
        var state = Enum.Parse<FileState>(row.FileState);
        // An unreachable folder makes its clips Offline, except those Yeetle deleted (spec §3).
        if (row.FolderOffline && state != FileState.Deleted)
            state = FileState.Offline;
        return new LibraryClip(new ClipId(row.Id), Path.Combine(row.FolderPath, row.RelativePath), DbTime.Read(row.RecordedAt),
            row.DurationMs is > ClipDurations.Unreadable ? TimeSpan.FromMilliseconds(row.DurationMs.Value) : null,
            state, Enum.Parse<Decision>(row.Decision), tags.ToImmutableHashSet(),
            markers.Select((m, i) => new Marker(i + 1, TimeSpan.FromMilliseconds(m.PositionMs), m.Label)).ToImmutableList());
    }

    private sealed class ClipRow
    {
        public long Id { get; init; }
        public string FolderPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string RecordedAt { get; init; } = string.Empty;
        public long? DurationMs { get; init; }
        public string FileState { get; init; } = string.Empty;
        public string Decision { get; init; } = string.Empty;
        public bool FolderOffline { get; init; }
    }

    private sealed class MarkerRow
    {
        public long ClipId { get; init; }
        public long PositionMs { get; init; }
        public string Label { get; init; } = string.Empty;
    }
}
