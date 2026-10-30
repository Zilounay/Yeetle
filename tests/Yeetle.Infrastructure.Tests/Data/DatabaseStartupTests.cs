using Dapper;
using Yeetle.Application.Data;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Tests.Data;

public class DatabaseStartupTests : IDisposable
{
    private readonly TestDataFolder _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_new_database_gets_the_latest_schema()
    {
        var check = _data.Startup.Prepare();

        Assert.Equal(DatabaseState.Ready, check.State);
        using var connection = _data.Connections.Open();
        Assert.Equal(Migrations.Latest, connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Clips';"));
    }

    [Fact]
    public void Connections_use_wal_and_foreign_keys()
    {
        _data.Startup.Prepare();

        using var connection = _data.Connections.Open();
        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode;"));
        Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA foreign_keys;"));
    }

    [Fact]
    public void Migrations_are_numbered_from_one_without_gaps() =>
        Assert.Equal(Enumerable.Range(1, Migrations.All.Count), Migrations.All.Select(m => m.Version));

    [Fact]
    public void Preparing_twice_keeps_the_data()
    {
        _data.Startup.Prepare();
        using (var connection = _data.Connections.Open())
            connection.Execute("INSERT INTO SourceFolders (Path, AddedAt) VALUES ('D:\\Clips', '2026-10-30');");

        Assert.Equal(DatabaseState.Ready, _data.Startup.Prepare().State);
        using var again = _data.Connections.Open();
        Assert.Equal(1, again.ExecuteScalar<int>("SELECT COUNT(*) FROM SourceFolders;"));
    }

    [Fact]
    public void An_upgrade_is_backed_up_first()
    {
        using (var connection = _data.Connections.Open())
            connection.Execute("CREATE TABLE Old (Id INTEGER);"); // an existing file at schema 0

        Assert.Equal(DatabaseState.Ready, _data.Startup.Prepare().State);

        Assert.Contains(_data.Backups.List(), b => b.Pool == BackupPool.Safety);
    }

    [Fact]
    public void A_newer_schema_is_refused_and_left_untouched()
    {
        var newer = Migrations.Latest + 1;
        using (var connection = _data.Connections.Open())
            connection.Execute($"PRAGMA user_version = {newer};");

        var check = _data.Startup.Prepare();

        Assert.Equal(DatabaseState.NewerSchema, check.State);
        Assert.Equal(newer, check.SchemaVersion);
        using var after = _data.Connections.Open();
        Assert.Equal(0, after.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master;"));
    }

    [Fact]
    public void A_damaged_file_is_reported_and_kept()
    {
        var garbage = Enumerable.Repeat((byte)0x5A, 4096).ToArray();
        File.WriteAllBytes(_data.Paths.Database, garbage);

        var check = _data.Startup.Prepare();

        Assert.Equal(DatabaseState.Damaged, check.State);
        Assert.Equal(garbage, File.ReadAllBytes(_data.Paths.Database));
    }
}
