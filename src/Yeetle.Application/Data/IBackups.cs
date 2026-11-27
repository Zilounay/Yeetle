namespace Yeetle.Application.Data;

// Each pool keeps its own count, so one kind of backup never pushes out another (spec §10).
public enum BackupPool
{
    Session,
    Daily,
    // Before a migration, a tag delete, a wipe or a batch file operation.
    Safety,
}

public sealed record BackupInfo(string Path, BackupPool Pool, DateTime CreatedAt);

public interface IBackups
{
    // Copies the database with SQLite's own backup, safe while the app runs, then trims the pool.
    void Create(BackupPool pool);

    // One daily backup per calendar day.
    void CreateDailyIfDue();

    // Newest first.
    IReadOnlyList<BackupInfo> List();

    // Puts the backup in place of the database. The current file is renamed, never deleted.
    void Restore(BackupInfo backup);

    // Settings' restore (#20): takes a safety backup now and restores at the next start.
    void ScheduleRestore(BackupInfo backup);

    // Called first thing at startup; true when a scheduled restore was put in place.
    bool ApplyScheduledRestore();
}
