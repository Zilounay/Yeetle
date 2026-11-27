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
        ClipDurations durations, ClipLibrary library)
    {
        _files = files;
        _durations = durations;
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
        };
        _editorActions = new Dictionary<string, ICommand>(player.Actions.Concat(editor.Actions))
        {
            [HotkeyActions.Rescan] = RescanCommand,
            [HotkeyActions.TheatreMode] = ToggleTheatreCommand,
        };
        // In the list, the Keep and Yeet keys (Enter, Del) open and delete, as in Windows Explorer.
        _explorerActions = new Dictionary<string, ICommand>
        {
            [HotkeyActions.Keep] = explorer.OpenCommand,
            [HotkeyActions.Yeet] = explorer.DeleteCommand,
            [HotkeyActions.MarkerWindow] = explorer.ShowMarkersCommand,
            [HotkeyActions.Rescan] = RescanCommand,
        };
        var known = _sessionActions.Keys.Concat(_editorActions.Keys).Concat(_explorerActions.Keys).ToHashSet();
        Hotkeys = new HotkeyMap(settings.Values.Hotkeys, known);
        var problems = Hotkeys.Problems.Prepend(settings.Problem).OfType<string>().ToList();
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
        editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasInfo));
        // Decisions changed or wiped in the Explorer change how many clips wait for a session.
        library.Changed += (_, _) => _ = Session.RefreshHomeAsync();
        explorer.OpenRequested += (_, clip) => _ = OpenClipAsync(clip);
        editor.Closed += (_, _) => _ = ShowExplorerAsync();
        _isTheatre = settings.Values.Player.TheatreMode;
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

    public bool ShowSidePanels => !IsTheatre && (IsEditorPage || (IsSessionPage && Session.IsActive));

    // The clip strip is hidden for now: it added little (#62). Its code stays for a later return.
    public bool ShowStrip => false;

    public bool ShowHome => IsSessionPage && Session.IsHome;

    public bool ShowSessionNotice => IsSessionPage && Session.Notice.Length > 0;

    public bool ShowScanSummary => ScanSummary.Length > 0;

    // The playback controls: while a session runs or a clip is open.
    public bool ShowControls => IsEditorPage || (IsSessionPage && Session.IsActive);

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

    public HotkeyMap Hotkeys { get; }

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
    private void ToggleTheatre() => IsTheatre = !IsTheatre;

    [RelayCommand]
    private async Task ShowSessionAsync()
    {
        if (IsEditorPage)
            await Editor.CloseCommand.ExecuteAsync(null);
        CurrentPage = Page.Session;
        await Session.RefreshHomeAsync();
    }

    // The Explorer is unavailable while a session runs (spec §5).
    [RelayCommand(CanExecute = nameof(CanShowExplorer))]
    private Task ShowExplorerAsync() => ShowExplorerPageAsync();

    private bool CanShowExplorer() => !Session.IsActive;

    private async Task ShowExplorerPageAsync()
    {
        CurrentPage = Page.Explorer;
        await Explorer.LoadAsync();
    }

    private async Task OpenClipAsync(Domain.Clips.ClipId clip)
    {
        if (await Editor.OpenAsync(clip))
            CurrentPage = Page.Editor;
        else
            await Explorer.LoadAsync();
    }

    private Task RefreshExplorerAsync() => IsExplorerPage ? Explorer.LoadAsync() : Task.CompletedTask;

    private void OnLayoutChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(IsSessionPage), nameof(IsExplorerPage), nameof(IsEditorPage), nameof(ShowPlayer), nameof(CollapsePlayer),
                     nameof(ShowSidePanels), nameof(ShowStrip), nameof(ShowHome), nameof(ShowControls),
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

    private async Task OnScannedAsync(ScanReport report)
    {
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
