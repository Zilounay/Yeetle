using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Services;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Settings;

namespace Yeetle.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IHotkeyTarget
{
    private readonly ScanScheduler _scans;
    private readonly IDialogs _dialogs;
    private readonly FileOperations _files;
    // First-scan questions, asked one at a time (spec §4). A folder is in _queued from the moment
    // a scan reports it until its question closes, so a second scan does not ask again meanwhile.
    private readonly Queue<PendingFirstScan> _questions = new();
    private readonly HashSet<long> _queued = [];
    // Folders already answered: a scan report from before the answer may still list them.
    private readonly HashSet<long> _answered = [];
    private bool _isAsking;

    // What the last scan found, e.g. files still recording.
    [ObservableProperty]
    private string _scanSummary = string.Empty;

    public MainWindowViewModel(PlayerViewModel player, SessionViewModel session, ScanScheduler scans, IDialogs dialogs,
        LoadedSettings settings, FileOperations files)
    {
        _files = files;
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
    public ICommand? FindCommand(KeyPress press) =>
        Hotkeys.Find(press) is { } action ? Actions[action] : Session.Tags.FindTagCommand(press);

    // File operations cut short by a crash are checked first (spec §9), then a scan runs (spec §4),
    // then an interrupted session is offered back (spec §5).
    public async Task StartAsync()
    {
        await _files.RecoverInterruptedAsync();
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

    [RelayCommand]
    private void OpenTagManager() => _dialogs.ShowTagManager();

    private async Task OnScannedAsync(ScanReport report)
    {
        ScanSummary = ScanNotice.Describe(report);
        foreach (var pending in report.PendingFirstScans.Where(p => !_answered.Contains(p.Folder.Id) && _queued.Add(p.Folder.Id)))
            _questions.Enqueue(pending);
        await AskQueuedAsync();
        await Session.RefreshHomeAsync();
    }

    // Closing a question without answering asks again on the next scan.
    private async Task AskQueuedAsync()
    {
        if (_isAsking)
            return;
        _isAsking = true;
        try
        {
            while (_questions.TryDequeue(out var pending))
            {
                var folderId = pending.Folder.Id;
                try
                {
                    if (await _dialogs.AskFirstScanAsync(pending) is not { } choice)
                        continue;
                    _answered.Add(folderId);
                    await _scans.CompleteFirstScanAsync(folderId, choice);
                }
                finally
                {
                    _queued.Remove(folderId);
                }
            }
        }
        finally
        {
            _isAsking = false;
        }
    }
}
