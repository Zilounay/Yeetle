using Dapper;
using Yeetle.Application.Data;

namespace Yeetle.Infrastructure.Tests.Data;

public class BackupTests : IDisposable
{
    private readonly TestDataFolder _data = new();

    public BackupTests() => _data.Startup.Prepare();

    public void Dispose() => _data.Dispose();

    private void Tick() => _data.Clock.Now = _data.Clock.Now.AddSeconds(1);

    [Fact]
    public void Each_pool_keeps_its_own_count()
    {
        for (var i = 0; i < _data.BackupSettings.SessionCount + 2; i++)
        {
            Tick();
            _data.Backups.Create(BackupPool.Session);
        }
        Tick();
        _data.Backups.Create(BackupPool.Safety);

        var backups = _data.Backups.List();
        Assert.Equal(_data.BackupSettings.SessionCount, backups.Count(b => b.Pool == BackupPool.Session));
        Assert.Single(backups, b => b.Pool == BackupPool.Safety);
        Assert.Single(backups, b => b.Pool == BackupPool.Daily); // made by Prepare
    }

    [Fact]
    public void Trimming_removes_the_oldest()
    {
        _data.BackupSettings.SessionCount = 1;
        _data.Backups.Create(BackupPool.Session);
        Tick();
        _data.Backups.Create(BackupPool.Session);

        Assert.Equal(_data.Clock.Now.DateTime, _data.Backups.List().Single(b => b.Pool == BackupPool.Session).CreatedAt);
    }

    [Fact]
    public void Only_one_daily_backup_per_day()
    {
        Tick();
        _data.Backups.CreateDailyIfDue();
        Assert.Single(_data.Backups.List(), b => b.Pool == BackupPool.Daily);

        _data.Clock.Now = _data.Clock.Now.AddDays(1);
        _data.Backups.CreateDailyIfDue();
        Assert.Equal(2, _data.Backups.List().Count(b => b.Pool == BackupPool.Daily));
    }

    [Fact]
    public void Restore_brings_back_the_data_and_keeps_the_current_file()
    {
        using (var connection = _data.Connections.Open())
            connection.Execute("INSERT INTO SourceFolders (Path, AddedAt) VALUES ('D:\\Before', '2026-10-30');");
        Tick();
        _data.Backups.Create(BackupPool.Session);
        using (var connection = _data.Connections.Open())
            connection.Execute("INSERT INTO SourceFolders (Path, AddedAt) VALUES ('D:\\After', '2026-10-30');");

        Tick();
        _data.Backups.Restore(_data.Backups.List().First(b => b.Pool == BackupPool.Session));

        using var restored = _data.Connections.Open();
        Assert.Equal(["D:\\Before"], restored.Query<string>("SELECT Path FROM SourceFolders;"));
        Assert.Contains(Directory.EnumerateFiles(_data.Paths.Root), f => f.Contains(".damaged-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_damaged_database_can_be_replaced_by_a_backup()
    {
        var backup = _data.Backups.List().Single();
        File.WriteAllBytes(_data.Paths.Database, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
        Assert.Equal(DatabaseState.Damaged, _data.Startup.Prepare().State);

        Tick();
        _data.Backups.Restore(backup);

        Assert.Equal(DatabaseState.Ready, _data.Startup.Prepare().State);
    }
}
