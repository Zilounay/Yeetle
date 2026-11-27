using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Application.Shell;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.App.ViewModels;

// One row of the clip list: name, recording date, duration and tags; Missing, Offline and Deleted
// clips are dimmed with a badge (spec §8, layout from #9). In the sessions history (#72) the date
// is when the clip was reviewed and the badge the decision taken then, with what changed since.
public sealed class ExplorerRowViewModel
{
    public ExplorerRowViewModel(LibraryClip clip, string tags)
        : this(clip, tags, string.Format(Strings.ClipDateFormat, clip.RecordedAt), clip.Decision switch
        {
            Decision.New => Strings.DecisionNew,
            Decision.Yeeted => Strings.DecisionYeeted,
            _ => string.Empty,
        })
    {
    }

    private ExplorerRowViewModel(LibraryClip clip, string tags, string date, string decisionBadge)
    {
        Clip = clip;
        Tags = tags;
        Date = date;
        DecisionBadge = decisionBadge;
        IsYeeted = clip.Decision == Decision.Yeeted;
    }

    // A clip of a past session: the time it was reviewed (with the day when the session ran past
    // midnight or was resumed later), the decision taken then, always shown, and what changed since.
    public static ExplorerRowViewModel ForSession(SessionRow row, string tags, DateTime sessionStart)
    {
        var reviewed = row.Review.ReviewedAt is { } at
            ? string.Format(at.Date == sessionStart.Date ? Strings.ReviewedTimeFormat : Strings.ReviewedDayTimeFormat, at)
            : Strings.UnknownTime;
        var then = row.Review.Decision switch
        {
            Decision.Kept => Strings.BadgeKept,
            Decision.Yeeted => Strings.DecisionYeeted,
            _ => Strings.BadgeSkipped,
        };
        return new ExplorerRowViewModel(row.Clip, tags, reviewed, then)
        {
            IsYeeted = row.Review.Decision == Decision.Yeeted,
            IsKept = row.Review.Decision == Decision.Kept,
            DecisionNow = row.DecisionChanged ? string.Format(Strings.DecisionNow, NowWord(row.Clip.Decision)) : string.Empty,
            TagsChanged = row.TagsChanged,
        };
    }

    private static string NowWord(Decision decision) => decision switch
    {
        Decision.Kept => Strings.BadgeKept,
        Decision.Yeeted => Strings.DecisionYeeted,
        _ => Strings.DecisionNew,
    };

    public LibraryClip Clip { get; }
    public string Name => Clip.FileName;
    public string Date { get; }
    public string Duration => Clip.Duration is { } duration ? TimeFormat.Clip(duration) : Strings.UnknownDuration;
    public string Tags { get; }
    // What a past session's clip became since: "now Kept" (#72).
    public string DecisionNow { get; private init; } = string.Empty;
    public bool HasDecisionNow => DecisionNow.Length > 0;
    public bool TagsChanged { get; private init; }
    public bool IsKept { get; private init; }
    public string StateBadge => Clip.State switch
    {
        FileState.Missing => Strings.StateMissing,
        FileState.Offline => Strings.StateOffline,
        FileState.Deleted => Strings.StateDeleted,
        FileState.NeedsAttention => Strings.StateNeedsAttention,
        _ => string.Empty,
    };
    public bool HasStateBadge => StateBadge.Length > 0;
    public bool IsDimmed => Clip.State != FileState.Present;
    // Shown when New or Yeeted clips are listed, so they stand out from Kept ones.
    public string DecisionBadge { get; }
    public bool HasDecisionBadge => DecisionBadge.Length > 0;
    public bool IsYeeted { get; private init; }
}

// A committed session in the Explorer's Sessions mode (#72): when it started, how long it took and
// what it decided.
public sealed class PastSessionViewModel(PastSession session)
{
    public PastSession Session { get; } = session;
    public string Title { get; } = string.Format(Strings.PastSessionTitle, session.StartedAt);
    public string Detail { get; } = string.Format(Strings.PastSessionDetail,
        Math.Max(1, (int)Math.Round(session.Duration.TotalMinutes)), session.Kept, session.Yeeted, session.Skipped);
}

