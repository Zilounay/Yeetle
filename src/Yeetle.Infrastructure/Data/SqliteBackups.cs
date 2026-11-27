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
    // The database a chosen restore replaced, kept beside the new one.
    private const string ReplacedSuffix = ".replaced-";
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

    public void Restore(BackupInfo backup) => PutInPlace(backup.Path, DamagedSuffix);

    // A restore chosen in Settings (#20) waits for the next start, when nothing has the database
    // open yet. A safety backup of the current database is taken first, so it can be restored too.
    public void ScheduleRestore(BackupInfo backup)
    {
        Create(BackupPool.Safety);
        File.WriteAllText(paths.RestorePending, backup.Path);
        log.Info($"Restore scheduled for the next start: {backup.Path}");
    }

    public bool ApplyScheduledRestore()
    {
        if (!File.Exists(paths.RestorePending))
            return false;
        var backup = File.ReadAllText(paths.RestorePending).Trim();
        // The request is used up whatever happens, so a bad one never comes back at every start.
        File.Delete(paths.RestorePending);
        if (!File.Exists(backup))
        {
            log.Error($"Scheduled restore skipped: {backup} is gone.");
            return false;
        }
        PutInPlace(backup, ReplacedSuffix);
        return true;
    }

    // The database and its WAL side files are moved aside together; nothing is deleted.
    private void PutInPlace(string backup, string asideSuffix)
    {
        var stamp = Stamp();
        foreach (var file in DatabaseFiles().Where(File.Exists))
            File.Move(file, file + asideSuffix + stamp);
        File.Copy(backup, paths.Database);
        log.Info($"Backup restored: {backup}");
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
