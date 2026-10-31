using Dapper;
using Yeetle.Application.Library;
using Yeetle.Domain.Clips;

namespace Yeetle.Infrastructure.Data;

public sealed class SqliteLibraryStore(SqliteConnectionFactory connections) : ILibraryStore
{
    public IReadOnlyList<SourceFolder> Folders()
    {
        using var connection = connections.Open();
        return connection.Query<FolderRow>(
                "SELECT Id, Path, IncludeSubfolders, Status, IsOffline, FirstScanDone FROM SourceFolders ORDER BY Path;")
            .Select(r => r.ToFolder())
            .ToList();
    }

    public SourceFolder AddFolder(string path, bool includeSubfolders, DateTime now)
    {
        using var connection = connections.Open();
        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO SourceFolders (Path, IncludeSubfolders, AddedAt) VALUES (@path, @includeSubfolders, @now)
            RETURNING Id;
            """,
            new { path, includeSubfolders, now = DbTime.Write(now) });
        return new SourceFolder(id, path, includeSubfolders, FolderStatus.Active, IsOffline: false, FirstScanDone: false);
    }

    public void UpdateFolder(SourceFolder folder)
    {
        using var connection = connections.Open();
        connection.Execute(
            """
            UPDATE SourceFolders
            SET IncludeSubfolders = @IncludeSubfolders, Status = @status, IsOffline = @IsOffline, FirstScanDone = @FirstScanDone
            WHERE Id = @Id;
            """,
            new { folder.Id, folder.IncludeSubfolders, status = folder.Status.ToString(), folder.IsOffline, folder.FirstScanDone });
    }

    public void DeleteFolder(long id)
    {
        using var connection = connections.Open();
        connection.Execute("DELETE FROM SourceFolders WHERE Id = @id;", new { id });
    }

    public int CountClips(long folderId)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Clips WHERE FolderId = @folderId;", new { folderId });
    }

    public IReadOnlyList<KnownClip> ClipsIn(long folderId)
    {
        using var connection = connections.Open();
        return connection.Query<ClipRow>(
                "SELECT Id, RelativePath, SizeBytes, ModifiedAt, FileState FROM Clips WHERE FolderId = @folderId;",
                new { folderId })
            .Select(r => new KnownClip(new ClipId(r.Id), r.RelativePath, r.SizeBytes, DbTime.Read(r.ModifiedAt),
                Enum.Parse<FileState>(r.FileState)))
            .ToList();
    }

    public void Apply(long folderId, ScanChanges changes, DateTime now)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            INSERT INTO Clips (FolderId, RelativePath, OriginalFileName, SizeBytes, ModifiedAt, RecordedAt, Decision, AddedAt)
            VALUES (@folderId, @RelativePath, @OriginalFileName, @SizeBytes, @ModifiedAt, @RecordedAt, @Decision, @now);
            """,
            changes.Added.Select(c => new
            {
                folderId,
                c.File.RelativePath,
                OriginalFileName = Path.GetFileName(c.File.RelativePath),
                c.File.SizeBytes,
                ModifiedAt = DbTime.Write(c.File.ModifiedAt),
                RecordedAt = DbTime.Write(c.RecordedAt),
                Decision = c.Decision.ToString(),
                now = DbTime.Write(now),
            }),
            transaction);
        connection.Execute(
            "UPDATE Clips SET SizeBytes = @SizeBytes, ModifiedAt = @ModifiedAt WHERE Id = @Id;",
            changes.Refreshed.Select(r => new { Id = r.Id.Value, r.File.SizeBytes, ModifiedAt = DbTime.Write(r.File.ModifiedAt) }),
            transaction);
        SetFileState(changes.Missing, FileState.Missing);
        SetFileState(changes.Found, FileState.Present);
        transaction.Commit();

        void SetFileState(IEnumerable<ClipId> clips, FileState state) => connection.Execute(
            "UPDATE Clips SET FileState = @state WHERE Id = @Id;",
            clips.Select(c => new { Id = c.Value, state = state.ToString() }),
            transaction);
    }

    private sealed class FolderRow
    {
        public long Id { get; init; }
        public string Path { get; init; } = string.Empty;
        public bool IncludeSubfolders { get; init; }
        public string Status { get; init; } = string.Empty;
        public bool IsOffline { get; init; }
        public bool FirstScanDone { get; init; }

        public SourceFolder ToFolder() =>
            new(Id, Path, IncludeSubfolders, Enum.Parse<FolderStatus>(Status), IsOffline, FirstScanDone);
    }

    private sealed class ClipRow
    {
        public long Id { get; init; }
        public string RelativePath { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public string ModifiedAt { get; init; } = string.Empty;
        public string FileState { get; init; } = string.Empty;
    }
}