// A node of the tag filter tree (#83): a box title, or a tag to tick. What sits under a tag is
// folded into it, as folders in Windows Explorer, so a game's hundreds of tags only show when
// the game is opened.
public sealed partial class TagFilterItem(string label, TagId? tag, BoxId? box, IReadOnlyList<TagFilterItem> children)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    // Box titles start open, so opening a tag shows its boxes with their tags; tags start folded.
    [ObservableProperty]
    private bool _isExpanded = tag is null;

    // How many clips of the library carry the tag, shown smaller and greyer after its name.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(HasCount))]
    private int _count;

    public string Label { get; } = label;
    public TagId? Tag { get; } = tag;
    public BoxId? Box { get; } = box;
    public IReadOnlyList<TagFilterItem> Children { get; } = children;
    public bool IsTag => Tag is not null;
    public bool IsHeader => Tag is null;
    public bool HasCount => Count > 0;
    public string CountText => string.Format(Strings.TagFilterCount, Count);

    public IEnumerable<TagFilterItem> SelfAndBelow() => Children.SelectMany(c => c.SelfAndBelow()).Prepend(this);

    // The same boxes and tags, with the same names, in the same order.
    public bool SameShape(TagFilterItem other) =>
        Label == other.Label && Tag == other.Tag && Box == other.Box && Children.Count == other.Children.Count
        && Children.Zip(other.Children).All(pair => pair.First.SameShape(pair.Second));
}

// The Explorer (spec §8): every clip of the library, filtered, sorted and acted on.
public sealed partial class ExplorerViewModel : ViewModelBase
{
    private const string TagSeparator = ", ";
    private const string SortUp = " ▲";
    private const string SortDown = " ▼";

    private readonly ClipLibrary _library;
    private readonly FileOperations _files;
    private readonly PlayerViewModel _player;
    private readonly IDialogs _dialogs;
    private readonly IShell _shell;
    private readonly SessionService _sessions;
    private readonly SettingsService _settings;
    private IReadOnlyList<LibraryClip> _all = [];
    private Dictionary<ClipId, LibraryClip> _byId = [];
    private IReadOnlyList<PastSession> _history = [];
    private TagCatalog _catalog = TagCatalog.Empty;
    private IReadOnlyList<ExplorerRowViewModel> _selected = [];
    // Set while the filters are filled in, so each change does not query again.
    private bool _loading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showKept = true;

    [ObservableProperty]
    private bool _showNew;

    [ObservableProperty]
    private bool _showYeeted;

    [ObservableProperty]
    private bool _showPresent = true;

    [ObservableProperty]
    private bool _showMissing = true;

    [ObservableProperty]
    private bool _showOffline = true;

    [ObservableProperty]
    private bool _showDeleted;

    [ObservableProperty]
    private bool _showNeedsAttention = true;

    [ObservableProperty]
    private bool _incompleteOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private LibrarySort _sort = LibrarySort.RecordedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private bool _descending = true;

    [ObservableProperty]
    private string _matchText = string.Empty;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool _isEmpty;

