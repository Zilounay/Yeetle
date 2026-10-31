namespace Yeetle.Application.Library;

public enum ScanTrigger
{
    Startup,
    SessionStart,
    // The window got focus again: at most one such scan per minute.
    Focus,
    Rescan,
}

// Decides when scans run (spec §4) and tells listeners what each one found.
public sealed class ScanScheduler(LibraryScanner scanner, TimeProvider clock)
{
    public static readonly TimeSpan FocusInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset? _lastScan;

    public event EventHandler<ScanReport>? Scanned;

    // Null when a focus scan was skipped because another ran less than a minute ago.
    public async Task<ScanReport?> RequestAsync(ScanTrigger trigger)
    {
        var now = clock.GetUtcNow();
        if (trigger == ScanTrigger.Focus && _lastScan is { } last && now - last < FocusInterval)
            return null;
        _lastScan = now;
        var report = await scanner.ScanAsync();
        Scanned?.Invoke(this, report);
        return report;
    }

    public async Task<ScanReport> CompleteFirstScanAsync(long folderId, FirstScanChoice choice)
    {
        var report = await scanner.CompleteFirstScanAsync(folderId, choice);
        Scanned?.Invoke(this, report);
        return report;
    }
}
