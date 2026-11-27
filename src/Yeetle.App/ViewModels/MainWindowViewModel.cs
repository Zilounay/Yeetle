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
using Yeetle.Application.Updates;

namespace Yeetle.App.ViewModels;

public enum Page
{
    Session,
    Explorer,
    // A clip opened from the Explorer.
    Editor,
}

public sealed partial class MainWindowViewModel : ViewModelBase, IHotkeyTarget
{
    private readonly ScanScheduler _scans;
    private readonly IDialogs _dialogs;
    private readonly FileOperations _files;
    private readonly ClipDurations _durations;
    private readonly UpdateChecker _updates;
    private readonly LoadedSettings _settings;
    // The last scan's report, so its summary can be written again in another language.
    private ScanReport? _lastScan;
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

    // What each page does with the hotkey actions of the settings file.
    private readonly Dictionary<string, ICommand> _sessionActions;
    private readonly Dictionary<string, ICommand> _editorActions;
    private readonly Dictionary<string, ICommand> _explorerActions;

    public MainWindowViewModel(PlayerViewModel player, SessionViewModel session, ExplorerViewModel explorer,
        EditorViewModel editor, ScanScheduler scans, IDialogs dialogs, LoadedSettings settings, FileOperations files,
        ClipDurations durations, ClipLibrary library, UpdateChecker updates, SettingsService settingsService)
    {
        _files = files;
        _durations = durations;
        _settings = settings;
        _updates = updates;
        Player = player;
        Session = session;
        Explorer = explorer;
        Editor = editor;
        _scans = scans;
        _dialogs = dialogs;
        _sessionActions = new Dictionary<string, ICommand>(player.Actions.Concat(session.Actions))
        {
            [HotkeyActions.Rescan] = RescanCommand,
            [HotkeyActions.TheatreMode] = ToggleTheatreCommand,
            [HotkeyActions.Settings] = OpenSettingsCommand,
        };
        _editorActions = new Dictionary<string, ICommand>(player.Actions.Concat(editor.Actions))
        {
            [HotkeyActions.Rescan] = RescanCommand,
            [HotkeyActions.TheatreMode] = ToggleTheatreCommand,
            [HotkeyActions.Settings] = OpenSettingsCommand,
        };
        // In the list, the Keep and Yeet keys (Enter, Del) open and delete, as in Windows Explorer.
        _explorerActions = new Dictionary<string, ICommand>
        {
            [HotkeyActions.Keep] = explorer.OpenCommand,
            [HotkeyActions.Yeet] = explorer.DeleteCommand,
            [HotkeyActions.MarkerWindow] = explorer.ShowMarkersCommand,
            [HotkeyActions.Rescan] = RescanCommand,
            [HotkeyActions.Settings] = OpenSettingsCommand,
        };
        _knownActions = _sessionActions.Keys.Concat(_editorActions.Keys).Concat(_explorerActions.Keys).ToHashSet();
        _hotkeys = new HotkeyMap(settings.Values.Hotkeys, _knownActions);
        // Keys changed in Settings work at once (#20).
        settingsService.Changed += (_, _) => Hotkeys = new HotkeyMap(settings.Values.Hotkeys, _knownActions);
        var problems = _hotkeys.Problems.Prepend(settings.Problem).OfType<string>().ToList();
        if (problems.Count > 0)
            player.ShowSettingsProblem(settings.Location, problems);
        _scans.Scanned += (_, report) => Dispatcher.UIThread.Post(() => _ = OnScannedAsync(report));
        _durations.Updated += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshExplorerAsync());
        player.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasInfo));
        session.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasInfo));
            OnLayoutChanged();
            if (e.PropertyName is nameof(SessionViewModel.IsActive) or nameof(SessionViewModel.NewCount))
            {
                OnPropertyChanged(nameof(SessionTabText));
                ShowExplorerCommand.NotifyCanExecuteChanged();
            }
        };
        editor.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasInfo));
            if (e.PropertyName is nameof(EditorViewModel.IsOpen) or nameof(EditorViewModel.IsFilePresent))
                OnLayoutChanged();
        };
        // Decisions changed or wiped in the Explorer change how many clips wait for a session.
        library.Changed += (_, _) => _ = Session.RefreshHomeAsync();
        explorer.OpenRequested += (_, clip) => _ = OpenClipAsync(clip);
        editor.Closed += (_, _) => _ = ShowExplorerAsync();
        _isTheatre = settings.Values.Player.TheatreMode;
        Resources.Loc.Instance.Changed += (_, _) => Relocalize();
    }

    // After a language switch (#20): every page makes its texts again.
    private void Relocalize()
    {
        if (_lastScan is { } report)
            ScanSummary = ScanNotice.Describe(report);
        Player.Relocalize();
        Session.Relocalize();
        Explorer.Relocalize();
        Editor.Relocalize();
        OnPropertyChanged(string.Empty);
    }

    // Whether the info block over the video has anything to show.
    public bool HasInfo => Session.IsActive || IsEditorPage || Player.Message.Length > 0 || Session.Notice.Length > 0
                           || ScanSummary.Length > 0;

    partial void OnScanSummaryChanged(string value)
    {
        OnPropertyChanged(nameof(HasInfo));
        OnPropertyChanged(nameof(ShowScanSummary));
    }

    // Theatre mode (spec §7): the video takes the window; tags, markers and the strip step aside.
    [ObservableProperty]
    private bool _isTheatre;

    partial void OnIsTheatreChanged(bool value) => OnLayoutChanged();

    // The session screen (home or review), the Explorer list, or a clip opened from it.
    [ObservableProperty]
    private Page _currentPage = Page.Session;

    partial void OnCurrentPageChanged(Page value) => OnLayoutChanged();

    public bool IsSessionPage => CurrentPage == Page.Session;

    public bool IsExplorerPage => CurrentPage == Page.Explorer;

    public bool IsEditorPage => CurrentPage == Page.Editor;

    // The video area: on every page but the list. It must be in the layout from the start, or its
    // native windows are never created and VLC opens a window of its own.
    public bool ShowPlayer => !IsExplorerPage;

    // On the home page the video area is collapsed to nothing, which hides its native windows, and a
    // page of its own shows instead of a black video area with a message over it (#62).
    public bool CollapsePlayer => ShowHome;

    // Tags and markers only for a clip that can play (#79).
    // Theatre mode keeps the clip's tags in sight over the video, with the full panel a hover away (#95).
    public bool ShowTheatreTags => IsTheatre && ShowControls;

    public bool ShowSidePanels => !IsTheatre && ShowControls;

    // The clip strip is hidden for now: it added little (#62). Its code stays for a later return.
    public bool ShowStrip => false;

    public bool ShowHome => IsSessionPage && Session.IsHome;

    public bool ShowSessionNotice => IsSessionPage && Session.Notice.Length > 0;

    public bool ShowScanSummary => ScanSummary.Length > 0;

    // The window over the video: while a session runs or a clip is open.
    public bool ShowOverlay => IsEditorOpen || (IsSessionPage && Session.IsActive);

    // The playback and decision controls: not for a clip whose file is not on disk (#79), which
    // only shows why and Back to library.
    public bool ShowControls => (IsEditorOpen && Editor.IsFilePresent) || (IsSessionPage && Session.IsActive);

    // The editor page shows at once; its clip follows a moment later (#78).
    private bool IsEditorOpen => IsEditorPage && Editor.IsOpen;

    // The Tags and Markers panels follow the session or the opened clip.
    public TagPanelViewModel SideTags => IsEditorPage ? Editor.Tags : Session.Tags;

    public MarkersPanelViewModel SideMarkers => IsEditorPage ? Editor.Markers : Session.Markers;

    public string SessionTabText => Session.NewCount > 0 && !Session.IsActive
        ? string.Format(Resources.Strings.TabSessionNew, Session.NewCount)
        : Resources.Strings.TabSession;

    public PlayerViewModel Player { get; }

    public SessionViewModel Session { get; }

    public ExplorerViewModel Explorer { get; }

    public EditorViewModel Editor { get; }

    [ObservableProperty]
    private HotkeyMap _hotkeys;

    private readonly HashSet<string> _knownActions;

    // Actions from the settings file first, as the current page understands them, then the tag
    // hotkeys of a running session or an opened clip.
    public ICommand? FindCommand(KeyPress press)
    {
        var actions = CurrentPage switch
        {
            Page.Explorer => _explorerActions,
            Page.Editor => _editorActions,
            _ => _sessionActions,
        };
        if (Hotkeys.Find(press) is { } action && actions.TryGetValue(action, out var command))
            return command;
        return CurrentPage switch
        {
            Page.Session => Session.Tags.FindTagCommand(press),
            Page.Editor => Editor.Tags.FindTagCommand(press),
            _ => null,
        };
    }

    // File operations cut short by a crash are checked first (spec §9), then a scan runs (spec §4),
    // then an interrupted session is offered back (spec §5). The update check runs alongside and
    // never delays any of it (spec §12).
    public async Task StartAsync()
    {
        if (_settings.Values.General.CheckForUpdatesAtStartup)
            _ = CheckForUpdateAsync();
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

    // One-click updates (spec §12): the top bar offers the version found at startup.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText), nameof(UpdateTip))]
    private AvailableUpdate? _update;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private int _updateProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText), nameof(UpdateTip))]
    private bool _updateFailed;

    public bool HasUpdate => Update is not null;

    public string UpdateText => Update is not { } update
        ? string.Empty
        : IsDownloadingUpdate
            ? string.Format(Resources.Strings.UpdateDownloading, UpdateProgress)
            : UpdateFailed
                ? Resources.Strings.UpdateFailed
                : string.Format(Resources.Strings.UpdateAvailable, update.Version);

    public string UpdateTip => Update is not { } update
        ? string.Empty
        : string.Format(UpdateFailed ? Resources.Strings.UpdateFailedTip : Resources.Strings.UpdateTip, update.Version);

    private async Task CheckForUpdateAsync() => Update = await _updates.CheckAsync();

    // Settings' Check now (#20): a version found also shows the update button.
    public async Task<UpdateCheck> CheckForUpdateNowAsync()
    {
        var check = await _updates.CheckNowAsync();
        if (check.Update is { } update)
            Update = update;
        return check;
    }

    // A running session is ended first, as when closing; then the update downloads and Yeetle
    // restarts into it.
    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (Update is not { } update || !await Session.ConfirmUpdateAsync())
            return;
        UpdateFailed = false;
        UpdateProgress = 0;
        IsDownloadingUpdate = true;
        var downloaded = await _updates.DownloadAsync(update,
            percent => Dispatcher.UIThread.Post(() => UpdateProgress = percent));
        if (downloaded)
        {
            // The player lets go of its files before Yeetle closes.
            Player.Stop();
            _updates.RestartIntoUpdate(update);
            return;
        }
        IsDownloadingUpdate = false;
        UpdateFailed = true;
    }

    private bool CanInstallUpdate() => !IsDownloadingUpdate;

    [RelayCommand]
    private void ToggleTheatre() => IsTheatre = !IsTheatre;

    // Every page switch shows the new page first; closing the clip and loading follow (#78).
    [RelayCommand]
    private async Task ShowSessionAsync()
    {
        var leaving = IsEditorPage ? Editor.LeaveAsync() : Task.CompletedTask;
        CurrentPage = Page.Session;
        await leaving;
        await Session.RefreshHomeAsync();
    }

    // The Explorer is unavailable while a session runs (spec §5).
    [RelayCommand(CanExecute = nameof(CanShowExplorer))]
    private Task ShowExplorerAsync() => ShowExplorerPageAsync();

    private bool CanShowExplorer() => !Session.IsActive;

    // The list on screen stays until the new one is read; a clip just closed is saved first, so the
    // list shows its last changes.
    private async Task ShowExplorerPageAsync()
    {
        CurrentPage = Page.Explorer;
        await Editor.Closing;
        await Explorer.LoadAsync();
    }

    private async Task OpenClipAsync(Domain.Clips.ClipId clip)
    {
        CurrentPage = Page.Editor;
        if (!await Editor.OpenAsync(clip))
            await ShowExplorerPageAsync();
    }

    private Task RefreshExplorerAsync() => IsExplorerPage ? Explorer.LoadAsync() : Task.CompletedTask;

    private void OnLayoutChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(IsSessionPage), nameof(IsExplorerPage), nameof(IsEditorPage), nameof(ShowPlayer), nameof(CollapsePlayer),
                     nameof(ShowSidePanels), nameof(ShowStrip), nameof(ShowHome), nameof(ShowOverlay), nameof(ShowControls), nameof(ShowTheatreTags),
                     nameof(ShowSessionNotice), nameof(SideTags), nameof(SideMarkers), nameof(HasInfo),
                 })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private Task RescanAsync() => _scans.RequestAsync(ScanTrigger.Rescan);

    [RelayCommand]
    private Task OpenFoldersAsync() => _dialogs.ShowSourceFoldersAsync();

    [RelayCommand]
    private void OpenTagManager() => _dialogs.ShowTagManager();

    [RelayCommand]
    private void OpenSettings() => _dialogs.ShowSettings();

    private async Task OnScannedAsync(ScanReport report)
    {
        _lastScan = report;
        ScanSummary = ScanNotice.Describe(report);
        // Durations of new clips are read in the background (spec §4).
        _ = _durations.FillMissingAsync();
        foreach (var pending in report.PendingFirstScans.Where(p => !_answered.Contains(p.Folder.Id) && _queued.Add(p.Folder.Id)))
            _questions.Enqueue(pending);
        await AskQueuedAsync();
        await Session.RefreshHomeAsync();
        await RefreshExplorerAsync();
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
