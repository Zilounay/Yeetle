using Dapper;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;

namespace Yeetle.Infrastructure.Data;

// Checks, backs up and upgrades the database before the app uses it (spec §10). It never
// recreates or empties a file: a problem is reported and the file left as it is.
public sealed class SqliteDatabaseStartup(SqliteConnectionFactory connections, IBackups backups, ILog log)
    : IDatabaseStartup
{
    private const string IntegrityOk = "ok";

    public int LatestSchemaVersion => Migrations.Latest;

    public string DataFolder => Path.GetDirectoryName(connections.DatabasePath)!;

    public DatabaseCheck Prepare()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var isNew = !File.Exists(connections.DatabasePath);
            int version;
            using (var connection = connections.Open())
            {
                var integrity = isNew ? IntegrityOk : connection.ExecuteScalar<string>("PRAGMA quick_check;");
                if (integrity != IntegrityOk)
                {
                    log.Error($"Database integrity check failed: {integrity}");
                    return new DatabaseCheck(DatabaseState.Damaged, 0, integrity);
                }
                version = connection.ExecuteScalar<int>("PRAGMA user_version;");
            }
            if (version > LatestSchemaVersion)
            {
                log.Error($"Database schema {version} is newer than this build ({LatestSchemaVersion}).");
                return new DatabaseCheck(DatabaseState.NewerSchema, version);
            }
            if (version < LatestSchemaVersion)
            {
                if (!isNew)
                    backups.Create(BackupPool.Safety);
                Migrate(version);
            }
            backups.CreateDailyIfDue();
            return new DatabaseCheck(DatabaseState.Ready, LatestSchemaVersion);
        }
        catch (SqliteException e) when (e.SqliteErrorCode is SQLitePCL.raw.SQLITE_CORRUPT or SQLitePCL.raw.SQLITE_NOTADB)
        {
            log.Error("Database is damaged.", e);
            return new DatabaseCheck(DatabaseState.Damaged, 0, e.Message);
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            log.Error("Database could not be opened.", e);
            return new DatabaseCheck(DatabaseState.Unavailable, 0, e.Message);
        }
    }

    // Each migration runs in one transaction together with its version number.
    private void Migrate(int from)
    {
        using var connection = connections.Open();
        foreach (var migration in Migrations.All.Where(m => m.Version > from))
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: transaction);
            connection.Execute($"PRAGMA user_version = {migration.Version};", transaction: transaction);
            transaction.Commit();
            log.Info($"Database migrated to {migration.Name}.");
        }
    }
}
