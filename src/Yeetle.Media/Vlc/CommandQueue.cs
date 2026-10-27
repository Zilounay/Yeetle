using System.Diagnostics;

namespace Yeetle.Media.Vlc;

// Runs player commands one at a time, in order, on the thread pool. LibVLC calls can block, so
// they must not run on the UI thread, and calling LibVLC from inside its own event handlers
// can deadlock, so handlers queue their work here too.
internal sealed class CommandQueue
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private bool _closed;

    public void Run(Action command)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _tail = _tail.ContinueWith(_ => Execute(command), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    // Runs the command after everything already queued and waits for it.
    public void RunAndWait(Action command, TimeSpan timeout)
    {
        Task tail;
        lock (_gate)
        {
            if (_closed)
                return;
            tail = _tail = _tail.ContinueWith(_ => Execute(command), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
        tail.Wait(timeout);
    }

    // Refuses new commands and waits for the queued ones to finish.
    public void Close(TimeSpan timeout)
    {
        Task tail;
        lock (_gate)
        {
            _closed = true;
            tail = _tail;
        }
        tail.Wait(timeout);
    }

    private static void Execute(Action command)
    {
        try
        {
            command();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Player command failed: {e}");
        }
    }
}
