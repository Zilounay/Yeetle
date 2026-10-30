using System.Globalization;
using Yeetle.Application.Diagnostics;

namespace Yeetle.Infrastructure.Diagnostics;

// One text file per day under Logs, the last few days kept. Logging never throws.
public sealed class FileLog : ILog
{
    private const int DaysKept = 14;
    private const string Prefix = "yeetle-";
    private const string Extension = ".log";
    private const string DayFormat = "yyyy-MM-dd";
    private const string LineTimeFormat = "HH:mm:ss.fff";
    private const string InfoLevel = "INFO";
    private const string ErrorLevel = "ERROR";

    private readonly string _folder;
    private readonly TimeProvider _clock;
    private readonly Lock _lock = new();

    public FileLog(AppPaths paths, TimeProvider clock)
    {
        _folder = paths.Logs;
        _clock = clock;
        TryRemoveOldFiles();
    }

    public void Info(string message) => Write(InfoLevel, message);

    public void Error(string message, Exception? exception = null) =>
        Write(ErrorLevel, exception is null ? message : message + Environment.NewLine + exception);

    private void Write(string level, string message)
    {
        var now = _clock.GetLocalNow();
        var file = Path.Combine(_folder, Prefix + now.ToString(DayFormat, CultureInfo.InvariantCulture) + Extension);
        var line = $"{now.ToString(LineTimeFormat, CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(file, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nowhere left to report it.
            }
        }
    }

    private void TryRemoveOldFiles()
    {
        try
        {
            if (!Directory.Exists(_folder))
                return;
            // The day format sorts by date.
            foreach (var old in Directory.EnumerateFiles(_folder, Prefix + "*" + Extension)
                         .OrderDescending(StringComparer.Ordinal).Skip(DaysKept))
                File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Old logs are only clutter.
        }
    }
}
