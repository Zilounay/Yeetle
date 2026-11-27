using Yeetle.Application.Diagnostics;

namespace Yeetle.Application.Settings;

// The settings while Yeetle runs (spec §11, decided in #20): a change applies at once, everywhere,
// and is written to settings.json in the background, with no OK or Cancel. Every part of the app
// holds the same AppSettings object, so most settings simply take effect the next time they are
// read; Changed tells the parts that build something from them (hotkeys, language) to rebuild.
public sealed class SettingsService(LoadedSettings loaded, ISettingsStore store, ILog log)
{
    private readonly SemaphoreSlim _writing = new(1, 1);
    private Task _lastWrite = Task.CompletedTask;

    public AppSettings Values => loaded.Values;

    // Where the file is, for the Settings window and messages.
    public string Location => loaded.Location;

    // Why the last save failed; empty once a save succeeds.
    public string SaveProblem { get; private set; } = string.Empty;

    // Raised on the caller's thread after each change.
    public event EventHandler? Changed;

    // Applies the change, then saves. Called on the UI thread, so the text is taken before
    // anything else can change the settings; writes then happen one after another, in order.
    public Task ChangeAsync(Action<AppSettings> change)
    {
        change(Values);
        Changed?.Invoke(this, EventArgs.Empty);
        var text = store.Format(Values);
        return _lastWrite = WriteAsync(text);
    }

    // Completes once every change made so far is on disk.
    public Task FlushAsync() => _lastWrite;

    private async Task WriteAsync(string text)
    {
        await _writing.WaitAsync();
        try
        {
            await Task.Run(() => store.Write(text));
            SaveProblem = string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveProblem = e.Message;
            log.Error("Settings could not be saved.", e);
        }
        finally
        {
            _writing.Release();
        }
    }
}
