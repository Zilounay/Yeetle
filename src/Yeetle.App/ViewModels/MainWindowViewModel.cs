using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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

    // What the last scan found, e.g. files still recording.
    [ObservableProperty]
    private string _scanSummary = string.Empty;

    public MainWindowViewModel(PlayerViewModel player, SessionViewModel session, ScanScheduler scans, IDialogs dialogs,
        LoadedSettings settings)
    {
        Player = player;
        Session = session;
        _scans = scans;
        _dialogs = dialogs;
        Actions = new Dictionary<string, ICommand>(player.Actions.Concat(session.Actions))
        {
            [HotkeyActions.Rescan] = RescanCommand,
        };
        Hotkeys = new HotkeyMap(settings.Values.Hotkeys, Actions.Keys.ToHashSet());
        var problems = Hotkeys.Problems.Prepend(settings.Problem).OfType<string>().ToList();
        if (problems.Count > 0)
            player.ShowSettingsProblem(settings.Location, problems);
        _scans.Scanned += (_, report) => Dispatcher.UIThread.Post(() => _ = OnScannedAsync(report));
        player.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasInfo));
        session.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasInfo));
    }

    // Whether the info block over the video has anything to show.
    public bool HasInfo => Session.IsActive || Player.Message.Length > 0 || Session.Notice.Length > 0 || ScanSummary.Length > 0;

    partial void OnScanSummaryChanged(string value) => OnPropertyChanged(nameof(HasInfo));

    public PlayerViewModel Player { get; }

    public SessionViewModel Session { get; }

    public HotkeyMap Hotkeys { get; }

    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    // Actions from the settings file first, then the tag hotkeys of a running session.
    public ICommand? FindCommand(KeyModifiers modifiers, PhysicalKey key, string? symbol) =>
        Hotkeys.Find(modifiers, key, symbol) is { } action ? Actions[action] : Session.Tags.FindTagCommand(modifiers, key, symbol);

    // A scan at start (spec §4), then an interrupted session is offered back (spec §5).
    public async Task StartAsync()
    {
        await _scans.RequestAsync(ScanTrigger.Startup);
        await Session.OfferUnfinishedAsync();
        await Session.RefreshHomeAsync();
    }

    public void OnActivated() => _ = _scans.RequestAsync(ScanTrigger.Focus);

    // False keeps the window open.
    public Task<bool> ConfirmCloseAsync() => Session.ConfirmCloseAsync();

    // Called when the window is about to close, while its video windows still exist.
    public void OnClosing() => Player.Stop();

    [RelayCommand]
    private Task RescanAsync() => _scans.RequestAsync(ScanTrigger.Rescan);

    [RelayCommand]
    private Task OpenFoldersAsync() => _dialogs.ShowSourceFoldersAsync();

    private async Task OnScannedAsync(ScanReport report)
    {
        ScanSummary = ScanNotice.Describe(report);
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
        await Session.RefreshHomeAsync();
    }
}
