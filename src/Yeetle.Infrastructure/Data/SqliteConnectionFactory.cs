using Microsoft.Data.Sqlite;

namespace Yeetle.Infrastructure.Data;

// Every connection has foreign keys on and the database in WAL mode (CLAUDE.md).
public sealed class SqliteConnectionFactory(AppPaths paths)
{
    public string DatabasePath => paths.Database;

    public SqliteConnection Open() => Open(paths.Database, SqliteOpenMode.ReadWriteCreate);

    public static SqliteConnection Open(string file, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = mode,
            ForeignKeys = true,
            // A pooled connection keeps the file open, which would block restoring a backup.
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            using var wal = connection.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            // A damaged file fails here; the handle must not stay open, or a restore could not
            // move the file aside.
            connection.Dispose();
            throw;
        }
    }
}
