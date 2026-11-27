using Yeetle.Application.Diagnostics;
using Yeetle.Application.Updates;

namespace Yeetle.Application.Tests.Updates;

public class UpdateCheckerTests
{
    private static readonly AvailableUpdate Newer = new("0.0.2");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly FakeUpdates _updates = new();
    private readonly RecordingLog _log = new();
    private readonly UpdateChecker _checker;

    public UpdateCheckerTests() => _checker = new UpdateChecker(_updates, _log);

    [Fact]
    public async Task Finds_the_newer_version()
    {
        _updates.Found = Newer;

        Assert.Equal(Newer, await _checker.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Does_not_check_when_not_installed()
    {
        _updates.IsInstalled = false;
        _updates.Found = Newer;

        Assert.Null(await _checker.CheckAsync(Cancellation));
        Assert.Equal(0, _updates.Checks);
    }

    [Fact]
    public async Task A_failed_check_finds_nothing_and_is_logged()
    {
        _updates.CheckFailure = new HttpRequestException("offline");

        Assert.Null(await _checker.CheckAsync(Cancellation));
        Assert.Single(_log.Errors);
    }

    [Fact]
    public async Task A_cancelled_check_is_not_swallowed()
    {
        _updates.CheckFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _checker.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Download_reports_success_and_progress()
    {
        var reported = new List<int>();

        Assert.True(await _checker.DownloadAsync(Newer, reported.Add, Cancellation));
        Assert.Equal([50, 100], reported);
    }

    [Fact]
    public async Task A_failed_download_returns_false_and_is_logged()
    {
        _updates.DownloadFailure = new IOException("disk full");

        Assert.False(await _checker.DownloadAsync(Newer, _ => { }, Cancellation));
        Assert.Single(_log.Errors);
    }

    private sealed class FakeUpdates : IAppUpdates
    {
        public bool IsInstalled { get; set; } = true;

        public AvailableUpdate? Found { get; set; }

        public Exception? CheckFailure { get; set; }

        public Exception? DownloadFailure { get; set; }

        public int Checks { get; private set; }

        public Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation = default)
        {
            Checks++;
            return CheckFailure is { } failure ? Task.FromException<AvailableUpdate?>(failure) : Task.FromResult(Found);
        }

        public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation = default)
        {
            if (DownloadFailure is { } failure)
                return Task.FromException(failure);
            progress(50);
            progress(100);
            return Task.CompletedTask;
        }

        public void RestartIntoUpdate(AvailableUpdate update)
        {
        }
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Errors { get; } = [];

        public void Info(string message)
        {
        }

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
