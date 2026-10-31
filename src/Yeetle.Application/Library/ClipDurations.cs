using Yeetle.Application.Diagnostics;
using Yeetle.Application.Playback;

namespace Yeetle.Application.Library;

// Fills in clip durations after scans, in the background, so the scan itself stays fast
// (spec §4). One file at a time, and never during a scan or a file operation, which could
// otherwise find the file open.
public sealed class ClipDurations(ILibraryStore store, IDurationReader reader, LibraryScanner scanner, ILog log)
{
    // Stored for a file that could not be read, so it is not tried again after every scan.
    public const long Unreadable = 0;

    // Clips read between two looks at the database.
    private const int BatchSize = 50;

    private const int Idle = 0;
    private const int Running = 1;

    private int _state = Idle;

    // Raised on a background thread after each batch.
    public event EventHandler? Updated;

    // Returns at once when a run is already going; that run picks up new clips too.
    public async Task FillMissingAsync()
    {
        if (Interlocked.Exchange(ref _state, Running) == Running)
            return;
        try
        {
            while (await Task.Run(() => store.ClipsWithoutDuration(BatchSize)) is { Count: > 0 } clips)
            {
                foreach (var clip in clips)
                    await scanner.WhileNotScanningAsync(() => ReadOne(clip));
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Data.Common.DbException)
        {
            log.Error("Clip durations could not be filled in.", e);
        }
        finally
        {
            Interlocked.Exchange(ref _state, Idle);
        }
    }

    private bool ReadOne(ClipFile clip)
    {
        TimeSpan? duration;
        try
        {
            duration = reader.Read(clip.FullPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Error($"Duration could not be read: {clip.FullPath}", e);
            duration = null;
        }
        store.SetDuration(clip.Id, duration is { } value ? (long)value.TotalMilliseconds : Unreadable);
        return true;
    }
}
