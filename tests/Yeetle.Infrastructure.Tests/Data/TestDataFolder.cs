using Yeetle.Application.Diagnostics;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Data;

namespace Yeetle.Infrastructure.Tests.Data;

// A throwaway %LOCALAPPDATA%\Yeetle for one test, with a clock the test controls.
public sealed class TestDataFolder : IDisposable
{
    public TestDataFolder()
    {
        Paths = new AppPaths(Path.Combine(Path.GetTempPath(), "YeetleTests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Paths.Root);
        Connections = new SqliteConnectionFactory(Paths);
        Backups = new SqliteBackups(Paths, BackupSettings, Log, Clock);
        Startup = new SqliteDatabaseStartup(Connections, Backups, Log);
    }

    public AppPaths Paths { get; }

    public ManualClock Clock { get; } = new();

    public BackupSettings BackupSettings { get; } = new();

    public NullLog Log { get; } = new();

    public SqliteConnectionFactory Connections { get; }

    public SqliteBackups Backups { get; }

    public SqliteDatabaseStartup Startup { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Paths.Root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }
}

public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 20, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public sealed class NullLog : ILog
{
    public List<string> Errors { get; } = [];

    public void Info(string message)
    {
    }

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}
