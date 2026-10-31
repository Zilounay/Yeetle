using Dapper;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Files;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Data;

public sealed class SqliteFileOperationStore(SqliteConnectionFactory connections) : IFileOperationStore
{
    private const string Planned = "Planned";
    private const string Done = "Done";
    private const string Failed = "Failed";

    public IReadOnlyList<FileTarget> Targets(IReadOnlyCollection<ClipId> clips)
    {
        using var connection = connections.Open();
        var ids = clips.Select(c => c.Value).ToList();
        var tags = connection.Query<(long ClipId, long TagId)>("SELECT ClipId, TagId FROM ClipTags WHERE ClipId IN @ids;", new { ids })
            .ToLookup(t => t.ClipId, t => new TagId(t.TagId));
        return connection.Query<TargetRow>(
                """
                SELECT c.Id, c.FolderId, f.Path AS FolderPath, c.RelativePath, c.OriginalFileName, c.RecordedAt
                FROM Clips c JOIN SourceFolders f ON f.Id = c.FolderId
                WHERE c.Id IN @ids ORDER BY c.RecordedAt, c.Id;
                """, new { ids })
            .Select(r => new FileTarget(new ClipId(r.Id), r.FolderId, r.FolderPath, r.RelativePath, r.OriginalFileName,
                DbTime.Read(r.RecordedAt), tags[r.Id].ToHashSet()))
            .ToList();
    }

    public bool IsPathKnown(long folderId, string relativePath, ClipId except)
    {
        using var connection = connections.Open();
        // RelativePath compares without case, as Windows does.
        return connection.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM Clips WHERE FolderId = @folderId AND RelativePath = @relativePath AND Id <> @id);",
            new { folderId, relativePath, id = except.Value });
    }

    public long Plan(ClipId clip, FileOperationKind kind, string fromPath, string? toPath, DateTime now)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO FileOperations (ClipId, Kind, FromPath, ToPath, Status, PlannedAt)
            VALUES (@clip, @kind, @fromPath, @toPath, @status, @now) RETURNING Id;
            """,
            new { clip = clip.Value, kind = kind.ToString(), fromPath, toPath, status = Planned, now = DbTime.Write(now) });
    }

    public void Complete(long operationId, ClipId clip, FileOperationKind kind, string? newRelativePath, DateTime now)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        if (kind == FileOperationKind.Delete)
            connection.Execute("UPDATE Clips SET FileState = @state WHERE Id = @id;",
                new { id = clip.Value, state = nameof(FileState.Deleted) }, transaction);
        else if (newRelativePath is not null)
            connection.Execute("UPDATE Clips SET RelativePath = @newRelativePath WHERE Id = @id;",
                new { id = clip.Value, newRelativePath }, transaction);
        Finish(connection, transaction, operationId, Done, null, now);
        transaction.Commit();
    }

    public void Fail(long operationId, FileProblem problem, string? detail, DateTime now)
    {
        using var connection = connections.Open();
        var error = detail is null ? problem.ToString() : $"{problem}: {detail}";
        Finish(connection, null, operationId, Failed, error, now);
    }

    public IReadOnlyList<PlannedOperation> Unfinished()
    {
        using var connection = connections.Open();
        return connection.Query<(long Id, long ClipId, string Kind, string FromPath, string? ToPath)>(
                "SELECT Id, ClipId, Kind, FromPath, ToPath FROM FileOperations WHERE Status = @status ORDER BY Id;",
                new { status = Planned })
            .Select(r => new PlannedOperation(r.Id, new ClipId(r.ClipId), Enum.Parse<FileOperationKind>(r.Kind), r.FromPath, r.ToPath))
            .ToList();
    }

    private static void Finish(SqliteConnection connection, SqliteTransaction? transaction, long operationId, string status, string? error, DateTime now) =>
        connection.Execute("UPDATE FileOperations SET Status = @status, Error = @error, FinishedAt = @now WHERE Id = @operationId;",
            new { operationId, status, error, now = DbTime.Write(now) }, transaction);

    private sealed class TargetRow
    {
        public long Id { get; init; }
        public long FolderId { get; init; }
        public string FolderPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string OriginalFileName { get; init; } = string.Empty;
        public string RecordedAt { get; init; } = string.Empty;
    }
}
