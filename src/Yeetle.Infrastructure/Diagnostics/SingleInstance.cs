using Yeetle.Application.Diagnostics;

namespace Yeetle.Infrastructure.Diagnostics;

// Only one Yeetle runs per Windows session (spec §10): a second launch signals the first, which
// brings its window forward, and then exits.
public sealed class SingleInstance : IAppActivation, IDisposable
{
    private const string MutexName = @"Local\Yeetle.Instance";
    private const string SignalName = @"Local\Yeetle.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle? _wait;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, SignalName);
        IsFirst = createdNew;
        if (IsFirst)
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal,
                (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public bool IsFirst { get; }

    public event EventHandler? ActivationRequested;

    // Called by a second launch.
    public void ActivateFirst() => _signal.Set();

    public void Dispose()
    {
        _wait?.Unregister(null);
        _signal.Dispose();
        if (IsFirst)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
