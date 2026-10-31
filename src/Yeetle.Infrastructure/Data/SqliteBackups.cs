using System.Globalization;
using Microsoft.Data.Sqlite;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Settings;

namespace Yeetle.Infrastructure.Data;

// Backups/<Pool>/yeetle-<time>.db (spec §10). Only the database is backed up, never videos.
public sealed class SqliteBackups(AppPaths paths, BackupSettings settings, ILog log, TimeProvider clock) : IBackups
{
    private const string Prefix = "yeetle-";
    private const string Extension = ".db";
    private const string TimeFormat = "yyyyMMdd-HHmmss-fff";
    private const string DamagedSuffix = ".damaged-";
    private const string WalSuffix = "-wal";
    private const string SharedMemorySuffix = "-shm";
    private const int MinimumKept = 1;

    public void Create(BackupPool pool)
    {
        var folder = PoolFolder(pool);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Prefix + Stamp() + Extension);
        using (var source = SqliteConnectionFactory.Open(paths.Database, SqliteOpenMode.ReadOnly))
        using (var destination = SqliteConnectionFactory.Open(target, SqliteOpenMode.ReadWriteCreate))
            source.BackupDatabase(destination);
        log.Info($"Backup created: {target}");
        Trim(pool);
    }

    public void CreateDailyIfDue()
    {
        if (!InPool(BackupPool.Daily).Any(b => b.CreatedAt.Date == Now.Date))
            Create(BackupPool.Daily);
    }

    public IReadOnlyList<BackupInfo> List() =>
        Enum.GetValues<BackupPool>().SelectMany(InPool).OrderByDescending(b => b.CreatedAt).ToList();

    public void Restore(BackupInfo backup)
    {
        var stamp = Stamp();
        // The database and its WAL side files are moved aside together; nothing is deleted.
        foreach (var file in DatabaseFiles().Where(File.Exists))
            File.Move(file, file + DamagedSuffix + stamp);
        File.Copy(backup.Path, paths.Database);
        log.Info($"Backup restored: {backup.Path}");
    }

    private DateTime Now => clock.GetLocalNow().DateTime;

    private string Stamp() => Now.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private IEnumerable<string> DatabaseFiles() =>
        [paths.Database, paths.Database + WalSuffix, paths.Database + SharedMemorySuffix];

    private string PoolFolder(BackupPool pool) => Path.Combine(paths.Backups, pool.ToString());

    private IEnumerable<BackupInfo> InPool(BackupPool pool)
    {
        var folder = PoolFolder(pool);
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, Prefix + "*" + Extension)
            .Select(f => (File: f, Time: ParseTime(f)))
            .Where(f => f.Time is not null)
            .Select(f => new BackupInfo(f.File, pool, f.Time!.Value))
            .ToList();
    }

    private static DateTime? ParseTime(string file) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file)[Prefix.Length..], TimeFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private void Trim(BackupPool pool)
    {
        var keep = pool switch
        {
            BackupPool.Session => settings.SessionCount,
            BackupPool.Daily => settings.DailyCount,
            _ => settings.SafetyCount,
        };
        foreach (var old in InPool(pool).OrderByDescending(b => b.CreatedAt).Skip(Math.Max(keep, MinimumKept)))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Error($"Old backup could not be removed: {old.Path}", e);
            }
        }
    }
}