    // The sessions history (#72): the same table, seen from the sessions' side. The filter column
    // then lists the committed sessions, and the filters apply inside the session picked.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClipsMode), nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader),
        nameof(ShowPickASession), nameof(ShowEmpty))]
    private bool _isSessionsMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPastSessions), nameof(ShowNoPastSessions), nameof(ShowPickASession))]
    private IReadOnlyList<PastSessionViewModel> _pastSessions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPickASession))]
    private PastSessionViewModel? _selectedSession;

    // The decisions taken in the session picked; New there means skipped.
    [ObservableProperty]
    private bool _showSessionKept = true;

    [ObservableProperty]
    private bool _showSessionYeeted = true;

    [ObservableProperty]
    private bool _showSessionSkipped = true;

    // Review order by default.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private SessionSort _sessionSort = SessionSort.Position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private bool _sessionDescending;

    public bool IsClipsMode => !IsSessionsMode;

    public bool HasPastSessions => PastSessions.Count > 0;

    public bool ShowNoPastSessions => IsLoaded && !HasPastSessions;

    public bool ShowPickASession => IsSessionsMode && HasPastSessions && SelectedSession is null;

    public ExplorerViewModel(ClipLibrary library, FileOperations files, PlayerViewModel player, IDialogs dialogs, IShell shell,
        SessionService sessions, SettingsService settings)
    {
        _sessions = sessions;
        _settings = settings;
        _library = library;
        _files = files;
        _player = player;
        _dialogs = dialogs;
        _shell = shell;
        PropertyChanged += OnFilterChanged;
        ShowFilters(settings.Values.LibraryView.LastUsed is { } last
            ? (last.Decisions, last.States)
            : (settings.Values.LibraryView.DefaultDecisions, settings.Values.LibraryView.DefaultStates));
    }

    // Swapped as a whole: adding rows one by one makes the list react to each of them (#78).
    [ObservableProperty]
    private IReadOnlyList<ExplorerRowViewModel> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagFilters))]
    private IReadOnlyList<TagFilterItem> _tagFilters = [];

    // Until the first load is done, placeholders show in place of the list and the filters (#78).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool _isLoaded;

    // Set while the library is read again or an action runs, for the thin progress bar (#78).
    [ObservableProperty]
    private bool _isBusy;

    public bool ShowEmpty => IsLoaded && IsEmpty && !ShowPickASession;

    public bool HasTagFilters => AllTagFilters().Any(f => f.IsTag);

    public string NameHeader => Strings.ColumnName + Arrow(LibrarySort.Name);
    public string DateHeader => (IsSessionsMode ? Strings.ColumnReviewed : Strings.ColumnRecorded) + Arrow(LibrarySort.RecordedAt);
    public string DurationHeader => Strings.ColumnDuration + Arrow(LibrarySort.Duration);

    public bool HasSelection => _selected.Count > 0;

    public bool HasSingleSelection => _selected.Count == 1;

    // Yeetle's own entries on top of the Windows context menu, in this order. Only those that apply
    // to the selection are offered: Open and Show in folder need a single clip (#80).
    private IEnumerable<(string Label, IRelayCommand Command)> MenuEntries() =>
        new (string Label, IRelayCommand Command)[]
        {
            (Strings.OpenInYeetle, OpenCommand), (Strings.ShowInFolder, ShowInFolderCommand),
            (Strings.ShowMarkers, ShowMarkersCommand), (Strings.SetKept, SetKeptCommand), (Strings.SetYeeted, SetYeetedCommand),
            (Strings.RenameByTags, RenameByTagsCommand), (Strings.RestoreNames, RestoreNamesCommand),
            (Strings.WipeData, WipeCommand), (Strings.DeleteFromDisk, DeleteCommand),
        }.Where(entry => entry.Command.CanExecute(null));

    // Raised to open a clip in the editor (spec §8).
    public event EventHandler<ClipId>? OpenRequested;

    // Loads the library again, keeping the filters; called when the Explorer is shown and after
    // every action.
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var history = _sessions.LoadHistoryAsync();
            (_all, var catalog) = await _library.LoadWithCatalogAsync();
            _byId = _all.ToDictionary(c => c.Id);
            _history = await history;
            FillPastSessions();
            FillTagFilters(catalog);
            _catalog = catalog;
            CountTags();
            Apply();
            IsLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // The view hands over the list's selection whenever it changes.
    public void SetSelection(IEnumerable<object> selected)
    {
        _selected = selected.OfType<ExplorerRowViewModel>().ToList();
        SelectionText = _selected.Count == 0 ? string.Empty : string.Format(Strings.SelectedCount, _selected.Count);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        foreach (var command in new IRelayCommand[]
                 {
                     OpenCommand, ShowInFolderCommand, SetKeptCommand, SetYeetedCommand,
                     ShowMarkersCommand, RenameByTagsCommand, RestoreNamesCommand, WipeCommand, DeleteCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    // The files a drag takes (spec §8): Missing, Offline and Deleted clips are left out with a notice.
    public IReadOnlyList<string> PathsToDrag()
    {
        var present = _selected.Where(r => r.Clip.State == FileState.Present).Select(r => r.Clip.FullPath).ToList();
        var leftOut = _selected.Count - present.Count;
        Notice = leftOut > 0 ? string.Format(Strings.DragLeftOut, leftOut) : string.Empty;
        return present;
    }

    // Right-click: Windows' own menu for the selected files, Yeetle's entries on top (spec §8).
    // Files that are not on disk get Yeetle's entries only.
    public async Task ShowMenuAsync(nint window, int screenX, int screenY)
    {
        var paths = _selected.All(r => r.Clip.State == FileState.Present) ? _selected.Select(r => r.Clip.FullPath).ToList() : [];
        var entries = MenuEntries().ToList();
        if (_shell.ShowFileMenu(window, screenX, screenY, paths, entries.Select(e => e.Label).ToList()) is { } chosen)
            await RunAsync(entries[chosen].Command);
    }

    [RelayCommand]
    private void SortBy(LibrarySort column)
    {
        if (IsSessionsMode)
        {
            // A session reads in review order: a column starts in its natural order, a second click
            // turns it around.
            var sort = ForSession(column);
            SessionDescending = sort == SessionSort && !SessionDescending;
            SessionSort = sort;
            return;
        }
        // The same column again turns the order around; dates start newest first.
        Descending = column == Sort ? !Descending : column != LibrarySort.Name;
        Sort = column;
    }

    [RelayCommand]
    private void ShowClips() => IsSessionsMode = false;

    [RelayCommand]
    private void ShowSessions() => IsSessionsMode = true;

    [RelayCommand]
    private void ClearFilters()
    {
        // Back to the defaults chosen in Settings, which are then what opens next time (#20).
        var view = _settings.Values.LibraryView;
        ShowFilters((view.DefaultDecisions, view.DefaultStates));
        _ = _settings.ChangeAsync(s => s.LibraryView.LastUsed = null);
        _loading = true;
        foreach (var filter in AllTagFilters())
            filter.IsChecked = false;
        IncompleteOnly = false;
        (ShowSessionKept, ShowSessionYeeted, ShowSessionSkipped) = (true, true, true);
        SearchText = string.Empty;
        _loading = false;
        Apply();
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void Open() => OpenRequested?.Invoke(this, _selected[0].Clip.Id);

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void ShowInFolder() => _shell.ShowInFolder(_selected[0].Clip.FullPath);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task SetKeptAsync() => SetDecisionAsync(Decision.Kept);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task SetYeetedAsync() => SetDecisionAsync(Decision.Yeeted);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task ShowMarkersAsync() => _dialogs.ShowMarkerSheetAsync(
        MarkerSheet.Write(_selected.Select(r => r.Clip), _catalog.Vocabulary, Strings.MarkerSheetTags, Strings.MarkerSheetMarkers));

    // Only kept clips are ever renamed (spec §5).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameByTagsAsync()
    {
        var kept = _selected.Where(r => r.Clip.Decision == Decision.Kept).Select(r => r.Clip.Id).ToList();
        if (kept.Count == 0)
        {
            Notice = Strings.RenameOnlyKept;
            return;
        }
        await RunFileOperationAsync(async () =>
        {
            var preview = await BusyAsync(_files.PlanRenameAsync(kept));
            if (preview.ToRename.Count == 0)
            {
                Notice = Strings.NothingToRename;
                return;
            }
            if (!await _dialogs.ConfirmRenameAsync(preview, fromSession: false))
                return;
            var outcomes = await _files.RenameAsync(preview);
            var renamed = outcomes.Where(o => o.Succeeded).Select(o => o.Clip).ToList();
            Notice = string.Format(Strings.FilesRenamed, renamed.Count);
            if (await _dialogs.ShowFileResultsAsync(Strings.RenameResultsTitle, outcomes, canRestoreNames: renamed.Count > 0,
                    preview.FirstRenamedPath(outcomes)))
                await RestoreAsync(renamed);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task RestoreNamesAsync() => RunFileOperationAsync(() => RestoreAsync(_selected.Select(r => r.Clip.Id).ToList()));

    // Asked with the count, after a backup (spec §8).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task WipeAsync()
    {
        var clips = _selected.Select(r => r.Clip.Id).ToList();
        var answer = await _dialogs.ChooseAsync(Strings.WipeTitle, string.Format(Strings.WipeMessage, clips.Count),
            [(Strings.WipeData, true)]);
        if (answer != true)
            return;
        await _library.WipeAsync(clips);
        Notice = string.Format(Strings.ClipsWiped, clips.Count);
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteAsync() => RunFileOperationAsync(async () =>
    {
        var plan = await BusyAsync(_files.PlanDeleteAsync(_selected.Select(r => r.Clip.Id).ToList()));
        if (plan.Items.Count == 0)
        {
            Notice = Strings.NothingToDelete;
            return;
        }
        if (!await _dialogs.ConfirmDeleteAsync(plan, fromSession: false))
            return;
        var outcomes = await _files.DeleteAsync(plan);
        if (outcomes.Any(o => !o.Succeeded))
            await _dialogs.ShowFileResultsAsync(Strings.DeleteResultsTitle, outcomes, canRestoreNames: false);
        Notice = string.Format(Strings.FilesDeleted, outcomes.Count(o => o.Succeeded));
    });

    private static async Task RunAsync(IRelayCommand command)
    {
        if (command is IAsyncRelayCommand asyncCommand)
            await asyncCommand.ExecuteAsync(null);
        else
            command.Execute(null);
    }

    private async Task SetDecisionAsync(Decision decision)
    {
        var clips = _selected.Select(r => r.Clip.Id).ToList();
        await BusyAsync(_library.SetDecisionAsync(clips, decision));
        Notice = string.Format(decision == Decision.Kept ? Strings.ClipsSetKept : Strings.ClipsSetYeeted, clips.Count);
        await LoadAsync();
    }

    private async Task RestoreAsync(IReadOnlyCollection<ClipId> clips)
    {
        var restored = await _files.RestoreNamesAsync(clips);
        if (restored.Count == 0)
        {
            Notice = Strings.NoNamesToRestore;
            return;
        }
        await _dialogs.ShowFileResultsAsync(Strings.RestoreResultsTitle, restored, canRestoreNames: false);
        Notice = string.Format(Strings.NamesRestored, restored.Count(o => o.Succeeded));
    }

    // The player lets go of its files first: an opened clip would otherwise be in use.
    private async Task RunFileOperationAsync(Func<Task> operation)
    {
        try
        {
            await _player.ReleaseFilesAsync();
            await operation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Data.Common.DbException)
        {
            Notice = string.Format(Strings.FileOperationsFailed, e.Message);
        }
        await LoadAsync();
    }

    // Only when the tags changed: with the starter tags this tree has over a thousand nodes, and
    // rebuilding it on every visit froze the window (#78). What was ticked or opened stays so.
    private void FillTagFilters(TagCatalog catalog)
    {
        var vocabulary = catalog.Vocabulary;
        var roots = Nodes(null);
        if (roots.Count == TagFilters.Count && roots.Zip(TagFilters).All(pair => pair.First.SameShape(pair.Second)))
            return;
        var old = AllTagFilters().ToList();
        var checkedTags = old.Where(f => f.IsChecked).Select(f => f.Tag).OfType<TagId>().ToHashSet();
        var openTags = old.Where(f => f is { IsTag: true, IsExpanded: true }).Select(f => f.Tag).OfType<TagId>().ToHashSet();
        var closedBoxes = old.Where(f => f is { IsHeader: true, IsExpanded: false }).Select(f => f.Box).OfType<BoxId>().ToHashSet();
        foreach (var filter in old)
            filter.PropertyChanged -= OnTagFilterChanged;
        foreach (var filter in roots.SelectMany(r => r.SelfAndBelow()))
        {
            if (filter.Tag is { } tag)
            {
                filter.IsChecked = checkedTags.Contains(tag);
                filter.IsExpanded = openTags.Contains(tag);
            }
            else if (filter.Box is { } box)
            {
                filter.IsExpanded = !closedBoxes.Contains(box);
            }
            filter.PropertyChanged += OnTagFilterChanged;
        }
        TagFilters = roots;

        // A named box is a title holding its tags; the tags of an unnamed box sit right under the
        // tag above it.
        List<TagFilterItem> Nodes(TagId? parent)
        {
            var nodes = new List<TagFilterItem>();
            foreach (var box in vocabulary.BoxesUnder(parent))
            {
                var tags = vocabulary.TagsIn(box.Id).Select(tag => new TagFilterItem(tag.Name, tag.Id, null, Nodes(tag.Id))).ToList();
                if (box.IsNamed)
                    nodes.Add(new TagFilterItem(box.Name, null, box.Id, tags));
                else
                    nodes.AddRange(tags);
            }
            return nodes;
        }
    }

    // How many clips of the library carry each tag.
    private void CountTags()
    {
        var counts = _all.SelectMany(c => c.Tags).CountBy(t => t).ToDictionary();
        foreach (var filter in AllTagFilters())
            filter.Count = filter.Tag is { } tag ? counts.GetValueOrDefault(tag) : 0;
    }

    private IEnumerable<TagFilterItem> AllTagFilters() => TagFilters.SelectMany(f => f.SelfAndBelow());

    private void OnTagFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagFilterItem.IsChecked) && !_loading)
            Apply();
    }

    private void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The decision and file-state filters used last are remembered for next time (#20).
        if (!_loading && e.PropertyName is nameof(ShowKept) or nameof(ShowNew) or nameof(ShowYeeted) or nameof(ShowPresent)
                or nameof(ShowMissing) or nameof(ShowOffline) or nameof(ShowDeleted) or nameof(ShowNeedsAttention))
            _ = _settings.ChangeAsync(s => s.LibraryView.LastUsed = new ExplorerFilters
            {
                Decisions = ShownDecisions().ToList(),
                States = ShownStates().ToList(),
            });
        if (!_loading && e.PropertyName is nameof(SearchText) or nameof(ShowKept) or nameof(ShowNew) or nameof(ShowYeeted)
                or nameof(ShowPresent) or nameof(ShowMissing) or nameof(ShowOffline) or nameof(ShowDeleted)
                or nameof(ShowNeedsAttention) or nameof(IncompleteOnly) or nameof(Sort) or nameof(Descending)
                or nameof(IsSessionsMode) or nameof(SelectedSession) or nameof(ShowSessionKept) or nameof(ShowSessionYeeted)
                or nameof(ShowSessionSkipped) or nameof(SessionSort) or nameof(SessionDescending))
            Apply();
    }

    private void Apply()
    {
        if (IsSessionsMode)
            ApplySession();
        else
            ApplyClips();
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowNoPastSessions));
    }

    // The clips of the session picked, as they were decided then (#72).
    private void ApplySession()
    {
        if (SelectedSession?.Session is not { } session)
        {
            Rows = [];
            MatchText = string.Empty;
            IsEmpty = false;
            SetSelection([]);
            return;
        }
        var decisions = new HashSet<Decision>();
        if (ShowSessionKept)
            decisions.Add(Decision.Kept);
        if (ShowSessionYeeted)
            decisions.Add(Decision.Yeeted);
        if (ShowSessionSkipped)
            decisions.Add(Decision.New);
        var tags = AllTagFilters().Where(f => f.IsChecked).Select(f => f.Tag).OfType<TagId>().ToList();
        var vocabulary = _catalog.Vocabulary;
        var shown = SessionQuery.Apply(session, _byId, decisions, tags, SearchText, vocabulary, SessionSort, SessionDescending);
        Rows = shown.Select(row => ExplorerRowViewModel.ForSession(row,
            string.Join(TagSeparator, vocabulary.NamesOf(row.Clip.Tags)), session.StartedAt)).ToList();
        MatchText = string.Format(Strings.MatchCount, shown.Count, session.Clips.Count);
        IsEmpty = shown.Count == 0;
        SetSelection([]);
    }

    // A committed session never changes, so the list is rebuilt only when sessions were added; the
    // one picked stays picked (the list control drops its selection when its items are replaced).
    private void FillPastSessions()
    {
        if (_history.Select(s => s.Id).SequenceEqual(PastSessions.Select(p => p.Session.Id)))
            return;
        var picked = SelectedSession?.Session.Id;
        PastSessions = _history.Select(s => new PastSessionViewModel(s)).ToList();
        SelectedSession = PastSessions.FirstOrDefault(p => p.Session.Id == picked);
    }

    // The defaults chosen in Settings, which also forget the last filters used (#20).
    public void UseDefaultFilters()
    {
        var view = _settings.Values.LibraryView;
        ShowFilters((view.DefaultDecisions, view.DefaultStates));
        Apply();
    }

    private void ShowFilters((IReadOnlyCollection<Decision> Decisions, IReadOnlyCollection<FileState> States) filters)
    {
        _loading = true;
        (ShowKept, ShowNew, ShowYeeted) = (filters.Decisions.Contains(Decision.Kept), filters.Decisions.Contains(Decision.New),
            filters.Decisions.Contains(Decision.Yeeted));
        (ShowPresent, ShowMissing, ShowOffline, ShowDeleted, ShowNeedsAttention) = (filters.States.Contains(FileState.Present),
            filters.States.Contains(FileState.Missing), filters.States.Contains(FileState.Offline),
            filters.States.Contains(FileState.Deleted), filters.States.Contains(FileState.NeedsAttention));
        _loading = false;
    }

    private IEnumerable<Decision> ShownDecisions()
    {
        if (ShowKept)
            yield return Decision.Kept;
        if (ShowNew)
            yield return Decision.New;
        if (ShowYeeted)
            yield return Decision.Yeeted;
    }

    private IEnumerable<FileState> ShownStates()
    {
        if (ShowPresent)
            yield return FileState.Present;
        if (ShowMissing)
            yield return FileState.Missing;
        if (ShowOffline)
            yield return FileState.Offline;
        if (ShowDeleted)
            yield return FileState.Deleted;
        if (ShowNeedsAttention)
            yield return FileState.NeedsAttention;
    }

    private void ApplyClips()
    {
        var decisions = new HashSet<Decision>();
        if (ShowKept)
            decisions.Add(Decision.Kept);
        if (ShowNew)
            decisions.Add(Decision.New);
        if (ShowYeeted)
            decisions.Add(Decision.Yeeted);
        var states = new HashSet<FileState>();
        if (ShowPresent)
            states.Add(FileState.Present);
        if (ShowMissing)
            states.Add(FileState.Missing);
        if (ShowOffline)
            states.Add(FileState.Offline);
        if (ShowDeleted)
            states.Add(FileState.Deleted);
        if (ShowNeedsAttention)
            states.Add(FileState.NeedsAttention);
        var tags = AllTagFilters().Where(f => f.IsChecked).Select(f => f.Tag).OfType<TagId>().ToList();
        var filter = new LibraryFilter(decisions, states, tags, SearchText, IncompleteOnly);
        var vocabulary = _catalog.Vocabulary;
        var shown = LibraryQuery.Apply(_all, filter, vocabulary, Sort, Descending);
        Rows = shown.Select(clip => new ExplorerRowViewModel(clip, string.Join(TagSeparator, vocabulary.NamesOf(clip.Tags)))).ToList();
        MatchText = string.Format(Strings.MatchCount, shown.Count, _all.Count);
        IsEmpty = shown.Count == 0;
        SetSelection([]);
    }

    // Work the user waits for without a dialog shows the thin progress bar (#78).
    private async Task BusyAsync(Task work) => await BusyAsync(WithResult(work));

    private async Task<T> BusyAsync<T>(Task<T> work)
    {
        IsBusy = true;
        try
        {
            return await work;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<bool> WithResult(Task work)
    {
        await work;
        return true;
    }

    private string Arrow(LibrarySort column) => IsSessionsMode
        ? ForSession(column) != SessionSort ? string.Empty : SessionDescending ? SortDown : SortUp
        : column != Sort ? string.Empty : Descending ? SortDown : SortUp;

    // The same header buttons sort a session: the date column is when the clip was reviewed.
    private static SessionSort ForSession(LibrarySort column) => column switch
    {
        LibrarySort.Name => SessionSort.Name,
        LibrarySort.Duration => SessionSort.Duration,
        _ => SessionSort.ReviewedAt,
    };
}
