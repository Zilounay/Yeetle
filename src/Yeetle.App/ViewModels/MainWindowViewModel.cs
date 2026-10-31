using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Services;
using Yeetle.Application.Library;
using Yeetle.Application.Settings;

namespace Yeetle.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IHotkeyTarget
{
    private readonly ScanScheduler _scans;
    private readonly IDialogs _dialogs;
    // Folders whose first-scan question is open, so a second scan does not ask again meanwhile.
    private readonly HashSet<long> _asking = [];

    public MainWindowViewModel(PlayerViewModel player, ScanScheduler scans, IDialogs dialogs, LoadedSettings settings)
    {
        Player = player;
        _scans = scans;
        _dialogs = dialogs;
        Actions = new Dictionary<string, ICommand>(player.Actions)
        {
            [HotkeyActions.Rescan] = RescanCommand,
        };
        Hotkeys = new HotkeyMap(settings.Values.Hotkeys, Actions.Keys.ToHashSet());
        var problems = Hotkeys.Problems.Prepend(settings.Problem).OfType<string>().ToList();
        if (problems.Count > 0)
            player.ShowSettingsProblem(settings.Location, problems);
        _scans.Scanned += (_, report) => Dispatcher.UIThread.Post(() => _ = OnScannedAsync(report));
    }

    public PlayerViewModel Player { get; }

    public HotkeyMap Hotkeys { get; }

    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    // Scans run at start and when the window gets focus back (spec §4).
    public Task StartAsync() => _scans.RequestAsync(ScanTrigger.Startup);

    public void OnActivated() => _ = _scans.RequestAsync(ScanTrigger.Focus);

    // Called when the window is about to close, while its video windows still exist.
    public void OnClosing() => Player.Stop();

    [RelayCommand]
    private Task RescanAsync() => _scans.RequestAsync(ScanTrigger.Rescan);

    [RelayCommand]
    private Task OpenFoldersAsync() => _dialogs.ShowSourceFoldersAsync();

    private async Task OnScannedAsync(ScanReport report)
    {
        Player.Notice = ScanNotice.Describe(report);
        foreach (var pending in report.PendingFirstScans.Where(p => _asking.Add(p.Folder.Id)))
        {
            try
            {
                if (await _dialogs.AskFirstScanAsync(pending) is { } choice)
                    await _scans.CompleteFirstScanAsync(pending.Folder.Id, choice);
            }
            finally
            {
                _asking.Remove(pending.Folder.Id);
            }
        }
    }
}
