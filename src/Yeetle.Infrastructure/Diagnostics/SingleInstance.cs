using System.Security.Cryptography;
using System.Text;
using Yeetle.Application.Diagnostics;

namespace Yeetle.Infrastructure.Diagnostics;

// Only one Yeetle runs per data folder (spec §10): a second launch signals the first, which brings
// its window forward, and then exits. The names carry a hash of the folder, so a test copy on a
// throwaway folder (AppPaths.DataFolderVariable) can run beside the real one.
public sealed class SingleInstance : IAppActivation, IDisposable
{
    private const string MutexPrefix = @"Local\Yeetle.Instance.";
    private const string SignalPrefix = @"Local\Yeetle.Activate.";
    // Enough of the hash to tell folders apart.
    private const int KeyLength = 16;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle? _wait;

    public SingleInstance(string dataFolder)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataFolder.ToUpperInvariant())))[..KeyLength];
        _mutex = new Mutex(initiallyOwned: true, MutexPrefix + key, out var createdNew);
        _signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, SignalPrefix + key);
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
